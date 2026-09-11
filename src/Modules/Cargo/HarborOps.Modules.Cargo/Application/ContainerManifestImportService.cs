using System.Globalization;
using System.Xml;
using ClosedXML.Excel;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class ContainerManifestImportService(DbContext dbContext)
{
    internal const long MaxFileSizeBytes = 5 * 1024 * 1024;
    internal const int MaxDataRows = 5000;

    public async Task<ContainerManifestPreviewResponse> PreviewAsync(Guid bookingId, Stream file, string fileName, CancellationToken cancellationToken)
    {
        await EnsureBookingExistsAsync(bookingId, cancellationToken);
        return await ParseAndValidateAsync(file, fileName, cancellationToken);
    }

    public async Task<ContainerManifestImportResponse> ImportAsync(Guid bookingId, Stream file, string fileName, CancellationToken cancellationToken)
    {
        await CargoBookingGuard.EnsureOpenAsync(dbContext, bookingId, cancellationToken);
        var preview = await ParseAndValidateAsync(file, fileName, cancellationToken);
        if (preview.InvalidCount > 0) throw new ContainerManifestImportValidationException(preview.Rows);
        var numbers = preview.Rows.Select(row => Container.NormalizeNumber(row.ContainerNumber!)).ToArray();
        var existing = await dbContext.Set<Container>().AsNoTracking().Where(container => numbers.Contains(container.ContainerNumber)).Select(container => container.ContainerNumber).ToListAsync(cancellationToken);
        if (existing.Count > 0) throw new DuplicateContainerNumberException(existing.OrderBy(value => value, StringComparer.Ordinal).First());
        var entities = preview.Rows.Select(row => Container.Register(Container.NormalizeNumber(row.ContainerNumber!), row.ContainerType!.Trim().ToUpperInvariant(), CargoStatus.Registered, cargoBookingId: bookingId, portOfLoadingCode: ContainerEntryValidator.NormalizePort(row.PortOfLoadingCode!), portOfDischargeCode: ContainerEntryValidator.NormalizePort(row.PortOfDischargeCode!), weightKg: row.WeightKg!.Value)).ToList();
        await dbContext.Set<Container>().AddRangeAsync(entities, cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            throw new DuplicateContainerNumberException(numbers.OrderBy(value => value, StringComparer.Ordinal).First());
        }
        return new ContainerManifestImportResponse(entities.Count, entities.Select(container => container.Id).ToList());
    }

    private async Task<ContainerManifestPreviewResponse> ParseAndValidateAsync(Stream file, string fileName, CancellationToken cancellationToken)
    {
        var rows = await ParseAsync(file, fileName, cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var results = rows.Select(row =>
        {
            var request = new AddCargoContainerRequest { ContainerNumber = row.ContainerNumber, ContainerType = row.ContainerType, PortOfLoadingCode = row.PortOfLoadingCode, PortOfDischargeCode = row.PortOfDischargeCode, WeightKg = row.WeightKg };
            var errors = new Dictionary<string, string[]>(ContainerEntryValidator.Validate(request), StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(row.ContainerNumber) && !seen.Add(Container.NormalizeNumber(row.ContainerNumber))) errors["containerNumber"] = ["Container number is duplicated in this file."];
            return new ContainerManifestImportRow(row.RowNumber, row.ContainerNumber?.Trim().ToUpperInvariant(), row.ContainerType?.Trim().ToUpperInvariant(), row.PortOfLoadingCode?.Trim().ToUpperInvariant(), row.PortOfDischargeCode?.Trim().ToUpperInvariant(), row.WeightKg, errors);
        }).ToList();
        return new ContainerManifestPreviewResponse(results.Count, results.Count(row => row.Errors.Count == 0), results.Count(row => row.Errors.Count > 0), results);
    }

    private static async Task<IReadOnlyList<RawRow>> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToUpperInvariant();
        if (extension is not ".XLSX" and not ".XML") throw new ContainerManifestFileException("Only .xlsx and .xml manifest files are supported.");
        return extension == ".XML" ? await ParseXmlAsync(stream, cancellationToken) : ParseWorkbook(stream);
    }

    private static List<RawRow> ParseWorkbook(Stream stream)
    {
        var tempPath = Path.ChangeExtension(Path.GetTempFileName(), ".xlsx");
        try
        {
            using (var target = File.Create(tempPath)) stream.CopyTo(target);
            using var workbook = new XLWorkbook(tempPath);
            var sheet = workbook.Worksheets.FirstOrDefault(sheet => sheet.CellsUsed().Any()) ?? throw new ContainerManifestFileException("The workbook does not contain a non-empty worksheet.");
            var header = sheet.FirstRowUsed() ?? throw new ContainerManifestFileException("The workbook does not contain a header row.");
            if (header.CellsUsed().Any(cell => cell.HasFormula)) throw new ContainerManifestFileException("Spreadsheet formulas are not supported.");
            var headerCells = header.CellsUsed().ToList();
            var normalizedHeaders = headerCells.Select(cell => NormalizeHeader(cell.GetString())).ToList();
            if (normalizedHeaders.Count != normalizedHeaders.Distinct(StringComparer.Ordinal).Count()) throw new ContainerManifestFileException("The workbook contains duplicate column headers.");
            var columns = headerCells.ToDictionary(cell => NormalizeHeader(cell.GetString()), cell => cell.Address.ColumnNumber, StringComparer.Ordinal);
            EnsureHeaders(columns.Keys);
            var rows = new List<RawRow>();
            foreach (var row in sheet.RowsUsed().Skip(1))
            {
                if (RequiredHeaders.Select(headerName => row.Cell(columns[headerName])).Any(cell => cell.HasFormula)) throw new ContainerManifestFileException("Spreadsheet formulas are not supported.");
                var values = RequiredHeaders.Select(headerName => row.Cell(columns[headerName]).GetString().Trim()).ToArray();
                if (values.All(string.IsNullOrWhiteSpace)) continue;
                if (rows.Count == MaxDataRows) throw new ContainerManifestFileException("The manifest exceeds the 5,000-row limit.");
                rows.Add(new RawRow(row.RowNumber(), values[0], values[1], values[2], values[3], ParseWeight(values[4])));
            }
            return rows;
        }
        catch (ContainerManifestFileException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ContainerManifestFileException("The uploaded file is not a valid .xlsx workbook.");
        }
        finally { File.Delete(tempPath); }
    }

    private static async Task<List<RawRow>> ParseXmlAsync(Stream stream, CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, Async = true };
        using var reader = XmlReader.Create(stream, settings);
        var document = new XmlDocument { XmlResolver = null };
        try
        {
            document.Load(reader);
        }
        catch (XmlException)
        {
            throw new ContainerManifestFileException("The uploaded file is not valid XML.");
        }
        if (document.DocumentElement?.Name != "ContainerManifest") throw new ContainerManifestFileException("XML root element must be ContainerManifest.");
        if (document.DocumentElement.ChildNodes.Cast<XmlNode>().Any(node => node is XmlElement element && element.Name != "Container")) throw new ContainerManifestFileException("ContainerManifest may contain only Container elements.");
        var nodes = document.DocumentElement.SelectNodes("Container")?.Cast<XmlElement>().ToList() ?? [];
        if (nodes.Count > MaxDataRows) throw new ContainerManifestFileException("The manifest exceeds the 5,000-row limit.");
        return nodes.Select((node, index) =>
        {
            var elements = node.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().ToList();
            if (elements.Any(element => !RequiredHeaders.Contains(NormalizeHeader(element.Name), StringComparer.Ordinal))
                || elements.Select(element => NormalizeHeader(element.Name)).Distinct(StringComparer.Ordinal).Count() != elements.Count
                || !RequiredHeaders.All(header => elements.Any(element => NormalizeHeader(element.Name) == header)))
            {
                throw new ContainerManifestFileException($"Container {index + 1} must contain only ContainerNumber, ContainerType, POL, POD, and WeightKg elements.");
            }

            string? Value(string name) => elements.Single(element => NormalizeHeader(element.Name) == name).InnerText;
            return new RawRow(index + 1, Value("CONTAINERNUMBER"), Value("CONTAINERTYPE"), Value("POL"), Value("POD"), ParseWeight(Value("WEIGHTKG")));
        }).ToList();
    }

    private static readonly string[] RequiredHeaders = ["CONTAINERNUMBER", "CONTAINERTYPE", "POL", "POD", "WEIGHTKG"];
    private static string NormalizeHeader(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    private static void EnsureHeaders(IEnumerable<string> headers) { if (!RequiredHeaders.All(header => headers.Contains(header, StringComparer.Ordinal)) || headers.Any(header => !RequiredHeaders.Contains(header, StringComparer.Ordinal))) throw new ContainerManifestFileException("The manifest must contain only ContainerNumber, ContainerType, POL, POD, and WeightKg columns."); }
    private static decimal? ParseWeight(string? value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var weight) ? weight : null;
    private async Task EnsureBookingExistsAsync(Guid bookingId, CancellationToken cancellationToken) { if (!await dbContext.Set<CargoBooking>().AsNoTracking().AnyAsync(booking => booking.Id == bookingId, cancellationToken)) throw new CargoBookingNotFoundException(bookingId); }
    private sealed record RawRow(int RowNumber, string? ContainerNumber, string? ContainerType, string? PortOfLoadingCode, string? PortOfDischargeCode, decimal? WeightKg);
}

internal sealed class ContainerManifestFileException(string message) : Exception(message);
internal sealed class ContainerManifestImportValidationException(IReadOnlyList<ContainerManifestImportRow> rows) : Exception("The manifest contains invalid rows.") { internal IReadOnlyList<ContainerManifestImportRow> Rows { get; } = rows; }

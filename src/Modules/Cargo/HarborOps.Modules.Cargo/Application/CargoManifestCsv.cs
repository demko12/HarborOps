using System.Globalization;
using System.Text;
using HarborOps.Modules.Cargo.Contracts;

namespace HarborOps.Modules.Cargo.Application;

internal static class CargoManifestCsv
{
    internal const string ContentType = "text/csv; charset=utf-8";
    internal const string MatchingCountHeader = "X-Matching-Count";

    private const char Delimiter = ',';

    private static readonly IReadOnlyList<string> Header =
    [
        "Booking number",
        "Container number",
        "Type",
        "POL",
        "POD",
        "Weight (kg)",
        "Status",
        "Vessel ID",
    ];

    private static readonly char[] CharactersRequiringQuotes = [Delimiter, '"', '\r', '\n'];
    private static readonly char[] FormulaLeadCharacters = ['=', '+', '-', '@', '\t', '\r'];

    internal static byte[] Write(CargoManifestExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        var builder = new StringBuilder();

        AppendLine(builder, Header.Select(Text));

        foreach (var row in export.Rows)
        {
            AppendLine(builder,
            [
                Text(export.BookingNumber),
                Text(row.ContainerNumber),
                Text(row.ContainerType),
                Text(row.PortOfLoadingCode),
                Text(row.PortOfDischargeCode),
                row.WeightKg?.ToString("0.000", CultureInfo.InvariantCulture) ?? string.Empty,
                Text(row.Status),
                Text(row.VesselId?.ToString("D", CultureInfo.InvariantCulture)),
            ]);
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var content = Encoding.UTF8.GetBytes(builder.ToString());

        return [.. preamble, .. content];
    }

    internal static string FileName(CargoManifestExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        var bookingNumber = new string(export.BookingNumber
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray());

        return string.Create(
            CultureInfo.InvariantCulture,
            $"manifest-{bookingNumber}-{export.GeneratedAtUtc:yyyyMMdd'T'HHmm'Z'}.csv");
    }

    internal static string Text(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0 && Array.IndexOf(FormulaLeadCharacters, text[0]) >= 0)
        {
            text = "'" + text;
        }

        return text.IndexOfAny(CharactersRequiringQuotes) < 0
            ? text
            : "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void AppendLine(StringBuilder builder, IEnumerable<string> fields)
    {
        builder.Append(string.Join(Delimiter, fields));
        builder.Append("\r\n");
    }
}

using System.Globalization;
using System.Text;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;

namespace HarborOps.UnitTests.Cargo;

public sealed class CargoManifestCsvTests
{
    private const string Header = "Booking number,Container number,Type,POL,POD,Weight (kg),Status,Vessel ID";

    private static readonly Guid BookingId = Guid.Parse("7e6d5c4b-3a29-4817-a6f5-e4d3c2b1a098");
    private static readonly Guid VesselId = Guid.Parse("0a1b2c3d-4e5f-4061-8273-94a5b6c7d8e9");

    [Fact]
    public void Write_emits_a_bom_the_header_and_one_crlf_line_per_row()
    {
        var bytes = CargoManifestCsv.Write(Export(
            "BK-2026-0145",
            Row("MSKU1234567", "42R1", "Loaded", "NLRTM", "USNYC", 12500m, VesselId)));

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3));
        Assert.Equal(
            $"{Header}\r\nBK-2026-0145,MSKU1234567,42R1,NLRTM,USNYC,12500.000,Loaded,{VesselId:D}\r\n",
            Decode(bytes));
    }

    [Fact]
    public void Write_emits_only_the_header_for_an_export_without_rows()
    {
        Assert.Equal($"{Header}\r\n", Decode(CargoManifestCsv.Write(Export("BK-2026-0145"))));
    }

    [Fact]
    public void Write_leaves_missing_legacy_values_blank()
    {
        var csv = Decode(CargoManifestCsv.Write(Export(
            "BK-2026-0145",
            Row("MSKU1234567", "20GP", "Registered", null, null, null, null))));

        Assert.EndsWith("\r\nBK-2026-0145,MSKU1234567,20GP,,,,Registered,\r\n", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_formats_weights_with_the_invariant_culture_whatever_the_current_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var csv = Decode(CargoManifestCsv.Write(Export(
                "BK-2026-0145",
                Row("MSKU1234567", "42R1", "Loaded", "NLRTM", "USNYC", 1234567.5m, null),
                Row("MSKU7654321", "42R1", "Loaded", "NLRTM", "USNYC", 0.001m, null))));

            Assert.Contains(",1234567.500,", csv, StringComparison.Ordinal);
            Assert.Contains(",0.001,", csv, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("A,B", "\"A,B\"")]
    [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    [InlineData("line\r\nbreak", "\"line\r\nbreak\"")]
    [InlineData("=SUM(A1:A2)", "'=SUM(A1:A2)")]
    [InlineData("+1234", "'+1234")]
    [InlineData("-10", "'-10")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tTAB", "'\tTAB")]
    [InlineData("\rCR", "\"'\rCR\"")]
    [InlineData("=HYPERLINK(\"http://x\",\"a,b\")", "\"'=HYPERLINK(\"\"http://x\"\",\"\"a,b\"\")\"")]
    [InlineData("MSKU=1", "MSKU=1")]
    [InlineData("", "")]
    public void Text_escapes_delimiters_quotes_line_breaks_and_formula_prefixes(string value, string expected)
    {
        Assert.Equal(expected, CargoManifestCsv.Text(value));
    }

    [Fact]
    public void Write_protects_every_text_field_against_formulas()
    {
        var csv = Decode(CargoManifestCsv.Write(Export(
            "=BOOKING",
            Row("+CONTAINER", "-TYPE", "@STATUS", "=POL", "+POD", 1m, null))));

        Assert.EndsWith("\r\n'=BOOKING,'+CONTAINER,'-TYPE,'=POL,'+POD,1.000,'@STATUS,\r\n", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_keeps_unicode_text_intact()
    {
        var csv = Decode(CargoManifestCsv.Write(Export(
            "BK-ČĆŽ-Đš",
            Row("MSKU1234567", "42R1", "Loaded", "NLRTM", "USNYC", 1m, null))));

        Assert.Contains("BK-ČĆŽ-Đš,MSKU1234567", csv, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BK-2026-0145", "manifest-BK-2026-0145-20260928T1432Z.csv")]
    [InlineData("BK/2026 \"0145\"", "manifest-BK-2026--0145--20260928T1432Z.csv")]
    [InlineData("BK_ČŽ", "manifest-BK_---20260928T1432Z.csv")]
    public void FileName_is_safe_and_stamped_with_the_utc_generation_time(string bookingNumber, string expected)
    {
        Assert.Equal(expected, CargoManifestCsv.FileName(Export(bookingNumber)));
    }

    private static CargoManifestExportRow Row(
        string containerNumber,
        string containerType,
        string status,
        string? portOfLoadingCode,
        string? portOfDischargeCode,
        decimal? weightKg,
        Guid? vesselId)
    {
        return new CargoManifestExportRow(Guid.NewGuid(), containerNumber, containerType, status, portOfLoadingCode, portOfDischargeCode, weightKg, vesselId);
    }

    private static CargoManifestExport Export(string bookingNumber, params CargoManifestExportRow[] rows)
    {
        return new CargoManifestExport(
            BookingId,
            bookingNumber,
            new DateTime(2026, 9, 28, 14, 32, 5, DateTimeKind.Utc),
            new CargoManifestExportFilters(null, null, null, null, null),
            false,
            rows.Length,
            rows.Length,
            rows.Sum(row => row.WeightKg ?? 0m),
            rows.Count(row => row.WeightKg is not null),
            rows.Count(row => row.WeightKg is null),
            10_000,
            rows);
    }

    private static string Decode(byte[] bytes)
    {
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }
}

using System.Globalization;
using System.Text;

namespace HarborOps.Api.Composition.Reports.Csv;

internal static class CsvWriter
{
    internal const char Delimiter = ',';

    private static readonly char[] CharactersRequiringQuoting = [Delimiter, '"', '\n', '\r'];
    private static readonly char[] FormulaInjectionLeadCharacters = ['=', '+', '-', '@', '\t'];

    internal static async Task WriteAsync(
        Stream destination,
        IReadOnlyList<string> header,
        IEnumerable<IReadOnlyList<string>> rows,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
        {
            NewLine = "\r\n",
        };

        await writer.WriteLineAsync(FormatLine(header).AsMemory(), cancellationToken);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(FormatLine(row).AsMemory(), cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static string FormatLine(IReadOnlyList<string> values) =>
        string.Join(Delimiter, values.Select(FormatField));

    internal static string FormatField(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0
            && Array.IndexOf(FormulaInjectionLeadCharacters, text[0]) >= 0
            && !IsPlainNumber(text))
        {
            text = "'" + text;
        }

        return text.IndexOfAny(CharactersRequiringQuoting) < 0
            ? text
            : "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static bool IsPlainNumber(string text) =>
        double.TryParse(
            text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);
}

namespace HarborOps.Modules.Cargo.Domain;

internal static class CargoSafetyFormat
{
    internal const int UnNumberDigitCount = 4;

    private const string UnNumberPrefix = "UN";

    internal static bool TryNormalizeImdgClass(string? value, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim().ToUpperInvariant();

        if (!IsDivisionDigit(candidate[0]))
        {
            return false;
        }

        var position = 1;

        if (position < candidate.Length && candidate[position] == '.')
        {
            if (position + 1 >= candidate.Length || !IsDivisionDigit(candidate[position + 1]))
            {
                return false;
            }

            position += 2;
        }

        if (position < candidate.Length && char.IsAsciiLetterUpper(candidate[position]))
        {
            position++;
        }

        if (position != candidate.Length)
        {
            return false;
        }

        normalized = candidate;

        return true;
    }

    internal static bool TryNormalizeUnNumber(string? value, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim().ToUpperInvariant();

        if (candidate.StartsWith(UnNumberPrefix, StringComparison.Ordinal))
        {
            candidate = candidate[UnNumberPrefix.Length..].TrimStart();
        }

        if (candidate.Length != UnNumberDigitCount)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        normalized = UnNumberPrefix + candidate;

        return true;
    }

    private static bool IsDivisionDigit(char character)
    {
        return character is >= '1' and <= '9';
    }
}

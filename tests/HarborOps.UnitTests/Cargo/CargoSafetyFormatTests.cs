using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.UnitTests.Cargo;

public sealed class CargoSafetyFormatTests
{
    [Theory]
    [InlineData("3", "3")]
    [InlineData("  3  ", "3")]
    [InlineData("2.1", "2.1")]
    [InlineData("1.4s", "1.4S")]
    [InlineData("1.4S", "1.4S")]
    [InlineData("5.1", "5.1")]
    [InlineData("9", "9")]
    public void TryNormalizeImdgClass_accepts_and_upper_cases_a_valid_class(
        string value,
        string expected)
    {
        var accepted = CargoSafetyFormat.TryNormalizeImdgClass(value, out var normalized);

        Assert.True(accepted);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("0.1")]
    [InlineData("3.")]
    [InlineData("3.0")]
    [InlineData("12")]
    [InlineData("1.4SS")]
    [InlineData("A")]
    [InlineData("3-1")]
    public void TryNormalizeImdgClass_rejects_anything_outside_the_expected_syntax(string? value)
    {
        var accepted = CargoSafetyFormat.TryNormalizeImdgClass(value, out var normalized);

        Assert.False(accepted);
        Assert.Equal(string.Empty, normalized);
    }

    [Theory]
    [InlineData("1203", "UN1203")]
    [InlineData("UN1203", "UN1203")]
    [InlineData("un1203", "UN1203")]
    [InlineData("  UN 1203  ", "UN1203")]
    [InlineData("0004", "UN0004")]
    public void TryNormalizeUnNumber_accepts_and_prefixes_a_four_digit_number(
        string value,
        string expected)
    {
        var accepted = CargoSafetyFormat.TryNormalizeUnNumber(value, out var normalized);

        Assert.True(accepted);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("UN123")]
    [InlineData("12A4")]
    [InlineData("NA1203")]
    public void TryNormalizeUnNumber_rejects_anything_outside_the_expected_syntax(string? value)
    {
        var accepted = CargoSafetyFormat.TryNormalizeUnNumber(value, out var normalized);

        Assert.False(accepted);
        Assert.Equal(string.Empty, normalized);
    }
}

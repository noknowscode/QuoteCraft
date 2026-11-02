using QuoteCraft.Helpers;
using Xunit;

namespace QuoteCraft.Tests.Helpers;

public class SettingsValidationTests
{
    [Theory]
    [InlineData("13.0", "0", "14")]
    [InlineData("0", "", "1")]
    [InlineData("100", "25.5", "365")]
    public void Valid_defaults_pass(string tax, string markup, string days)
    {
        Assert.Null(SettingsValidation.Validate(tax, markup, days));
    }

    [Theory]
    [InlineData("abc", "0", "14", "Tax rate")]
    [InlineData("101", "0", "14", "Tax rate")]
    [InlineData("-1", "0", "14", "Tax rate")]
    [InlineData("13", "-5", "14", "Markup")]
    [InlineData("13", "x", "14", "Markup")]
    [InlineData("13", "0", "0", "valid for at least 1 day")]
    [InlineData("13", "0", "", "valid for at least 1 day")]
    public void Invalid_defaults_name_the_field(string tax, string markup, string days, string expected)
    {
        Assert.Contains(expected, SettingsValidation.Validate(tax, markup, days));
    }
}

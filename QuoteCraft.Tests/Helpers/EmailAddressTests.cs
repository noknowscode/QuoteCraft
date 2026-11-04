using QuoteCraft.Helpers;
using Xunit;

namespace QuoteCraft.Tests.Helpers;

/// <summary>The email check shared by onboarding and the client dialog.</summary>
public class EmailAddressTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("sarah@northside.ca", true)]
    [InlineData("sarah@northside", false)]
    [InlineData("sarah.northside.ca", false)]
    public void An_address_needs_an_at_and_a_dot_unless_the_field_is_empty(string? email, bool valid) =>
        Assert.Equal(valid, EmailAddress.IsValidOrEmpty(email));
}

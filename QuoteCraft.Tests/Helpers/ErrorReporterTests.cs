using QuoteCraft.Helpers;
using Xunit;

namespace QuoteCraft.Tests.Helpers;

public class ErrorReporterTests
{
    [Fact]
    public void A_reason_the_app_wrote_for_people_is_shown()
    {
        var locked = new InvalidOperationException("If it is open in another app, close it and try again.");

        Assert.Equal("The PDF could not be created. If it is open in another app, close it and try again.",
            ErrorReporter.Describe("The PDF could not be created.", locked));
    }

    [Fact]
    public void Any_other_error_asks_to_try_again()
    {
        Assert.Equal("The quote could not be reopened. Try again.",
            ErrorReporter.Describe("The quote could not be reopened.", new IOException("disk I/O error")));
    }
}

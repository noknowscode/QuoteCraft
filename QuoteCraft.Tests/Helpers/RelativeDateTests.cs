using QuoteCraft.Helpers;
using Xunit;

namespace QuoteCraft.Tests.Helpers;

public class RelativeDateTests
{
    // A fixed "now" with an offset, so the calendar words don't depend on the machine's time zone.
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 15, 30, 0, TimeSpan.FromHours(-5));

    [Theory]
    [InlineData(0, 0, 20, "Just now")]
    [InlineData(0, 0, -20 * 60, "20m ago")]
    [InlineData(0, -3, 0, "Today")]
    [InlineData(-1, 0, 0, "Yesterday")]
    [InlineData(-3, 0, 0, "3 days ago")]
    [InlineData(-14, 0, 0, "2 weeks ago")]
    [InlineData(-45, 0, 0, "1 month ago")]
    public void Past_dates_read_as_ago(int days, int hours, int seconds, string expected)
    {
        var date = Now.AddDays(days).AddHours(hours).AddSeconds(seconds > 0 ? -seconds : seconds);
        Assert.Equal(expected, RelativeDate.Format(date, Now));
    }

    [Theory]
    [InlineData(0, 3, "Today")]
    [InlineData(1, 0, "Tomorrow")]
    [InlineData(4, 0, "In 4 days")]
    [InlineData(7, 0, "In 1 week")]
    [InlineData(21, 0, "In 3 weeks")]
    [InlineData(30, 0, "In 1 month")]
    [InlineData(90, 0, "In 3 months")]
    public void Future_dates_read_as_ahead(int days, int hours, string expected)
    {
        Assert.Equal(expected, RelativeDate.Format(Now.AddDays(days).AddHours(hours), Now));
    }

    [Fact]
    public void A_valid_until_date_ahead_is_never_just_now()
    {
        // The bug this guards: a quote valid for four more weeks showed "Just now".
        Assert.Equal("In 4 weeks", RelativeDate.Format(Now.AddDays(29), Now));
    }

    [Fact]
    public void Far_dates_show_the_date()
    {
        Assert.Equal("Nov 2, 2028", RelativeDate.Format(Now.AddYears(2), Now));
        Assert.Equal("Nov 2, 2024", RelativeDate.Format(Now.AddYears(-2), Now));
    }

    [Fact]
    public void Calendar_days_use_now_offset()
    {
        // 23:30 at -5 is the next day in UTC; it is still "Today" where the user is.
        var lateToday = new DateTimeOffset(2026, 11, 2, 23, 30, 0, TimeSpan.FromHours(-5));
        Assert.Equal("Today", RelativeDate.Format(lateToday, Now));
        Assert.Equal("Tomorrow", RelativeDate.Format(lateToday.AddMinutes(31), Now));
    }
}

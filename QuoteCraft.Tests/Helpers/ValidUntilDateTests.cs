using QuoteCraft.Helpers;
using Xunit;

namespace QuoteCraft.Tests.Helpers;

/// <summary>The valid-until date between the editor's picker and the database (PRD F1.8).</summary>
public class ValidUntilDateTests
{
    [Fact]
    public void A_picked_day_is_stored_as_its_last_second_in_utc()
    {
        var picked = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(-5));

        Assert.Equal(new DateTimeOffset(2026, 12, 1, 23, 59, 59, TimeSpan.Zero), ValidUntilDate.FromPicker(picked));
    }

    [Fact]
    public void The_picker_shows_the_stored_utc_day_whatever_the_time()
    {
        var stored = new DateTimeOffset(2026, 12, 1, 23, 59, 59, TimeSpan.Zero);

        var shown = ValidUntilDate.ToPicker(stored)!.Value;

        Assert.Equal((2026, 12, 1, 12), (shown.Year, shown.Month, shown.Day, shown.Hour));
        Assert.True(ValidUntilDate.SameDay(shown, ValidUntilDate.ToPicker(ValidUntilDate.FromPicker(shown))));
        Assert.Null(ValidUntilDate.ToPicker(null));
    }

    [Theory]
    [InlineData(-1, "The valid-until date cannot be in the past.")]
    [InlineData(0, null)]
    [InlineData(30, null)]
    public void A_day_before_today_is_refused(int daysFromToday, string? message)
    {
        var now = new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);

        Assert.Equal(message, ValidUntilDate.Check(new DateTimeOffset(new DateTime(2026, 11, 4).AddDays(daysFromToday)), now));
    }
}

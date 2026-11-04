using QuoteCraft.Helpers;
using Uno.Extensions.Reactive.UI;
using Xunit;

namespace QuoteCraft.Tests.Helpers;

/// <summary>
/// The FeedView states for a message. A FeedView whose source is replaced by a newer view model's feed sees that feed's
/// first message, in which "no error" is not a change: it still has to leave the error state.
/// </summary>
public class WholeFeedViewStateSelectorTests
{
    private readonly WholeFeedViewStateSelector _selector = new();

    // Only the progress state reads the FeedView, and only while a message is loading.
    private static FeedView NoView => null!;

    [Fact]
    public void A_new_feeds_first_value_clears_the_error_state()
    {
        // Its changes hold Data only: MVUX's own selector would move the data state and leave "Error" up.
        Message<int> first = Message<int>.Initial.With().Data(5);

        Assert.Equal(["Some", "NoError", "NoProgress"], _selector.StatesFor(NoView, first));
    }

    [Fact]
    public void A_failed_load_shows_the_error_state()
    {
        Message<int> failed = Message<int>.Initial.With().Error(new IOException("disk I/O error"));

        Assert.Contains("Error", _selector.StatesFor(NoView, failed));
    }

    [Fact]
    public void An_empty_value_shows_the_none_state_without_an_error()
    {
        Message<int> empty = Message<int>.Initial.With().Data(Option<int>.None());

        Assert.Equal(["None", "NoError", "NoProgress"], _selector.StatesFor(NoView, empty));
    }
}

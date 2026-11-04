using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace QuoteCraft.Tests.Presentation;

/// <summary>
/// The navigator a model gets, without a window: it records every request and answers dialog routes ("!" qualifier)
/// from <see cref="Answer"/>, the way the dialog's model would (a result, or null for Cancel).
/// </summary>
public sealed class FakeNavigator : INavigator, IInstance<IServiceProvider>
{
    private readonly IServiceProvider _services = new ServiceCollection()
        .AddSingleton(Substitute.For<IRouteResolver>())
        .BuildServiceProvider();

    private readonly Dictionary<string, Func<object?, object?>> _answers = [];

    public IServiceProvider? Instance => _services;

    public Route? Route => null;

    /// <summary>Every request, oldest first.</summary>
    public List<NavigationRequest> Requests { get; } = [];

    /// <summary>The routes navigated to, with their qualifier ("!Confirm", "QuoteEditor", "-", "-/Main/QuoteEditor").</summary>
    public IEnumerable<string> Routes => Requests.Select(r => r.Route.FullPath());

    /// <summary>The data sent with the last request to <paramref name="route"/> (dialogs included).</summary>
    public object? DataFor(string route) =>
        Requests.LastOrDefault(r => r.Route.Base == route)?.Route.Data?.TryGetValue(string.Empty, out var data) == true ? data : null;

    /// <summary>The data a dialog's model sent back (NavigateBackWithResultAsync), or null.</summary>
    public object? BackResult =>
        Requests.LastOrDefault(r => r.Route.Qualifier == Qualifiers.NavigateBack)?.Route.Data?.TryGetValue(string.Empty, out var data) == true ? data : null;

    /// <summary>What the dialog at <paramref name="route"/> returns, given the data it was opened with.</summary>
    public FakeNavigator Answer(string route, Func<object?, object?> answer)
    {
        _answers[route] = answer;
        return this;
    }

    public FakeNavigator Answer(string route, object? result) => Answer(route, _ => result);

    /// <summary>The Confirm dialog: true presses its primary button (Delete, Discard, Upgrade).</summary>
    public FakeNavigator Confirm(bool primary) =>
        Answer("Confirm", primary ? Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary : Microsoft.UI.Xaml.Controls.ContentDialogResult.None);

    public Task<bool> CanNavigate(Route route) => Task.FromResult(true);

    public Task<NavigationResponse?> NavigateAsync(NavigationRequest request)
    {
        Requests.Add(request);
        if (request.Result is null)
        {
            return Task.FromResult<NavigationResponse?>(new NavigationResponse(request.Route, Success: true, Navigator: this));
        }

        var data = request.Route.Data?.TryGetValue(string.Empty, out var value) == true ? value : null;
        var result = request.Route.Base is { } route && _answers.TryGetValue(route, out var answer) ? answer(data) : null;
        IOption option = result is null ? Option.None<object>() : Option.Some(result);
        return Task.FromResult<NavigationResponse?>(new NavigationResultResponse(request.Route, Task.FromResult(option), Success: true, Navigator: this));
    }
}

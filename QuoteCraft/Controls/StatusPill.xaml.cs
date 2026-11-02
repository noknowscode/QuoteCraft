using Microsoft.UI.Xaml.Automation;

namespace QuoteCraft.Controls;

public sealed partial class StatusPill : UserControl
{
    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(object), typeof(StatusPill),
            new PropertyMetadata(null, (d, _) => ((StatusPill)d).Apply()));

    public StatusPill()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply();
    }

    /// <summary>The quote status (a <see cref="QuoteStatus"/> or its name).</summary>
    public object? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    private void Apply()
    {
        var name = Status?.ToString();
        if (string.IsNullOrEmpty(name) || !Enum.TryParse<QuoteStatus>(name, ignoreCase: true, out var status))
        {
            status = QuoteStatus.Draft;
        }

        Label.Text = status.ToString().ToUpperInvariant();
        AutomationProperties.SetName(this, $"Status: {status}");
        VisualStateManager.GoToState(this, status.ToString(), false);
    }
}

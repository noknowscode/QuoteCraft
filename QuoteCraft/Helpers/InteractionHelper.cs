using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace QuoteCraft.Helpers;

/// <summary>
/// Hover, press, focus, selection and keyboard activation for list cards (Toolkit CardContentControls with
/// StaticCardStyle, whose own state washes are off), set from XAML with the IsInteractive, IsSelected and Command
/// attached properties. The brushes come from the card's theme and are set again when it changes.
///
/// Interaction Tokens (change here to update globally):
///   Elevation:  Rest(0,0,0)  Hover(0,-2,4)  Pressed(0,0,2)
///   Colors:     Hover/Focus card = PrimaryBrush, Rest card = OutlineVariantBrush
///   Opacity:    Pressed = 0.92, Rest/Hover = 1.0
/// </summary>
public static class InteractionHelper
{
    // ── Elevation Tokens ─────────────────────────────────────────────
    private static readonly Vector3 ElevationRest = new(0, 0, 0);
    private static readonly Vector3 ElevationHover = new(0, -2, 4);
    private static readonly Vector3 ElevationPressed = new(0, 0, 2);

    // ── Opacity Tokens ───────────────────────────────────────────────
    private const double OpacityPressed = 0.92;

    // ── Per-element state tracking ───────────────────────────────────
    private static readonly ConditionalWeakTable<Control, PointerState> _states = new();

    private sealed class PointerState
    {
        public bool IsHovered;
        public bool IsPressed;
        public bool IsFocused;
    }

    // ── Attached Property: Command ───────────────────────────────────
    // Optional ICommand invoked on keyboard Enter/Space activation.

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached(
            "Command",
            typeof(System.Windows.Input.ICommand),
            typeof(InteractionHelper),
            new PropertyMetadata(null));

    public static System.Windows.Input.ICommand? GetCommand(DependencyObject obj) =>
        (System.Windows.Input.ICommand?)obj.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject obj, System.Windows.Input.ICommand? value) =>
        obj.SetValue(CommandProperty, value);

    // ── Attached Property: IsSelected ────────────────────────────────
    // The selected card keeps the primary card and a primary-container fill.

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.RegisterAttached(
            "IsSelected",
            typeof(bool),
            typeof(InteractionHelper),
            new PropertyMetadata(false, (d, _) =>
            {
                if (d is Control card) ApplyVisualState(card, GetState(card));
            }));

    public static bool GetIsSelected(DependencyObject obj) => (bool)obj.GetValue(IsSelectedProperty);

    public static void SetIsSelected(DependencyObject obj, bool value) => obj.SetValue(IsSelectedProperty, value);

    // ── Attached Property: RestBackground ────────────────────────────
    // The resource key of the card's resting fill (SurfaceBrush unless set).

    public static readonly DependencyProperty RestBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "RestBackground",
            typeof(string),
            typeof(InteractionHelper),
            new PropertyMetadata("SurfaceBrush"));

    public static string GetRestBackground(DependencyObject obj) => (string)obj.GetValue(RestBackgroundProperty);

    public static void SetRestBackground(DependencyObject obj, string value) => obj.SetValue(RestBackgroundProperty, value);

    // ── Attached Property: IsInteractive ─────────────────────────────

    public static readonly DependencyProperty IsInteractiveProperty =
        DependencyProperty.RegisterAttached(
            "IsInteractive",
            typeof(bool),
            typeof(InteractionHelper),
            new PropertyMetadata(false, OnIsInteractiveChanged));

    public static bool GetIsInteractive(DependencyObject obj) =>
        (bool)obj.GetValue(IsInteractiveProperty);

    public static void SetIsInteractive(DependencyObject obj, bool value) =>
        obj.SetValue(IsInteractiveProperty, value);

    private static void OnIsInteractiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control card) return;

        if ((bool)e.NewValue)
        {
            card.PointerEntered += Card_PointerEntered;
            card.PointerExited += Card_PointerExited;
            card.PointerPressed += Card_PointerPressed;
            card.PointerReleased += Card_PointerReleased;
            card.PointerCanceled += Card_PointerCanceled;
            card.GotFocus += Card_GotFocus;
            card.LostFocus += Card_LostFocus;
            card.KeyDown += Card_KeyDown;
            card.ActualThemeChanged += Card_ActualThemeChanged;
            card.IsTabStop = true;
        }
        else
        {
            card.PointerEntered -= Card_PointerEntered;
            card.PointerExited -= Card_PointerExited;
            card.PointerPressed -= Card_PointerPressed;
            card.PointerReleased -= Card_PointerReleased;
            card.PointerCanceled -= Card_PointerCanceled;
            card.GotFocus -= Card_GotFocus;
            card.LostFocus -= Card_LostFocus;
            card.KeyDown -= Card_KeyDown;
            card.ActualThemeChanged -= Card_ActualThemeChanged;
        }
    }

    // ── Event Handlers ───────────────────────────────────────────────

    // Brushes set from code keep their colour: set them again for the new theme.
    private static void Card_ActualThemeChanged(FrameworkElement sender, object args)
    {
        if (sender is Control card) ApplyVisualState(card, GetState(card));
    }

    private static void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Control card) return;
        var state = GetState(card);
        state.IsHovered = true;
        ApplyVisualState(card, state);
    }

    private static void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Control card) return;
        var state = GetState(card);
        state.IsHovered = false;
        state.IsPressed = false;
        ApplyVisualState(card, state);
    }

    private static void Card_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Control card) return;
        var state = GetState(card);
        state.IsPressed = true;
        ApplyVisualState(card, state);
    }

    private static void Card_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Control card) return;
        var state = GetState(card);
        state.IsPressed = false;
        ApplyVisualState(card, state);
    }

    private static void Card_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Control card) return;
        var state = GetState(card);
        state.IsHovered = false;
        state.IsPressed = false;
        ApplyVisualState(card, state);
    }

    private static void Card_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not Control card) return;
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Space)) return;

        e.Handled = true;

        // Press visual feedback
        var state = GetState(card);
        state.IsPressed = true;
        ApplyVisualState(card, state);

        // Invoke the attached Command if set, passing DataContext as parameter
        var command = GetCommand(card);
        var parameter = card.DataContext;
        if (command is not null && command.CanExecute(parameter))
            command.Execute(parameter);

        // Reset visual
        state.IsPressed = false;
        ApplyVisualState(card, state);
    }

    private static void Card_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not Control card) return;
        var state = GetState(card);
        state.IsFocused = true;
        ApplyVisualState(card, state);
    }

    private static void Card_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not Control card) return;
        var state = GetState(card);
        state.IsFocused = false;
        ApplyVisualState(card, state);
    }

    // ── State Machine ────────────────────────────────────────────────

    private static void ApplyVisualState(Control card, PointerState state)
    {
        var selected = GetIsSelected(card);
        card.Background = GetBrush(card, selected ? "PrimaryContainerBrush" : GetRestBackground(card));

        if (state.IsPressed)
        {
            card.Translation = ElevationPressed;
            card.Opacity = OpacityPressed;
            card.BorderBrush = GetBrush(card, "PrimaryBrush");
        }
        else if (state.IsHovered || state.IsFocused || selected)
        {
            card.Translation = state.IsHovered || state.IsFocused ? ElevationHover : ElevationRest;
            card.Opacity = 1.0;
            card.BorderBrush = GetBrush(card, "PrimaryBrush");
        }
        else
        {
            card.Translation = ElevationRest;
            card.Opacity = 1.0;
            card.BorderBrush = GetBrush(card, "OutlineVariantBrush");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static PointerState GetState(Control card) =>
        _states.GetOrCreateValue(card);

    private static Microsoft.UI.Xaml.Media.Brush GetBrush(FrameworkElement element, string key) =>
        ThemeResources.GetBrush(element, key);
}

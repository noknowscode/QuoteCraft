using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace QuoteCraft.Helpers;

/// <summary>
/// Hover, press, focus, selection and keyboard activation for list cards (a Toolkit CardContentControl, or a Border
/// on the pages not converted yet), set from XAML with the IsInteractive, IsSelected and Command attached properties.
///
/// Interaction Tokens (change here to update globally):
///   Elevation:  Rest(0,0,0)  Hover(0,-2,4)  Pressed(0,0,2)
///   Colors:     Hover/Focus border = PrimaryBrush, Rest border = OutlineVariantBrush
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
    private static readonly ConditionalWeakTable<FrameworkElement, PointerState> _states = new();

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
    // The selected card keeps the primary border and a primary-container fill.

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.RegisterAttached(
            "IsSelected",
            typeof(bool),
            typeof(InteractionHelper),
            new PropertyMetadata(false, (d, _) =>
            {
                if (d is FrameworkElement card) ApplyVisualState(card, GetState(card));
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
        if (d is not FrameworkElement border) return;

        if ((bool)e.NewValue)
        {
            border.PointerEntered += Border_PointerEntered;
            border.PointerExited += Border_PointerExited;
            border.PointerPressed += Border_PointerPressed;
            border.PointerReleased += Border_PointerReleased;
            border.PointerCanceled += Border_PointerCanceled;
            border.GotFocus += Border_GotFocus;
            border.LostFocus += Border_LostFocus;
            border.KeyDown += Border_KeyDown;
            border.ActualThemeChanged += Border_ActualThemeChanged;
            border.IsTabStop = true;
        }
        else
        {
            border.PointerEntered -= Border_PointerEntered;
            border.PointerExited -= Border_PointerExited;
            border.PointerPressed -= Border_PointerPressed;
            border.PointerReleased -= Border_PointerReleased;
            border.PointerCanceled -= Border_PointerCanceled;
            border.GotFocus -= Border_GotFocus;
            border.LostFocus -= Border_LostFocus;
            border.KeyDown -= Border_KeyDown;
            border.ActualThemeChanged -= Border_ActualThemeChanged;
        }
    }

    // ── Event Handlers ───────────────────────────────────────────────

    // Brushes set from code keep their colour: set them again for the new theme.
    private static void Border_ActualThemeChanged(FrameworkElement sender, object args)
    {
        if (sender is FrameworkElement border) ApplyVisualState(border, GetState(border));
    }

    private static void Border_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement border) return;
        var state = GetState(border);
        state.IsHovered = true;
        ApplyVisualState(border, state);
    }

    private static void Border_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement border) return;
        var state = GetState(border);
        state.IsHovered = false;
        state.IsPressed = false;
        ApplyVisualState(border, state);
    }

    private static void Border_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement border) return;
        var state = GetState(border);
        state.IsPressed = true;
        ApplyVisualState(border, state);
    }

    private static void Border_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement border) return;
        var state = GetState(border);
        state.IsPressed = false;
        ApplyVisualState(border, state);
    }

    private static void Border_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement border) return;
        var state = GetState(border);
        state.IsHovered = false;
        state.IsPressed = false;
        ApplyVisualState(border, state);
    }

    private static void Border_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not FrameworkElement border) return;
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Space)) return;

        e.Handled = true;

        // Press visual feedback
        var state = GetState(border);
        state.IsPressed = true;
        ApplyVisualState(border, state);

        // Invoke the attached Command if set, passing DataContext as parameter
        var command = GetCommand(border);
        var parameter = border.DataContext;
        if (command is not null && command.CanExecute(parameter))
            command.Execute(parameter);

        // Reset visual
        state.IsPressed = false;
        ApplyVisualState(border, state);
    }

    private static void Border_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement border) return;
        var state = GetState(border);
        state.IsFocused = true;
        ApplyVisualState(border, state);
    }

    private static void Border_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement border) return;
        var state = GetState(border);
        state.IsFocused = false;
        ApplyVisualState(border, state);
    }

    // ── State Machine ────────────────────────────────────────────────

    private static void ApplyVisualState(FrameworkElement border, PointerState state)
    {
        var selected = GetIsSelected(border);
        SetBackground(border, GetBrush(border, selected ? "PrimaryContainerBrush" : GetRestBackground(border)));

        if (state.IsPressed)
        {
            border.Translation = ElevationPressed;
            border.Opacity = OpacityPressed;
            SetBorderBrush(border, GetBrush(border, "PrimaryBrush"));
        }
        else if (state.IsHovered || state.IsFocused || selected)
        {
            border.Translation = state.IsHovered || state.IsFocused ? ElevationHover : ElevationRest;
            border.Opacity = 1.0;
            SetBorderBrush(border, GetBrush(border, "PrimaryBrush"));
        }
        else
        {
            border.Translation = ElevationRest;
            border.Opacity = 1.0;
            SetBorderBrush(border, GetBrush(border, "OutlineVariantBrush"));
        }
    }

    private static void SetBackground(FrameworkElement element, Microsoft.UI.Xaml.Media.Brush brush)
    {
        if (element is Control control) control.Background = brush;
        else if (element is Border border) border.Background = brush;
    }

    private static void SetBorderBrush(FrameworkElement element, Microsoft.UI.Xaml.Media.Brush brush)
    {
        if (element is Control control) control.BorderBrush = brush;
        else if (element is Border border) border.BorderBrush = brush;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static PointerState GetState(FrameworkElement border) =>
        _states.GetOrCreateValue(border);

    private static Microsoft.UI.Xaml.Media.Brush GetBrush(FrameworkElement element, string key) =>
        ThemeResources.GetBrush(element, key);
}

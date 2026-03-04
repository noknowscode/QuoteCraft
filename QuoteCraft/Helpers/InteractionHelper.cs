using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Input;

namespace QuoteCraft.Helpers;

/// <summary>
/// Unified interaction system for all interactive card elements.
/// Provides declarative state management via the IsInteractive attached property
/// with hover, press, focus, and rest states.
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
    private static readonly ConditionalWeakTable<Border, PointerState> _states = new();

    private sealed class PointerState
    {
        public bool IsHovered;
        public bool IsPressed;
        public bool IsFocused;
    }

    // ── Attached Property: IsInteractive ─────────────────────────────
    // Usage: <Border helpers:InteractionHelper.IsInteractive="True" ... />

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
        if (d is not Border border) return;

        if ((bool)e.NewValue)
        {
            border.PointerEntered += Border_PointerEntered;
            border.PointerExited += Border_PointerExited;
            border.PointerPressed += Border_PointerPressed;
            border.PointerReleased += Border_PointerReleased;
            border.PointerCanceled += Border_PointerCanceled;
            border.GotFocus += Border_GotFocus;
            border.LostFocus += Border_LostFocus;
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
        }
    }

    // ── Event Handlers ───────────────────────────────────────────────

    private static void Border_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border) return;
        var state = GetState(border);
        state.IsHovered = true;
        ApplyVisualState(border, state);
    }

    private static void Border_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border) return;
        var state = GetState(border);
        state.IsHovered = false;
        state.IsPressed = false;
        ApplyVisualState(border, state);
    }

    private static void Border_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border) return;
        var state = GetState(border);
        state.IsPressed = true;
        ApplyVisualState(border, state);
    }

    private static void Border_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border) return;
        var state = GetState(border);
        state.IsPressed = false;
        ApplyVisualState(border, state);
    }

    private static void Border_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border) return;
        var state = GetState(border);
        state.IsHovered = false;
        state.IsPressed = false;
        ApplyVisualState(border, state);
    }

    private static void Border_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border) return;
        var state = GetState(border);
        state.IsFocused = true;
        ApplyVisualState(border, state);
    }

    private static void Border_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border) return;
        var state = GetState(border);
        state.IsFocused = false;
        ApplyVisualState(border, state);
    }

    // ── State Machine ────────────────────────────────────────────────

    private static void ApplyVisualState(Border border, PointerState state)
    {
        if (state.IsPressed)
        {
            border.Translation = ElevationPressed;
            border.Opacity = OpacityPressed;
            border.BorderBrush = GetBrush("PrimaryBrush");
        }
        else if (state.IsHovered || state.IsFocused)
        {
            border.Translation = ElevationHover;
            border.Opacity = 1.0;
            border.BorderBrush = GetBrush("PrimaryBrush");
        }
        else
        {
            border.Translation = ElevationRest;
            border.Opacity = 1.0;
            border.BorderBrush = GetBrush("OutlineVariantBrush");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static PointerState GetState(Border border) =>
        _states.GetOrCreateValue(border);

    private static Microsoft.UI.Xaml.Media.Brush? GetBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value)
            ? value as Microsoft.UI.Xaml.Media.Brush
            : null;

    // ── Legacy API (backwards-compatible with HoverHelper) ───────────

    public static void ApplyHover(Border border)
    {
        var state = GetState(border);
        state.IsHovered = true;
        ApplyVisualState(border, state);
    }

    public static void RemoveHover(Border border)
    {
        var state = GetState(border);
        state.IsHovered = false;
        state.IsPressed = false;
        ApplyVisualState(border, state);
    }
}

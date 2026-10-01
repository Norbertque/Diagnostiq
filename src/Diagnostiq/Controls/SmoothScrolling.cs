using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Diagnostiq.Controls;

/// <summary>
/// Wheel and touchpad scrolling for every pixel-scrolling ScrollViewer in the app.
/// <para>
/// WPF scrolls three lines (48 px) per wheel event whatever its size. A precision touchpad sends
/// many small events (delta 5–20 instead of 120), so pages flew by in 48 px jumps. Here the distance
/// follows the delta (48 px per 120, the Windows "lines per notch" setting) and the offset glides to it.
/// </para>
/// A scroller that's already at its end lets the event bubble, so a nested scroll area hands over to the page.
/// Item-scrolling lists (CanContentScroll) keep WPF's own behaviour.
/// </summary>
public static class SmoothScrolling
{
    private const double PixelsPerLine = 16;      // WPF's line size for pixel scrolling
    private const double SettleTime = 0.06;       // seconds; how quickly the glide catches up

    private sealed class State
    {
        public double Target;
        public double LastSet;
        public TimeSpan LastFrame;
        public bool Animating;
    }

    private static readonly ConditionalWeakTable<ScrollViewer, State> States = new();

    /// <summary>Call once at startup.</summary>
    public static void Register() =>
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheel));

    private static void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer sv || sv.CanContentScroll || sv.ScrollableHeight <= 0) return;

        var state = States.GetValue(sv, _ => new State());
        if (!state.Animating) state.Target = sv.VerticalOffset;

        // Already at the end in this direction: let an outer scroller take it.
        if ((e.Delta > 0 && state.Target <= 0) || (e.Delta < 0 && state.Target >= sv.ScrollableHeight)) return;

        int lines = SystemParameters.WheelScrollLines;
        double perNotch = lines < 0 ? sv.ViewportHeight : lines * PixelsPerLine;   // -1 = "one screen at a time"
        state.Target = Math.Clamp(state.Target - e.Delta / 120.0 * perNotch, 0, sv.ScrollableHeight);
        e.Handled = true;

        if (!SystemParameters.ClientAreaAnimation)
        {
            sv.ScrollToVerticalOffset(state.Target);
            return;
        }
        if (state.Animating) return;
        state.Animating = true;
        state.LastSet = sv.VerticalOffset;
        state.LastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += Step;

        void Step(object? _, EventArgs args)
        {
            var now = ((RenderingEventArgs)args).RenderingTime;
            // Someone else moved it (scrollbar drag, keyboard, focus): stop gliding and let them.
            bool interrupted = Math.Abs(sv.VerticalOffset - state.LastSet) > 1 || !sv.IsLoaded;
            double remaining = state.Target - sv.VerticalOffset;
            if (interrupted || Math.Abs(remaining) < 0.5)
            {
                if (!interrupted) sv.ScrollToVerticalOffset(state.Target);
                CompositionTarget.Rendering -= Step;
                state.Animating = false;
                return;
            }
            double dt = state.LastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - state.LastFrame).TotalSeconds, 0, 0.1);
            if (dt == 0) return;   // Rendering can fire twice per frame
            state.LastFrame = now;
            double next = sv.VerticalOffset + remaining * (1 - Math.Exp(-dt / SettleTime));
            state.LastSet = next;
            sv.ScrollToVerticalOffset(next);
        }
    }
}

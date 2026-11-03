using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace PinList;

/// <summary>
/// The motion of the phone screens: an open list slides in from the right and out again, the tabs cross-fade, and a
/// sheet slides up with a soft spring (and closes when it is swiped down). A newer motion cancels an older one, so
/// quick taps never leave a screen half way.
/// </summary>
public partial class PhoneView
{
    CancellationTokenSource? _slide, _fade, _sheetMotion;

    /// <summary>Animates one number property of a control from one value to another.</summary>
    static Task Animate(Animatable target, AvaloniaProperty property, double from, double to, TimeSpan duration, Easing easing, CancellationToken ct)
    {
        var animation = new Animation
        {
            Duration = duration,
            Easing = easing,
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
            },
        };
        return animation.RunAsync(target, ct);
    }

    /// <summary>Waits for a motion; one that was cancelled by a newer motion is not an error.</summary>
    static async Task<bool> Finished(Task motion)
    {
        try { await motion; return true; }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>Motion needs a screen that is shown (not in tests that never show it) and has a size.</summary>
    bool CanMove => MotionEnabled && IsLoaded && Bounds.Width > 0;

    /// <summary>Turns the motion off (tests switch it off, so screens are where they should be at once).</summary>
    internal static bool MotionEnabled { get; set; } = true;

    /// <summary>Opening a list: it slides in from the right (about 250 ms).</summary>
    async void SlideInBoard()
    {
        _slide?.Cancel();
        _slide = new CancellationTokenSource();
        var ct = _slide.Token;
        BoardScreen.IsHitTestVisible = true;
        if (!CanMove) { BoardScreen.RenderTransform = null; return; }
        BoardScreen.RenderTransform = new TranslateTransform();
        bool done = await Finished(Animate(BoardScreen, TranslateTransform.XProperty, Bounds.Width, 0, TimeSpan.FromMilliseconds(250), new CubicEaseOut(), ct));
        if (done && !ct.IsCancellationRequested) BoardScreen.RenderTransform = null;
    }

    /// <summary>Back: the list slides out to the right (about 220 ms), then goes away.</summary>
    async void SlideOutBoard()
    {
        _slide?.Cancel();
        _slide = new CancellationTokenSource();
        var ct = _slide.Token;
        if (!CanMove) { BoardScope.IsVisible = false; return; }
        BoardScreen.IsHitTestVisible = false; // the cards below can already be used
        BoardScreen.RenderTransform = new TranslateTransform();
        bool done = await Finished(Animate(BoardScreen, TranslateTransform.XProperty, 0, Bounds.Width, TimeSpan.FromMilliseconds(220), new CubicEaseIn(), ct));
        if (!done || ct.IsCancellationRequested) return;
        BoardScope.IsVisible = false;
        BoardScreen.RenderTransform = null;
        BoardScreen.IsHitTestVisible = true;
    }

    /// <summary>Changing tabs: the new screen fades in (about 150 ms).</summary>
    async void FadeIn(Control screen)
    {
        _fade?.Cancel();
        _fade = new CancellationTokenSource();
        var ct = _fade.Token;
        if (!CanMove) return;
        bool done = await Finished(Animate(screen, OpacityProperty, 0, 1, TimeSpan.FromMilliseconds(150), new CubicEaseOut(), ct));
        if (done && !ct.IsCancellationRequested) screen.Opacity = 1;
    }

    // ================= Sheet =================

    (double Y, long Time)? _sheetDrag;

    void SetupSheet()
    {
        SheetHandle.PointerPressed += SheetDrag_Pressed;
        SheetHandle.PointerMoved += SheetDrag_Moved;
        SheetHandle.PointerReleased += SheetDrag_Released;
        SheetHandle.PointerCaptureLost += (_, _) => EndSheetDrag(close: false);
    }

    /// <summary>The sheet slides up with a soft overshoot while the background dims.</summary>
    async void SlideSheetIn()
    {
        _sheetMotion?.Cancel();
        _sheetMotion = new CancellationTokenSource();
        var ct = _sheetMotion.Token;
        _sheetDrag = null;
        Sheet.RenderTransform = null;
        Scrim.Opacity = 1;
        if (!CanMove) return;
        Sheet.RenderTransform = new TranslateTransform();
        double from = Math.Max(Bounds.Height * 0.6, 320);
        var scrim = Animate(Scrim, OpacityProperty, 0, 1, TimeSpan.FromMilliseconds(150), new CubicEaseOut(), ct);
        var sheet = Animate(Sheet, TranslateTransform.YProperty, from, 0, TimeSpan.FromMilliseconds(320), new SplineEasing(0.2, 1.15, 0.35, 1), ct);
        bool done = await Finished(sheet);
        await Finished(scrim);
        if (done && !ct.IsCancellationRequested)
        {
            Sheet.RenderTransform = null;
            Scrim.Opacity = 1;
        }
    }

    void SheetDrag_Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _sheetMotion?.Cancel();
        _sheetDrag = (e.GetPosition(this).Y, Environment.TickCount64);
        e.Pointer.Capture(SheetHandle);
    }

    void SheetDrag_Moved(object? sender, PointerEventArgs e)
    {
        if (_sheetDrag is not { } drag) return;
        double dy = Math.Max(0, e.GetPosition(this).Y - drag.Y);
        Sheet.RenderTransform = new TranslateTransform(0, dy);
        Scrim.Opacity = Math.Clamp(1 - dy / Math.Max(Bounds.Height, 1), 0.3, 1);
    }

    /// <summary>Dragged down more than 80 px, or flicked down fast: the sheet closes.</summary>
    void SheetDrag_Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_sheetDrag is not { } drag) return;
        double dy = e.GetPosition(this).Y - drag.Y;
        double speed = dy / Math.Max(1, Environment.TickCount64 - drag.Time); // px per ms
        EndSheetDrag(close: dy > 80 || (dy > 24 && speed > 1.0)); // first: letting go of the capture also ends a drag
        e.Pointer.Capture(null);
    }

    void EndSheetDrag(bool close)
    {
        if (_sheetDrag == null) return;
        _sheetDrag = null;
        Sheet.RenderTransform = null;
        Scrim.Opacity = 1;
        if (close) HideSheet();
    }
}

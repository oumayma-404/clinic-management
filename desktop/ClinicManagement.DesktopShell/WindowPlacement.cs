using System;
using System.Windows;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Where the shell's window opens: the size it asks for, reconciled with the work area it has to fit inside.
///
/// <para>Pure, and separate from <see cref="MainWindow"/>, for the reason <c>desktop/CLAUDE.md</c> gives about
/// <c>MirrorPathPlanner</c> — the shell itself cannot be run under CI (no WebView2 runtime), so the only part of it
/// that can be <b>proven</b> is the part that touches no window. This is worth being that part: the failure it
/// prevents is a window a clinic cannot close, move or resize, and the arithmetic is the entire fix.</para>
/// </summary>
public static class WindowPlacement
{
    /// <summary>Every value the window takes, so what is asserted here is exactly what gets assigned.</summary>
    public readonly record struct Placement(
        double Left,
        double Top,
        double Width,
        double Height,
        double MinWidth,
        double MinHeight);

    /// <summary>
    /// Clamp the size to <paramref name="workArea"/>, then centre within it — in that order.
    ///
    /// <para>⚠️ The order is the fix. WPF's <c>WindowStartupLocation="CenterScreen"</c> centres the requested size
    /// <b>whether or not it fits</b>: 1280×820 on a 1366×768 laptop at 150% scaling (a ~910×470 DIP work area)
    /// gives <c>Top = -175</c>, so the title bar — and with it fermer, agrandir and réduire — lands above the top
    /// of the screen. Centring a size that has already been clamped can never produce a negative offset, which is
    /// what guarantees the caption is reachable.</para>
    ///
    /// <para>The minimums are clamped too, and returned rather than left to the caller: a <c>MinHeight</c> larger
    /// than the work area re-imposes the very size the clamp above just removed, and WPF applies it silently after
    /// the assignment to <c>Height</c>.</para>
    /// </summary>
    /// <returns>
    /// <c>null</c> when no usable work area was reported (a session switch, no attached display) — the caller keeps
    /// its declared size, which is a better guess than a zero-sized window.
    /// </returns>
    public static Placement? Fit(
        double desiredWidth,
        double desiredHeight,
        double minWidth,
        double minHeight,
        Rect workArea)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return null;
        }

        var width = Math.Min(desiredWidth, workArea.Width);
        var height = Math.Min(desiredHeight, workArea.Height);

        return new Placement(
            Left: workArea.Left + Math.Max(0, (workArea.Width - width) / 2),
            Top: workArea.Top + Math.Max(0, (workArea.Height - height) / 2),
            Width: width,
            Height: height,
            // `workArea`, not `width`: a minimum equal to the clamped width is right, but one derived from a
            // *smaller* clamped width would forbid the user from ever widening the window again.
            MinWidth: Math.Min(minWidth, workArea.Width),
            MinHeight: Math.Min(minHeight, workArea.Height));
    }
}

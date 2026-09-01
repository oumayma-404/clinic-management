using System.Windows;
using ClinicManagement.DesktopShell;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>
/// The window opens somewhere a human can reach its title bar.
///
/// <para>These exist because the shell cannot be run under CI and the defect they cover was reported from a
/// clinic, not found here: the client opened larger than the screen and the close, agrandir and réduire buttons
/// were above the top edge, so the window could not be closed, moved or resized by mouse at all.</para>
/// </summary>
public class WindowPlacementTests
{
    // What the XAML asks for.
    private const double DesiredWidth = 1280;
    private const double DesiredHeight = 820;
    private const double MinWidth = 640;
    private const double MinHeight = 480;

    private static WindowPlacement.Placement Fit(Rect workArea)
    {
        var placement = WindowPlacement.Fit(DesiredWidth, DesiredHeight, MinWidth, MinHeight, workArea);
        Assert.NotNull(placement);
        return placement!.Value;
    }

    /// <summary>The whole invariant, in one assertion — the window is inside the work area on all four sides.</summary>
    private static void AssertFullyInside(WindowPlacement.Placement p, Rect workArea)
    {
        Assert.True(p.Left >= workArea.Left, $"Left {p.Left} < work area {workArea.Left}");
        Assert.True(p.Top >= workArea.Top, $"Top {p.Top} < work area {workArea.Top}");
        Assert.True(p.Left + p.Width <= workArea.Right + 0.001, $"right edge {p.Left + p.Width} > {workArea.Right}");
        Assert.True(p.Top + p.Height <= workArea.Bottom + 0.001, $"bottom edge {p.Top + p.Height} > {workArea.Bottom}");
    }

    // The reported machine: a 1366×768 laptop at 150% scaling is a 910.67×512 DIP screen, and the taskbar takes
    // ~40 DIP of it. `CenterScreen` on the un-clamped 820 gave Top = (472 - 820) / 2 = -174, which is the number
    // in the bug report.
    [Fact]
    public void On_The_Laptop_That_Reported_It_The_Title_Bar_Is_On_Screen()
    {
        var workArea = new Rect(0, 0, 910.67, 472);

        var placement = Fit(workArea);

        Assert.Equal(0, placement.Top);
        Assert.Equal(0, placement.Left);
        AssertFullyInside(placement, workArea);
    }

    [Theory]
    // The reported laptop, and the two other small-screen shapes a clinic PC actually comes in.
    [InlineData(910.67, 472)]     // 1366×768 @ 150%
    [InlineData(1280, 680)]       // 1280×720, taskbar
    [InlineData(1024, 728)]       // an old 1024×768 desk machine
    // Exactly the asked-for size, the boundary between clamping and not.
    [InlineData(1280, 820)]
    // Room to spare — the ordinary case, which must not regress.
    [InlineData(1920, 1040)]
    [InlineData(2560, 1400)]
    public void The_Window_Always_Opens_Fully_Inside_The_Work_Area(double width, double height)
    {
        var workArea = new Rect(0, 0, width, height);

        AssertFullyInside(Fit(workArea), workArea);
    }

    [Fact]
    public void A_Screen_With_Room_Keeps_The_Asked_For_Size_And_Is_Centred()
    {
        var workArea = new Rect(0, 0, 1920, 1040);

        var placement = Fit(workArea);

        Assert.Equal(DesiredWidth, placement.Width);
        Assert.Equal(DesiredHeight, placement.Height);
        Assert.Equal((1920 - 1280) / 2, placement.Left);
        Assert.Equal((1040 - 820) / 2, placement.Top);
    }

    // A taskbar docked left or top moves the work area's origin. Centring within `Width`/`Height` alone would put
    // the window under it — the offsets are added for this case and nothing else exercises them.
    [Fact]
    public void A_Work_Area_That_Does_Not_Start_At_Zero_Is_Honoured()
    {
        var workArea = new Rect(80, 40, 1840, 1000);

        var placement = Fit(workArea);

        Assert.Equal(80 + (1840 - 1280) / 2, placement.Left);
        Assert.Equal(40 + (1000 - 820) / 2, placement.Top);
        AssertFullyInside(placement, workArea);
    }

    // ⚠️ The half that is easy to leave out: WPF applies MinWidth/MinHeight *after* the assignment to
    // Width/Height, so a minimum bigger than the work area silently re-imposes the oversized window the clamp
    // above just removed — and the bug comes back with the fix still in the file.
    [Fact]
    public void A_Minimum_Larger_Than_The_Work_Area_Is_Clamped_Too()
    {
        var workArea = new Rect(0, 0, 600, 400);

        var placement = Fit(workArea);

        Assert.Equal(600, placement.MinWidth);
        Assert.Equal(400, placement.MinHeight);
        Assert.True(placement.MinWidth <= placement.Width);
        Assert.True(placement.MinHeight <= placement.Height);
        AssertFullyInside(placement, workArea);
    }

    // A minimum that already fits is left alone, so the user can still shrink the window to it.
    [Fact]
    public void A_Minimum_That_Fits_Is_Left_Alone()
    {
        var placement = Fit(new Rect(0, 0, 1920, 1040));

        Assert.Equal(MinWidth, placement.MinWidth);
        Assert.Equal(MinHeight, placement.MinHeight);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1280, 0)]
    [InlineData(0, 820)]
    public void No_Usable_Work_Area_Leaves_The_Declared_Size_Alone(double width, double height)
    {
        Assert.Null(WindowPlacement.Fit(DesiredWidth, DesiredHeight, MinWidth, MinHeight, new Rect(0, 0, width, height)));
    }

    // `Rect.Empty` is what an unset rectangle actually is in WPF, and its Width/Height are negative infinity —
    // not zero, so the guard has to be `<= 0` rather than `== 0`.
    [Fact]
    public void An_Empty_Rect_Leaves_The_Declared_Size_Alone()
    {
        Assert.Null(WindowPlacement.Fit(DesiredWidth, DesiredHeight, MinWidth, MinHeight, Rect.Empty));
    }
}

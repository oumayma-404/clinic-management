using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Paints the window chrome and the shell's own French screens in the same theme the web app resolved to.
///
/// <para>
/// WHY THIS EXISTS. The shell used to be a light OS title bar and a light WPF menu strip stacked above a web app
/// the user had put in dark mode — three bands, two of which belonged to a different product. A thin client whose
/// whole job is to disappear cannot announce itself twice before the app starts.
/// </para>
///
/// <para>
/// ⚠️ **The web app is the authority, not Windows.** The theme is a per-user setting inside the app
/// (next-themes, <c>attribute="class"</c> → a <c>dark</c> class on <c>&lt;html&gt;</c>), so reading the OS
/// preference would be right until the moment a user overrides it — which is the moment they are looking at the
/// seam. Windows' own setting is used only as the value *before* a page has loaded, where there is no web app to
/// ask; see <see cref="ResolveOsPreference"/>.
/// </para>
///
/// <para>
/// The colours are the app's own tokens from <c>web/app/globals.css</c>, converted from oklch once. A drift here
/// is a visible seam rather than a rounding error — the same warning <c>web/app/manifest.ts</c> carries about its
/// <c>theme_color</c>, and for the same reason: the caption sits directly above the app's own ground.
/// </para>
/// </summary>
internal static class WindowTheme
{
    /// <summary>One palette. Field names are the CSS custom properties they were converted from.</summary>
    private sealed record Palette(
        string Background,
        string Card,
        string Foreground,
        string MutedForeground,
        string Border,
        string Primary,
        string PrimaryForeground,
        string Destructive,
        string NoticeBackground,
        string NoticeBorder,
        string NoticeForeground);

    private static readonly Palette Light = new(
        Background: "#F0F9FE",        // --background        oklch(0.977 0.011 230)
        Card: "#FFFFFF",              // --card              oklch(1 0 0)
        Foreground: "#0E191F",        // --foreground        oklch(0.205 0.020 236)
        MutedForeground: "#58666E",   // --muted-foreground  oklch(0.50 0.021 234)
        Border: "#D9E3E8",            // --border            oklch(0.91 0.013 230)
        Primary: "#02678F",           // --primary           oklch(0.485 0.101 234)
        PrimaryForeground: "#F9FCFF", // --primary-foreground
        Destructive: "#C92F33",       // --destructive       oklch(0.55 0.19 25)
        NoticeBackground: "#FFF4CE",
        NoticeBorder: "#E1C65B",
        NoticeForeground: "#5C4813");

    private static readonly Palette Dark = new(
        Background: "#050A0E",        // .dark --background        oklch(0.14 0.014 238)
        Card: "#0D161C",              // .dark --card              oklch(0.193 0.018 238)
        Foreground: "#EEF4F8",        // .dark --foreground        oklch(0.964 0.008 232)
        MutedForeground: "#87949C",   // .dark --muted-foreground  oklch(0.658 0.019 234)
        Border: "#1E292F",            // .dark --border            oklch(0.273 0.019 236)
        Primary: "#017EAE",           // .dark --primary           oklch(0.56 0.117 234)
        PrimaryForeground: "#F9FCFF",
        Destructive: "#C92F33",
        // The update strip's own trio, lifted onto a dark ground rather than inverted: a #FFF4CE band over
        // near-black is a flashbang, and the app's own `--warning-wash` is what a warning looks like here.
        NoticeBackground: "#3A2E10",
        NoticeBorder: "#6B5620",
        NoticeForeground: "#F0D89A");

    /// <summary>The theme currently applied, so a repeated report from the page costs nothing.</summary>
    private static bool? _applied;

    /// <summary>
    /// Paint everything for <paramref name="dark"/>. Safe to call before the window has a handle — the DWM half
    /// is skipped and re-applied by <see cref="Reapply"/> once it does.
    /// </summary>
    internal static void Apply(Window window, bool dark)
    {
        if (_applied == dark)
        {
            return;
        }

        _applied = dark;
        var palette = dark ? Dark : Light;

        var resources = Application.Current.Resources;
        resources["ShellBackground"] = Brush(palette.Background);
        resources["ShellCard"] = Brush(palette.Card);
        resources["ShellInk"] = Brush(palette.Foreground);
        resources["ShellInkMuted"] = Brush(palette.MutedForeground);
        resources["ShellBorder"] = Brush(palette.Border);
        resources["ShellAccent"] = Brush(palette.Primary);
        resources["ShellAccentInk"] = Brush(palette.PrimaryForeground);
        resources["ShellDanger"] = Brush(palette.Destructive);
        resources["ShellNoticeBackground"] = Brush(palette.NoticeBackground);
        resources["ShellNoticeBorder"] = Brush(palette.NoticeBorder);
        resources["ShellNoticeInk"] = Brush(palette.NoticeForeground);

        ApplyChrome(window, palette, dark);
    }

    /// <summary>Re-runs the DWM half for a window that has only just been given a handle.</summary>
    internal static void Reapply(Window window)
    {
        if (_applied is bool dark)
        {
            ApplyChrome(window, dark ? Dark : Light, dark);
        }
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    // ---- DWM ------------------------------------------------------------------------------------

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Tint the non-client area — caption, its text, and the window border.
    ///
    /// <para>
    /// ⚠️ **Every call here is allowed to fail and none is checked.** Caption/text/border colouring landed in
    /// Windows 11 (build 22000) and the immersive-dark attribute was renumbered between Windows 10 1903 and 20H1;
    /// on anything older the call returns a failing HRESULT and the title bar simply stays as Windows drew it.
    /// That is the correct outcome — the shell is a clinic's working tool, and a cosmetic attribute must never be
    /// the reason it does not start. `PreserveSig` is what keeps that a return value instead of an exception.
    /// </para>
    /// </summary>
    private static void ApplyChrome(Window window, Palette palette, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return; // No handle yet — Reapply() runs once SourceInitialized fires.
        }

        var useDark = dark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref useDark, sizeof(int));
        }

        var caption = ColorRef(palette.Background);
        var text = ColorRef(palette.Foreground);
        var border = ColorRef(palette.Border);
        DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(int));
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(int));
    }

    /// <summary>
    /// A Win32 <c>COLORREF</c> is <c>0x00BBGGRR</c> — the reverse of the <c>#RRGGBB</c> the palette is written in.
    /// Swapping the two channels silently is why a caption comes out blue when the brand is teal.
    /// </summary>
    private static int ColorRef(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        return color.R | (color.G << 8) | (color.B << 16);
    }

    // ---- The pre-page default -------------------------------------------------------------------

    /// <summary>
    /// Windows' own app-theme preference, used only until the web app reports its own.
    ///
    /// <para>
    /// Anything unreadable means light: a missing key, a policy-locked hive or a future Windows that moves it must
    /// leave the shell looking like it did before this file existed, never dark-on-guesswork.
    /// </para>
    /// </summary>
    internal static bool ResolveOsPreference()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }
}

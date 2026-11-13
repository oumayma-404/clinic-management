using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.DesktopShell;
using ClinicManagement.DesktopShell.Camera;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>
/// The two places the camera import's shell half is mirrored on the web side, read from the files themselves so a
/// change on one side fails here rather than silently on a clinic PC.
/// </summary>
public class CameraContractTests
{
    /// <summary>
    /// The page classifies the browser path's files with four sets; the shell classifies a card's with its own. A
    /// format one side calls JPEG and the other does not is copied and never read, or never copied — with no error.
    /// </summary>
    [Theory]
    [InlineData("JPEG")]
    [InlineData("RAW")]
    [InlineData("HEIC")]
    [InlineData("VIDEO")]
    public void The_Shell_Sorts_Files_Exactly_As_The_Page_Does(string set)
    {
        var source = File.ReadAllText(RepoFile("web", "lib", "photo-import", "sources.ts"));
        var match = Regex.Match(source, $@"const {set} = new Set\(\[(?<items>[^\]]*)\]\)");
        Assert.True(match.Success, $"sources.ts no longer declares {set} as a Set — this guard measures nothing");

        var web = Regex.Matches(match.Groups["items"].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        var shell = set switch
        {
            "JPEG" => CameraFileKinds.Jpeg,
            "RAW" => CameraFileKinds.Raw,
            "HEIC" => CameraFileKinds.Heic,
            _ => CameraFileKinds.Video,
        };

        Assert.NotEmpty(web);
        Assert.True(web.SetEquals(shell), $"{set}: web [{string.Join(", ", web.Order())}] ≠ shell [{string.Join(", ", shell.Order())}]");
    }

    /// <summary>
    /// bridge.md's rule — the method set and the version move together — made checkable: every method the Windows app
    /// exposes is declared in the page's typing of the bridge and named in the contract.
    /// </summary>
    [Fact]
    public void Every_Method_The_Windows_App_Exposes_Is_Declared_For_The_Page_And_In_The_Contract()
    {
        var script = VaultBridge.Script("1.8.0");
        var frozen = Regex.Match(script, @"value: Object\.freeze\(\{(?<body>.*?)\}\)", RegexOptions.Singleline);
        Assert.True(frozen.Success, "the bridge object is no longer built with Object.freeze({ … })");

        var members = Regex.Matches(frozen.Groups["body"].Value, @"^\s*(\w+):", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Except(new[] { "version", "platform" })
            .ToList();
        Assert.Contains("cameraScan", members);

        var typing = File.ReadAllText(RepoFile("web", "types", "clinic-shell.d.ts"));
        var contract = File.ReadAllText(RepoFile("mobile", "shared", "bridge.md"));
        foreach (var member in members)
        {
            Assert.Matches(new Regex($@"\b{member}\??\("), typing);
            Assert.Contains(member, contract);
        }
    }

    [Fact]
    public void Camera_Messages_Wait_In_A_Queue_Rather_Than_The_Coffres_Single_Slot()
    {
        var script = VaultBridge.Script("1.8.0");

        Assert.Contains("__clinicShellPendingCamera", script);
        Assert.Contains("queue.push", script);
        Assert.Contains("'1.8.0'", script);
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());

    // The suite may be built far from the repo (the SAC workaround); the source file is where the repo is.
    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}

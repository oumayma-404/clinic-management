using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.DesktopShell;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>
/// The PC de secours offer's shell half (<c>clinic-pc-copy</c> AC-1.4–1.8, AC-1.11): what is handed to the elevated
/// installer, how its exit code reads back, and which page messages are trusted. The run itself needs UAC and is the
/// Windows rehearsal's job; everything decided before and after it is here.
/// </summary>
public class RelayInstallerTests
{
    // ── The installer's command line ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_Value_Is_One_Quoted_Argument_So_A_Path_With_Spaces_Survives()
    {
        var arguments = RelayInstaller.Arguments(
            @"C:\Users\Dr Ben Salah\AppData\Local\ClinicManagement\relay\pair-1.txt",
            "https://app.apexa.tn:443",
            "PC-ACCUEIL",
            10_737_418_240,
            @"C:\Users\Dr Ben Salah\AppData\Local\ClinicManagement\relay\result-1.txt");

        Assert.StartsWith("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RELAY ", arguments);
        Assert.Contains(@"""/PAIRFILE=C:\Users\Dr Ben Salah\AppData\Local\ClinicManagement\relay\pair-1.txt""", arguments);
        Assert.Contains(@"""/CLOUD=https://app.apexa.tn:443""", arguments);
        Assert.Contains(@"""/LABEL=PC-ACCUEIL""", arguments);
        Assert.Contains("/NEEDBYTES=10737418240", arguments);
        Assert.Contains(@"""/RESULTFILE=C:\Users\Dr Ben Salah\AppData\Local\ClinicManagement\relay\result-1.txt""", arguments);
    }

    // The code is a secret: it goes in a file, never on a command line any process can read.
    [Fact]
    public void The_Code_Itself_Is_Never_On_The_Command_Line()
    {
        const string code = "Zk3q9-Yp_2vT8wLmN0aBcDeFgHiJkLmNoPqRsTuVwXy";

        var arguments = RelayInstaller.Arguments(@"C:\t\pair.txt", "https://c", "PC", 1, @"C:\t\r.txt");

        Assert.DoesNotContain(code, arguments);
        Assert.DoesNotContain("/CODE", arguments);
    }

    [Fact]
    public void A_Quote_In_The_Machine_Name_Cannot_Close_The_Argument()
    {
        var arguments = RelayInstaller.Arguments(@"C:\t\p.txt", "https://c", "PC\" /PAIRFILE=evil", 1, @"C:\t\r.txt");

        Assert.Contains(@"""/LABEL=PC /PAIRFILE=evil""", arguments);
        Assert.Single(Regex.Matches(arguments, @"""/PAIRFILE="));
    }

    [Fact]
    public void A_Negative_Room_Is_Sent_As_None() =>
        Assert.Contains("/NEEDBYTES=0", RelayInstaller.Arguments(@"C:\p", "https://c", "PC", -5, @"C:\r"));

    // ⚠️ The derived guard: every /NAME= the shell passes is one the installer reads, and /RELAY is its switch. A
    // renamed parameter on either side is otherwise an install that silently ignores the code.
    [Fact]
    public void Every_Parameter_The_Shell_Passes_Is_One_The_Installer_Reads()
    {
        var iss = File.ReadAllText(RepoFile("packaging", "setup", "clinic-setup.iss"));
        var passed = Regex.Matches(RelayInstaller.Arguments("p", "c", "l", 1, "r"), @"/([A-Z]+)=")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.Equal(new[] { "PAIRFILE", "CLOUD", "LABEL", "NEEDBYTES", "RESULTFILE" }, passed);
        foreach (var name in passed)
        {
            Assert.Contains("{param:" + name + "|", iss);
        }

        Assert.Contains("'/RELAY'", iss);
    }

    // ── How the installer ended (AC-1.11) ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, RelayInstaller.Installed)]
    [InlineData(7, RelayInstaller.Refused)]
    [InlineData(20, RelayInstaller.Refused)]
    [InlineData(21, RelayInstaller.Failed)]
    [InlineData(1, RelayInstaller.Failed)]
    [InlineData(5, RelayInstaller.Failed)]
    public void An_Exit_Code_Reads_As_One_Outcome(int exitCode, string kind) =>
        Assert.Equal(kind, RelayInstaller.OutcomeFor(exitCode, null).Kind);

    [Fact]
    public void The_Installers_Own_Sentence_Wins()
    {
        var outcome = RelayInstaller.OutcomeFor(7, "  Il faut 12 Go libres sur ce PC (8 Go disponibles).  ");

        Assert.Equal("Il faut 12 Go libres sur ce PC (8 Go disponibles).", outcome.Sentence);
    }

    [Fact]
    public void An_Outcome_Is_Never_Wordless()
    {
        foreach (var code in new[] { 0, 7, 20, 21, 99 })
        {
            Assert.False(string.IsNullOrWhiteSpace(RelayInstaller.OutcomeFor(code, "  ").Sentence));
        }
    }

    // The exit codes the installer documents are the ones read here (`GetCustomSetupExitCode`).
    [Fact]
    public void The_Installer_Still_Ends_With_The_Codes_The_Shell_Reads()
    {
        var iss = File.ReadAllText(RepoFile("packaging", "setup", "clinic-setup.iss"));

        Assert.Contains("SetupOutcome := 20", iss);
        Assert.Contains("SetupOutcome := 21", iss);
        Assert.Contains("SetupOutcome := 0", iss);
    }

    // ── What the page may ask ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("r1", true)]
    [InlineData("r42", true)]
    [InlineData("r", false)]
    [InlineData("i1", false)]
    [InlineData("r1');alert(1);//", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_The_Bridges_Own_Ids_Reach_A_Script(string? id, bool accepted) =>
        Assert.Equal(accepted, RelayInstaller.IsRequestId(id));

    [Theory]
    [InlineData("Zk3q9-Yp_2vT8wLmN0aBcDeFgHiJkLmNoPqRsTuVwXy", true)]
    [InlineData("short", false)]
    [InlineData("has space in it and is long enough", false)]
    [InlineData("\"/RELAY /PAIRFILE=x\"aaaaaaaaaaaaaa", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_A_Code_Shaped_Like_The_Clouds_Is_Worth_An_Elevated_Run(string? code, bool plausible) =>
        Assert.Equal(plausible, RelayInstaller.IsPlausibleCode(code));

    // ── This machine's facts (AC-1.6, AC-1.7) ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData((byte)1, true)]   // high
    [InlineData((byte)8, true)]   // charging
    [InlineData((byte)0, true)]   // between thresholds
    [InlineData((byte)128, false)] // no system battery — a desktop, on a UPS or not
    [InlineData((byte)255, false)] // unknown — never claimed to be a laptop
    public void A_Laptop_Is_A_Pc_With_A_System_Battery(byte flag, bool laptop) =>
        Assert.Equal(laptop, RelayHost.IsBatteryFlag(flag));

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(6, true)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    [InlineData(5, false)] // suspended: the key sits in the clear
    [InlineData(8, false)]
    [InlineData(0, null)]  // not an answer — Home editions, network drives
    [InlineData(null, null)]
    public void BitLocker_Is_Read_In_Three_States(int? value, bool? encrypted) =>
        Assert.Equal(encrypted, DriveEncryption.FromBitLockerProtection(value));

    // The installer comes in parallel parts: together they must cover the file exactly once, in order.
    [Theory]
    [InlineData(360_976_382L, 6)]
    [InlineData(17_000_000L, 6)]
    [InlineData(5L, 6)]
    [InlineData(1L, 6)]
    public void The_Parts_Cover_The_File_Exactly_Once(long length, int parts)
    {
        var ranges = RelayInstaller.Ranges(length, parts);

        Assert.Equal(0, ranges[0].From);
        Assert.Equal(length - 1, ranges[^1].To);
        for (var i = 1; i < ranges.Length; i++)
        {
            Assert.Equal(ranges[i - 1].To + 1, ranges[i].From);
        }

        Assert.Equal(length, ranges.Sum(r => r.To - r.From + 1));
        Assert.True(ranges.Length <= parts);
    }

    [Fact]
    public void An_Empty_File_Has_No_Parts() => Assert.Empty(RelayInstaller.Ranges(0, 6));

    private static string RepoFile(params string[] parts) =>
        Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());

    // The suite may be built far from the repo (the SAC workaround); the source file is where the repo is.
    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}

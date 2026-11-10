using System.Reflection;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.UnitTests.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The demo's classification (<c>demo-cabinet</c> FR-2), derived from the compiled controllers. Every write is refused
/// unless reviewed below; every read passes unless reviewed below. Both sets are asserted in both directions, and a
/// source scan catches the case a list cannot: a NEW export, archive or dossier read that nobody marked.
/// </summary>
public class DemoExemptionCoverageTests
{
    /// <summary>The writes a visitor may make. The attribute carries the same reason at the action.</summary>
    private static readonly HashSet<string> ExpectedAllowedWrites = new()
    {
        "Auth.DemoSession",                          // entering
        "Auth.Refresh",                              // staying
        "Auth.Logout",                               // leaving
        "CspReport.Receive",                         // the browser's own security reports
        "MedicalDocuments.PreviewFicheOrdonnance",   // the ordonnance aperçu, persists nothing
        "MedicalDocuments.GeneratePdfForDownload",   // printing, persists nothing
    };

    /// <summary>The reads a visitor may not make.</summary>
    private static readonly HashSet<string> ExpectedRefusedReads = new()
    {
        "Appointments.ExportAppointments",
        "Backup.DownloadArchive",
        "Backup.GetFileManifest",                    // the archive copy's listing; not a hand-out, but backups are off
        "Billing.ExportReceivables",
        "Billing.ExportCaisseLedger",
        "Billing.ExportChequesDue",
        "Expenses.ExportExpenses",
        "GoogleCalendar.Callback",                   // a GET that writes tokens
        "Invoices.ExportInvoices",
        "LabOrders.ExportLabWorkOrders",
        "Patients.ExportPatients",
        "Patients.ExportDossier",
        "Stock.ExportStock",
        "Team.ExportAttendance",
        "TreatmentPlans.ExportPlans",
    };

    /// <summary>What a read calls when it hands data out of the product. Each is shared by every action of its kind.</summary>
    private static readonly string[] HandOutMarkers =
    {
        "Csv(",                         // ApiControllerBase.Csv — every « Exporter »
        "PatientDossierPackager",       // one patient's dossier
        "ClinicArchiveFormat",          // the whole cabinet
    };

    private static IEnumerable<Type> ProductionControllers() =>
        typeof(ClinicManagement.API.Controllers.AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null);

    private static string Name(Type controller, MethodInfo action) =>
        $"{controller.Name.Replace("Controller", string.Empty)}.{action.Name}";

    private static bool IsWrite(MethodInfo action)
    {
        var declared = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).SelectMany(a => a.HttpMethods).ToList();

        return declared.Count == 0
               || declared.Any(m => m is not ("GET" or "HEAD" or "OPTIONS"));
    }

    private static List<string> AllowedWrites(IEnumerable<Type> controllers) =>
        controllers.SelectMany(c => Actions(c)
                .Where(a => IsWrite(a) && a.GetCustomAttribute<AllowedInDemoAttribute>(inherit: true) is not null)
                .Select(a => Name(c, a)))
            .ToList();

    private static List<string> RefusedReads(IEnumerable<Type> controllers) =>
        controllers.SelectMany(c => Actions(c)
                .Where(a => !IsWrite(a) && a.GetCustomAttribute<RefusedInDemoAttribute>(inherit: true) is not null)
                .Select(a => Name(c, a)))
            .ToList();

    [Fact]
    public void No_Unreviewed_Write_Is_Allowed_In_The_Demo()
    {
        var unexpected = AllowedWrites(ProductionControllers()).Except(ExpectedAllowedWrites).OrderBy(x => x).ToList();

        Assert.True(unexpected.Count == 0,
            "Write(s) allowed in the demo but not reviewed: " + string.Join(", ", unexpected));
    }

    [Fact]
    public void Every_Reviewed_Write_Is_Still_Allowed()
    {
        var missing = ExpectedAllowedWrites.Except(AllowedWrites(ProductionControllers())).OrderBy(x => x).ToList();

        Assert.True(missing.Count == 0,
            "Reviewed demo write(s) no longer allowed (attribute removed, action renamed, verb changed): "
            + string.Join(", ", missing));
    }

    [Fact]
    public void The_Refused_Reads_Are_Exactly_The_Reviewed_Ones()
    {
        var refused = RefusedReads(ProductionControllers()).ToHashSet();

        Assert.True(refused.SetEquals(ExpectedRefusedReads),
            "Unreviewed: " + string.Join(", ", refused.Except(ExpectedRefusedReads))
            + " | No longer refused: " + string.Join(", ", ExpectedRefusedReads.Except(refused)));
    }

    /// <summary>[FR-2] « a new action shall not ship unclassified »: a read that hands data out must say so.</summary>
    [Fact]
    public void Every_Read_That_Hands_Data_Out_Is_Refused()
    {
        var handOuts = HandOutReads();
        var refused = RefusedReads(ProductionControllers()).ToHashSet();

        Assert.True(handOuts.Count >= 12, $"The source scan found only {handOuts.Count} hand-out reads; it is scanning nothing.");

        var unmarked = handOuts.Where(h => !refused.Contains(h)).OrderBy(x => x).ToList();
        Assert.True(unmarked.Count == 0,
            "Read(s) handing data out of the product without [RefusedInDemo]: " + string.Join(", ", unmarked));
    }

    [Fact]
    public void Every_Demo_Classification_Gives_A_Reason()
    {
        var thin = ProductionControllers()
            .SelectMany(c => Actions(c).Select(a => (Name: Name(c, a),
                Reason: a.GetCustomAttribute<AllowedInDemoAttribute>(true)?.Reason
                        ?? a.GetCustomAttribute<RefusedInDemoAttribute>(true)?.Reason)))
            .Where(x => x.Reason is not null && x.Reason.Trim().Length < 20)
            .Select(x => x.Name)
            .ToList();

        Assert.True(thin.Count == 0, "Demo classification reason(s) too thin to review: " + string.Join(", ", thin));
    }

    [Fact]
    public void The_Guard_Detects_A_Newly_Allowed_Write_And_A_Newly_Refused_Read()
    {
        Assert.Equal(new[] { "DemoProbe.Write" }, AllowedWrites(new[] { typeof(DemoProbeController) }));
        Assert.Equal(new[] { "DemoProbe.Read" }, RefusedReads(new[] { typeof(DemoProbeController) }));
    }

    [Fact]
    public void The_Source_Scan_Detects_An_Unmarked_Export()
    {
        var body = "public IActionResult Export() { return Csv(table, \"x\"); }";

        Assert.True(HandsDataOut(body));
        Assert.False(HandsDataOut("public IActionResult List() { return Ok(rows); }"));

        // A signature that wraps is still found, and its body still read.
        var wrapped = "    public async Task<IActionResult>\n        ExportThings(\n        int page)\n    {\n        return Csv(t, \"x\");\n    }\n\n    public IActionResult Next() => Ok();\n";
        var found = MethodBody(wrapped, "ExportThings");
        Assert.NotNull(found);
        Assert.True(HandsDataOut(found!));
        Assert.DoesNotContain("Next()", found);
    }

    private static bool HandsDataOut(string body) => HandOutMarkers.Any(m => body.Contains(m, StringComparison.Ordinal));

    /// <summary>
    /// Every GET whose own body calls a hand-out marker, read from the controller sources. A read whose source cannot
    /// be found fails the scan rather than being skipped — a scan that skips is a guard that passes on the new case.
    /// </summary>
    private static List<string> HandOutReads()
    {
        var folder = Path.Combine(SolutionSources.Root().FullName, "ClinicManagement.API", "Controllers");
        var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();

        var found = new List<string>();
        var unresolved = new List<string>();
        foreach (var controller in ProductionControllers())
        {
            // Every file declaring the class, whatever the file is called (a partial spans several).
            var declaration = new Regex($@"\bclass\s+{Regex.Escape(controller.Name)}\b");
            var source = string.Join("\n", files.Where(f => declaration.IsMatch(f)));

            foreach (var action in Actions(controller).Where(a => !IsWrite(a)))
            {
                var body = MethodBody(source, action.Name);
                if (body is null)
                {
                    unresolved.Add(Name(controller, action));
                }
                else if (HandsDataOut(body))
                {
                    found.Add(Name(controller, action));
                }
            }
        }

        Assert.True(unresolved.Count == 0, "Source scan could not find these reads: " + string.Join(", ", unresolved));
        return found;
    }

    /// <summary>From the method's signature (which may wrap) to the next member's attributes or doc comment.</summary>
    private static string? MethodBody(string source, string method)
    {
        var start = Regex.Match(source, $@"\bpublic\b[^;{{}}]*?\b{Regex.Escape(method)}\s*\(");
        if (!start.Success)
        {
            return null;
        }

        var rest = source[start.Index..];
        var end = Regex.Match(rest[1..], @"\n    (\[|///|public |private |protected |internal )");
        return end.Success ? rest[..(end.Index + 1)] : rest;
    }

    private class DemoProbeController : ControllerBase
    {
        [HttpPost]
        [AllowedInDemo("A probe reason long enough to be a sentence.")]
        public IActionResult Write() => Ok();

        [HttpGet]
        [RefusedInDemo("A probe reason long enough to be a sentence.")]
        public IActionResult Read() => Ok();
    }
}

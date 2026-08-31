using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>No log statement names a patient</b> (<c>hosted-security-hardening</c> FR-4.4).
///
/// <para><b>Why this is a guard and not a code review.</b> Eleven statements wrote a patient's name into the log
/// file, eight of them at <c>Information</c> or above and therefore into the rolling file on disk — and Part D
/// makes that file <b>durable</b>, so what used to vanish on a container restart is now kept for thirty days. The
/// scrub and the volume land in one change for exactly that reason, and a scrub with nothing holding it is one
/// refactor away from coming back.</para>
///
/// <para><b>How it derives its candidate set.</b> It scans every source file in the solution for a logging call,
/// extracts its <b>message template</b>, and reads the <c>{Placeholder}</c> names out of it. Nothing is listed:
/// a service written next month is covered on the day it is written, which is the property a
/// <c>[InlineData]</c> table of today's log sites cannot have — it can only fail on rows somebody remembered to
/// add.</para>
///
/// <para>⚠️ <b>A statement that masks its value passes.</b> The rule is not « never write the word patient » — it
/// is that a patient-identifying value must not reach the file. <c>LogMask.Name</c> /
/// <c>LogMask.FileName</c> / <c>ReminderPhone.Mask</c> in the same statement is what says so, and keeping the
/// placeholder's name honest (<c>{PatientName}</c>, holding <c>M… (7)</c>) is better than renaming it to
/// something that hides what the field is.</para>
///
/// <para>⚠️ <b>The forbidden set is deliberately specific.</b> <c>{Name}</c> and <c>{Message}</c> are not on it:
/// they name a clinic, a procedure type, a queue message and a hundred other things, so including them would
/// produce a wall of false positives — and a guard whose output is mostly noise is a guard that gets an
/// exemption list, then two, then deleted. What is here is the vocabulary this product actually uses for a
/// person.</para>
/// </summary>
public class LogTemplateCoverageTests
{
    /// <summary>
    /// Placeholder names that identify a human being. Matched case-insensitively and on the whole placeholder,
    /// so <c>{PatientId}</c> — an opaque GUID, and the *replacement* this feature moved every scrubbed statement
    /// onto — is deliberately absent.
    /// </summary>
    private static readonly HashSet<string> PatientIdentifyingNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Patient", "PatientName", "PatientFullName", "PatientFirstName", "PatientLastName",
        "FirstName", "LastName", "FullName",
        "Email", "PatientEmail", "UserEmail",
        "Phone", "PhoneNumber", "PatientPhone", "Mobile",
        "DateOfBirth", "BirthDate", "Cin", "CnamId", "InsuranceNumber",
        "FileName", "AttachmentFileName", "DocumentFileName"
    };

    /// <summary>
    /// Calls that render a value safe. Presence anywhere in the statement is enough — a statement that masks one
    /// of two names is still worth a human look, but this guard's job is the systematic case, and demanding
    /// per-argument analysis would mean parsing C# rather than scanning it.
    /// </summary>
    private static readonly string[] Maskers = { "LogMask.", "ReminderPhone.Mask" };

    /// <summary>
    /// <b>Expressions that YIELD a person's name, whatever the placeholder holding them is called.</b>
    ///
    /// <para>⚠️ <b>This is the hole the name-based rule above had, and it was live.</b> The rule reads placeholder
    /// <i>names</i> — so <c>{PatientName}</c> is caught and <c>{Summary}</c> is not. But
    /// <c>GoogleCalendarSyncService</c> builds its calendar summary as
    /// <c>$"Appointment: {patient.GetFullName()}"</c> and logged it through placeholders called
    /// <c>{Summary}</c> and <c>{Events}</c> — seven statements, two of them at Information and Warning and
    /// therefore in the durable rolling file. Every one passed a green guard, because a name-based check cannot
    /// see a value. So the guard now reads the ARGUMENTS too.</para>
    ///
    /// <para>⚠️ <b>Kept deliberately short and high-signal.</b> These are accessors that can only be a person, so
    /// a hit is a real finding rather than something to be exempted. Matching a bare <c>.Name</c> would flag a
    /// clinic, a procedure and a supplier on every other line — and a guard whose output is mostly noise gets an
    /// exemption list, then two, then deleted. <c>.Summary</c> earns its place because in this solution the only
    /// thing carrying that member is a Google calendar event, whose summary is a patient name by construction.</para>
    /// </summary>
    /// <para>⚠️ <b><c>.FullName</c> is deliberately absent.</b> It is a BCL member on <c>FileSystemInfo</c> and
    /// <c>Type</c> before it is anything of ours, and it fired on
    /// <c>PgDumpBackupService.LogInformation("Pruned old backup folder {Folder}.", ours[i].FullName)</c> — a
    /// directory path. The domain's own <c>GetFullName()</c> covers the person case, and the placeholder rule
    /// still catches a <c>{FullName}</c> placeholder, so nothing is lost by keeping the noise out.</para>
    private static readonly string[] PatientIdentifyingExpressions =
    {
        "GetFullName()",
        ".FirstName",
        ".LastName",
        ".PatientName",
        ".Summary",
    };

    /// <summary>
    /// Statements that legitimately name one of the above, each with the reason. Asserted <b>equal in both
    /// directions</b>, so a stale entry fails as loudly as a new violation — the house style, and the half that
    /// stops an exemption outliving the code it was written for.
    ///
    /// <para>Keyed on <c>file:placeholder</c> rather than on a line number, which every edit above it changes.</para>
    /// </summary>
    private static readonly Dictionary<string, string> AllowedByDesign = new(StringComparer.Ordinal)
    {
    };

    [Fact]
    public void No_Log_Template_Names_A_Patient()
    {
        var violations = Violations();

        Assert.Equal(
            AllowedByDesign.Keys.OrderBy(k => k, StringComparer.Ordinal),
            violations.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>
    /// Non-vacuity. Reflection and source scans fail <b>open</b>: a moved folder, a renamed project or a regex
    /// that stopped matching would leave this class green for ever while checking nothing — which is precisely
    /// how <c>SystemWideCallerCoverageTests</c>' console-verb branch matched nothing for two features.
    /// </summary>
    [Fact]
    public void The_Scan_Finds_A_Substantial_Number_Of_Log_Statements()
    {
        var templates = AllTemplates().ToList();

        Assert.True(
            templates.Count > 200,
            $"Only {templates.Count} log templates found — the scan has stopped seeing the solution's sources.");
    }

    /// <summary>
    /// And that it can still see the placeholders inside them, which is the second way this could pass while
    /// checking nothing: finding every statement and reading no names out of any of them.
    /// </summary>
    [Fact]
    public void The_Scan_Reads_Placeholders_Out_Of_Those_Templates()
    {
        var placeholders = AllTemplates()
            .SelectMany(t => Placeholders(t.Template))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(placeholders.Count > 50, $"Only {placeholders.Count} distinct placeholders found.");
        Assert.Contains("PatientId", placeholders, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The executed red proof: the <b>real</b> scanner over a statement that names a patient, so a green run
    /// above is evidence rather than an absence of evidence.
    /// </summary>
    [Fact]
    public void The_Guard_Rejects_A_Template_That_Names_A_Patient()
    {
        const string offending =
            "_logger.LogInformation(\"Sent the reminder to {PatientName} at {Phone}\", patient.GetFullName(), phone);";

        var found = ViolationsIn("Probe.cs", offending);

        Assert.Contains("Probe.cs:PatientName", found);
        Assert.Contains("Probe.cs:Phone", found);
    }

    /// <summary>And the mirror: the same statement with the values masked must NOT be reported.</summary>
    [Fact]
    public void The_Guard_Accepts_A_Template_Whose_Value_Is_Masked()
    {
        const string masked =
            "_logger.LogInformation(\"Sent the reminder to {PatientName} at {Phone}\", "
            + "LogMask.Name(patient.GetFullName()), ReminderPhone.Mask(phone));";

        Assert.Empty(ViolationsIn("Probe.cs", masked));
    }

    /// <summary>
    /// The red proof for the <b>expression</b> rule, written as the real defect it was found as: an innocent
    /// placeholder name over a value that is a patient. Before this rule the statement below was green, and
    /// seven of its shape were live in <c>GoogleCalendarSyncService</c> — two of them writing a patient's name
    /// into the durable log file at Information and Warning.
    /// </summary>
    [Fact]
    public void The_Guard_Rejects_An_Innocent_Placeholder_Over_A_Patient_Value()
    {
        const string offending =
            "_logger.LogWarning(\"Cannot parse event {EventId}: summary '{Summary}'\", e.Id, e.Summary);";

        Assert.Contains("Probe.cs:.Summary", ViolationsIn("Probe.cs", offending));
    }

    /// <summary>Its mirror, so the rule is a rule and not a permanent red.</summary>
    [Fact]
    public void The_Guard_Accepts_That_Same_Value_Masked()
    {
        const string masked =
            "_logger.LogWarning(\"Cannot parse event {EventId}: summary '{Summary}'\", e.Id, LogMask.Name(e.Summary));";

        Assert.Empty(ViolationsIn("Probe.cs", masked));
    }

    // ---------------------------------------------------------------- the scan

    private static Dictionary<string, string> Violations()
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (file, template, statement) in AllTemplates())
        {
            if (Maskers.Any(m => statement.Contains(m, StringComparison.Ordinal)))
            {
                continue;
            }

            foreach (var offender in Offenders(template, statement))
            {
                found[$"{file}:{offender}"] = template;
            }
        }

        return found;
    }

    /// <summary>
    /// What is wrong with one statement: a placeholder <b>named</b> for a person, or an argument expression that
    /// <b>evaluates to</b> one. Both are reported in the same <c>file:token</c> shape so a single allow-list and
    /// a single both-directions assertion cover them.
    /// </summary>
    private static IEnumerable<string> Offenders(string template, string statement) =>
        Placeholders(template)
            .Where(PatientIdentifyingNames.Contains)
            .Concat(PatientIdentifyingExpressions
                .Where(e => statement.Contains(e, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal);

    private static IReadOnlyList<string> ViolationsIn(string file, string source) =>
        LogStatements(source)
            .Where(s => !Maskers.Any(m => s.Statement.Contains(m, StringComparison.Ordinal)))
            .SelectMany(s => Offenders(s.Template, s.Statement))
            .Select(p => $"{file}:{p}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<(string File, string Template, string Statement)> AllTemplates()
    {
        var root = SolutionSources.Root();

        foreach (var file in SolutionSources.CsFiles(root))
        {
            // The test project itself is excluded: this very class contains the probe strings above, and a
            // guard that fails on its own red proof is a guard nobody can write.
            if (file.Contains($"{Path.DirectorySeparatorChar}ClinicManagement.UnitTests{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (template, statement) in LogStatements(File.ReadAllText(file)))
            {
                yield return (Path.GetFileName(file), template, statement);
            }
        }
    }

    /// <summary>
    /// Every <c>Log&lt;Level&gt;(…)</c> call, paired with its message template.
    ///
    /// <para>The template is the first string literal of the call, which for <c>ILogger</c> is either the message
    /// itself or — when the first argument is an exception — the one after it. Taking the first literal covers
    /// both, since an exception argument is not a literal.</para>
    /// </summary>
    private static IEnumerable<(string Template, string Statement)> LogStatements(string source)
    {
        foreach (Match call in Regex.Matches(
                     source, @"\.Log(?:Trace|Debug|Information|Warning|Error|Critical)\s*\("))
        {
            var statement = StatementFrom(source, call.Index + call.Length - 1);
            if (statement is null)
            {
                continue;
            }

            var literal = Regex.Match(statement, "\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (literal.Success)
            {
                yield return (literal.Groups[1].Value, statement);
            }
        }
    }

    /// <summary>
    /// The call's text, from its opening parenthesis to the matching close. A depth counter that skips string
    /// literals — a template legitimately contains parentheses, and cutting at the first <c>)</c> would truncate
    /// the arguments and lose the masker this guard reads.
    /// </summary>
    private static string? StatementFrom(string source, int openParen)
    {
        var depth = 0;
        var inString = false;

        for (var i = openParen; i < source.Length; i++)
        {
            var c = source[i];

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        return source[openParen..(i + 1)];
                    }

                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// The names inside <c>{…}</c>, dropping Serilog's <c>@</c>/<c>$</c> prefixes and any alignment or format
    /// specifier — <c>{Count,5:N0}</c> is the property <c>Count</c>.
    /// </summary>
    private static IEnumerable<string> Placeholders(string template) =>
        Regex.Matches(template, @"\{([@$]?)([A-Za-z_][A-Za-z0-9_]*)[^}]*\}")
            .Select(m => m.Groups[2].Value);
}

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using StepRequest = ClinicManagement.Application.Features.TreatmentPlans.Commands.TreatmentPlanItemStepRequest;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// The séances a followed treatment is carried out over, as the dentist confirmed them <b>for this patient</b>.
///
/// <para>
/// A multi-séance act is now split <b>by default</b> when it is booked, and the protocol is editable in the
/// booking dialog before anything is created — « sometimes the steps vary depending on patient ». Two rules
/// carry that, and both are the kind that fail in silence: the tri-state <c>Steps</c> (absent ≠ empty), and the
/// per-step minimum interval, which the step editor's request object did not carry at all.
/// </para>
/// </summary>
public class StartTreatmentStepsTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IProcedureTypeRepository> _procedureTypes = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    /// <summary>The plan the handler actually persisted — every assertion below reads this.</summary>
    private TreatmentPlan? _saved;

    public StartTreatmentStepsTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

        var patient = new Patient(PatientId, ClinicId, "Amine", "Trabelsi", new DateTime(1985, 4, 12), "Male");
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        _plans.Setup(r => r.AddAsync(It.IsAny<TreatmentPlan>(), It.IsAny<CancellationToken>()))
            .Callback<TreatmentPlan, CancellationToken>((p, _) => _saved = p)
            .Returns((TreatmentPlan p, CancellationToken _) => Task.FromResult(p));
    }

    /// <summary>« Couronne / bridge », whose catalogue protocol proposes three séances — the client's own case.</summary>
    private Guid CrownWithProtocol()
    {
        var procedure = new ProcedureType(
            Guid.NewGuid(), ClinicId, "Couronne / bridge (par élément)", 30, ColorHex.FromString("#4F83CC"),
            defaultCost: 500m);
        procedure.SetDefaultSteps(new[]
        {
            new ProcedureStepTemplate("Préparation et empreinte", 60, null),
            new ProcedureStepTemplate("Essayage", 30, 7),
            new ProcedureStepTemplate("Pose de la couronne", 30, 14),
        });
        _procedureTypes.Setup(r => r.GetByIdAsync(procedure.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(procedure);
        return procedure.Id;
    }

    private StartTreatmentCommandHandler Handler() => new(
        _plans.Object, _patients.Object, _procedureTypes.Object, _clinicResolver.Object, _uow.Object,
        NullLogger<StartTreatmentCommandHandler>.Instance);

    private async Task<TreatmentPlanItem> StartAsync(List<StepRequest>? steps)
    {
        var result = await Handler().Handle(
            new StartTreatmentCommand
            {
                PatientId = PatientId,
                ProcedureTypeId = CrownWithProtocol(),
                Steps = steps,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(_saved);
        return _saved!.Items.Single();
    }

    // ---------------------------------------------------------------------------------------------------------
    // The tri-state
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Absent means « nobody decided », and the catalogue protocol applies — every caller written before the
    /// field, and what a booking with the séances untouched still sends.
    /// </summary>
    [Fact]
    public async Task No_steps_supplied_takes_the_catalogue_protocol()
    {
        var item = await StartAsync(null);

        Assert.Equal(
            new[] { "Préparation et empreinte", "Essayage", "Pose de la couronne" },
            item.Steps.OrderBy(s => s.SequenceNumber).Select(s => s.Label));
    }

    /// <summary>
    /// An empty list is « cet acte se fait en une séance », and it must <b>refuse</b> the protocol rather than
    /// fall through to it. `?? DefaultSteps` is the natural way to write this line and it silently re-proposes
    /// the séances the dentist has just declined.
    /// </summary>
    [Fact]
    public async Task An_empty_list_means_one_seance_and_never_falls_back_to_the_catalogue()
    {
        var item = await StartAsync(new List<StepRequest>());

        Assert.Empty(item.Steps);
    }

    /// <summary>
    /// A confirmed list is used verbatim, in its own order — this is « the steps vary depending on the patient »,
    /// and the whole reason the field exists.
    /// </summary>
    [Fact]
    public async Task A_confirmed_list_replaces_the_protocol_in_its_own_order()
    {
        var item = await StartAsync(new List<StepRequest>
        {
            new() { Label = "Empreinte", EstimatedDurationMinutes = 45, MinDaysAfterPrevious = null },
            new() { Label = "Pose", EstimatedDurationMinutes = 30, MinDaysAfterPrevious = 21 },
        });

        var steps = item.Steps.OrderBy(s => s.SequenceNumber).ToList();
        Assert.Equal(new[] { "Empreinte", "Pose" }, steps.Select(s => s.Label));
        Assert.Equal(new int?[] { 45, 30 }, steps.Select(s => s.EstimatedDurationMinutes));
    }

    /// <summary>
    /// ⚠️ The catalogue is a <b>proposal</b>, and confirming a different list for one patient must not write
    /// back to it — « le protocole par défaut ne bouge pas » is the half of the request that is easiest to
    /// break by reaching for the entity that is already in hand.
    /// </summary>
    [Fact]
    public async Task Confirming_a_different_list_leaves_the_catalogue_protocol_alone()
    {
        var procedureId = CrownWithProtocol();
        var procedure = await _procedureTypes.Object.GetByIdAsync(procedureId, CancellationToken.None);

        await Handler().Handle(
            new StartTreatmentCommand
            {
                PatientId = PatientId,
                ProcedureTypeId = procedureId,
                Steps = new List<StepRequest> { new() { Label = "En une fois" } },
            },
            CancellationToken.None);

        Assert.Equal(
            new[] { "Préparation et empreinte", "Essayage", "Pose de la couronne" },
            procedure!.DefaultSteps.Select(s => s.Label));
    }

    // ---------------------------------------------------------------------------------------------------------
    // The interval — a different quantity from the chair time, and it was being dropped
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The minimum wait survives the round trip. It is not the chair time: an implant's osseointegration is
    /// eight to twelve weeks of <c>MinDaysAfterPrevious</c> and zero minutes of
    /// <c>EstimatedDurationMinutes</c>, and <c>RecallWorklistRules</c> reads the first — so a protocol that
    /// loses it reports a correctly-progressing implant as abandoned after a flat fortnight.
    /// </summary>
    [Fact]
    public async Task The_minimum_interval_reaches_the_treatment()
    {
        var item = await StartAsync(new List<StepRequest>
        {
            new() { Label = "Pose de l'implant", EstimatedDurationMinutes = 90, MinDaysAfterPrevious = null },
            new() { Label = "Mise en charge", EstimatedDurationMinutes = 45, MinDaysAfterPrevious = 84 },
        });

        var steps = item.Steps.OrderBy(s => s.SequenceNumber).ToList();
        Assert.Null(steps[0].MinDaysAfterPrevious);
        Assert.Equal(84, steps[1].MinDaysAfterPrevious);
    }

    /// <summary>
    /// The protocol's own intervals are copied too. They were, before this change — the defect was one layer up
    /// (the step <i>editor</i>'s request had no such field), and this pins the half that was already right.
    /// </summary>
    [Fact]
    public async Task The_catalogue_protocols_intervals_are_copied()
    {
        var item = await StartAsync(null);

        Assert.Equal(
            new int?[] { null, 7, 14 },
            item.Steps.OrderBy(s => s.SequenceNumber).Select(s => s.MinDaysAfterPrevious));
    }

    // ---------------------------------------------------------------------------------------------------------
    // The derived guard
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠️ <b>A <c>TreatmentPlanItemStepInput</c> that COPIES a step carries that step's interval.</b>
    ///
    /// <para>
    /// The fourth argument is <c>MinDaysAfterPrevious</c> and it has a <b>default of null</b>, so a
    /// three-argument call compiles, reads correctly, and silently erases the interval — and
    /// <c>SetSteps</c> has replace semantics, so it erases it from <i>every</i> step of the act, including ones
    /// the caller only echoed back. <c>SetTreatmentPlanItemStepsCommand</c> was exactly that
    /// (<c>new TreatmentPlanItemStepInput(s.Id, s.Label, s.EstimatedDurationMinutes)</c>): renaming one step of
    /// an implant wiped the osseointegration wait off all of them, with no error anywhere and the client having
    /// sent the field all along.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>It flags a COPY, not every three-argument call</b>, and that distinction is the whole design of the
    /// guard. <c>ContinueRecordedActCommand</c> synthesises two séances out of prose — « ce qui a été fait » and
    /// « ce qui reste » — and there is no source interval for it to carry; demanding a fourth argument there
    /// would be noise, and a guard that reports things nobody should act on stops being read. A copy is
    /// recognised by two or more arguments reading members off the <b>same</b> receiver, which is what every
    /// mapping from a request, a template or an existing step looks like.
    /// </para>
    ///
    /// <para>
    /// Production code only, on <c>ClinicCreationEntitlementTests</c>' reasoning: a fixture that writes three
    /// arguments is stating « this séance has no minimum wait », which is a real thing to say.
    /// </para>
    /// </summary>
    [Fact]
    public void A_step_input_copied_from_a_source_carries_its_interval()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();
        var copies = 0;

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(root, "api"), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file);
            if (rel.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                rel.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                rel.StartsWith($"api{Path.DirectorySeparatorChar}ClinicManagement.UnitTests"))
            {
                continue;
            }

            var src = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match m in Regex.Matches(
                         src, @"new\s+TreatmentPlanItemStepInput\s*\("))
            {
                var args = ArgumentsOf(src, m.Index + m.Length);
                if (args is null) continue;

                // A copy: two or more arguments reading members off one receiver (`s.Id`, `s.Label`, …).
                var receivers = args
                    .Select(a => Regex.Match(a, @"^([A-Za-z_][A-Za-z0-9_]*)\.[A-Za-z_]"))
                    .Where(r => r.Success)
                    .GroupBy(r => r.Groups[1].Value)
                    .Where(g => g.Count() >= 2)
                    .ToList();
                if (receivers.Count == 0) continue;

                copies++;
                if (args.Count == 4) continue;
                offenders.Add(
                    $"{rel}:{src.Take(m.Index).Count(c => c == '\n') + 1} — copies from " +
                    $"`{receivers[0].Key}` with {args.Count} arguments, so MinDaysAfterPrevious defaults to " +
                    "null and every step of the act loses its minimum wait");
            }
        }

        // Tripwire: the record was renamed or the copies restructured, so the scan is measuring nothing.
        Assert.True(copies > 0, "found no TreatmentPlanItemStepInput copied from a source — the scan is broken");
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Top-level arguments of a call whose opening paren has just been consumed, or null if unbalanced.</summary>
    private static List<string>? ArgumentsOf(string src, int afterOpenParen)
    {
        var args = new List<string>();
        var depth = 1;
        var start = afterOpenParen;
        for (var i = afterOpenParen; i < src.Length; i++)
        {
            var c = src[i];
            if (c is '(' or '[') depth++;
            else if (c is ']') depth--;
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    var last = src[start..i].Trim();
                    if (last.Length > 0) args.Add(last);
                    return args;
                }
            }
            else if (c == ',' && depth == 1)
            {
                args.Add(src[start..i].Trim());
                start = i + 1;
            }
        }
        return null;
    }

    /// <summary>
    /// Found through <c>[CallerFilePath]</c>, not <c>AppContext.BaseDirectory</c>: this suite is routinely built
    /// to a temporary output path (Smart App Control refuses freshly-built in-repo assemblies), so the binary's
    /// own directory says nothing about where the repository is.
    /// </summary>
    private static string RepositoryRoot([CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

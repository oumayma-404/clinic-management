using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Audit;
using ClinicManagement.Application.Features.Audit.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Audit;

/// <summary>
/// [I6] The read side of « Journal d'activité »: the query's clinic scoping and date arithmetic, the actor seam
/// that decides whose name goes on a row, and the French labels.
///
/// <para>The load-bearing case here is <see cref="Window_Is_The_Clinic_Local_Day_Inclusive_On_Both_Ends"/>.
/// Everything else in this file would still read plausibly with an off-by-an-hour window; that one is the
/// difference between « le 3 août » meaning the clinic's day and meaning 01:00-to-01:00, which files an action
/// taken at 00:30 under the previous day — finding #20's shape, and the reason
/// <c>LastTickOfLocalDayUtc</c> exists at all.</para>
/// </summary>
public class AuditLedgerReadTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IAuditEntryRepository> _audit = new();
    private readonly Mock<ICurrentClinicResolver> _clinic = new();

    public AuditLedgerReadTests()
    {
        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _audit.Setup(r => r.GetRecordedEntityTypesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        Respond();
    }

    private GetAuditEntriesQueryHandler Handler() =>
        new(_audit.Object, _clinic.Object, NullLogger<GetAuditEntriesQueryHandler>.Instance);

    private static AuditEntry Row(
        string userId = "local|owner",
        string? email = "owner@clinic.tn",
        string entityType = nameof(Patient),
        AuditAction action = AuditAction.Delete) =>
        new(ClinicId, userId, email, entityType, "11111111-1111-1111-1111-111111111111", action,
            "LastName: Ben Salah", new DateTime(2026, 8, 3, 9, 0, 0, DateTimeKind.Utc));

    private void Respond(params AuditEntry[] rows) =>
        _audit.Setup(r => r.GetFilteredAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(), It.IsAny<AuditAction?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<AuditEntry>(rows, page: 1, pageSize: rows.Length, totalCount: rows.Length));

    // ---------------------------------------------------------------- clinic scoping

    // The ledger is a per-clinic read, and it is the one read where a leak would hand over another practice's
    // whole activity history. `AuditEntries` deliberately carries NO global query filter (its ClinicId is
    // nullable), so this explicit argument is the only thing scoping it — worth pinning directly.
    [Fact]
    public async Task Reads_Only_The_Callers_Clinic()
    {
        await Handler().Handle(new GetAuditEntriesQuery(), CancellationToken.None);

        _audit.Verify(r => r.GetFilteredAsync(
            ClinicId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<AuditAction?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Fails_When_No_Clinic_Is_In_Scope()
    {
        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("Cabinet introuvable."));

        var result = await Handler().Handle(new GetAuditEntriesQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _audit.VerifyNoOtherCalls();
    }

    // ---------------------------------------------------------------- the window

    /// <summary>
    /// [I6] « Le 3 août » means the clinic's calendar day, both ends inclusive: from 23:00 UTC on the 2nd
    /// (Tunisia is UTC+1) to the last tick before 23:00 UTC on the 3rd.
    ///
    /// <para>The upper bound is asserted as a <b>tick inside the day</b>, not the next midnight. The obvious
    /// helper — <c>EndOfLocalDayUtc</c> — is exclusive, and using it here would make an action logged exactly at
    /// midnight appear in both adjacent days. Fixed instants throughout: a test that recomputed the boundary the
    /// way the production code does would agree with an off-by-one by construction.</para>
    /// </summary>
    [Fact]
    public async Task Window_Is_The_Clinic_Local_Day_Inclusive_On_Both_Ends()
    {
        DateTime? from = null, to = null;
        _audit.Setup(r => r.GetFilteredAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(), It.IsAny<AuditAction?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string?, string?, DateTime?, DateTime?, AuditAction?, PageRequest?, CancellationToken>(
                (_, _, _, f, t, _, _, _) => { from = f; to = t; })
            .ReturnsAsync(PagedResult<AuditEntry>.Unpaged(Array.Empty<AuditEntry>()));

        await Handler().Handle(
            new GetAuditEntriesQuery { From = new DateTime(2026, 8, 3), To = new DateTime(2026, 8, 3) },
            CancellationToken.None);

        Assert.Equal(new DateTime(2026, 8, 2, 23, 0, 0, DateTimeKind.Utc), from);

        // One tick inside the day, i.e. the instant before the next local midnight.
        var nextLocalMidnightUtc = new DateTime(2026, 8, 3, 23, 0, 0, DateTimeKind.Utc);
        Assert.Equal(nextLocalMidnightUtc.AddTicks(-1), to);
        Assert.True(to < nextLocalMidnightUtc, "The upper bound must be inside the day, not the next midnight.");
    }

    [Fact]
    public async Task No_Dates_Means_No_Bounds()
    {
        DateTime? from = new DateTime(1999, 1, 1), to = new DateTime(1999, 1, 1);
        _audit.Setup(r => r.GetFilteredAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(), It.IsAny<AuditAction?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string?, string?, DateTime?, DateTime?, AuditAction?, PageRequest?, CancellationToken>(
                (_, _, _, f, t, _, _, _) => { from = f; to = t; })
            .ReturnsAsync(PagedResult<AuditEntry>.Unpaged(Array.Empty<AuditEntry>()));

        await Handler().Handle(new GetAuditEntriesQuery(), CancellationToken.None);

        Assert.Null(from);
        Assert.Null(to);
    }

    // ---------------------------------------------------------------- the action filter

    [Theory]
    [InlineData("Delete", AuditAction.Delete)]
    [InlineData("delete", AuditAction.Delete)]
    [InlineData("INSERT", AuditAction.Insert)]
    [InlineData("Update", AuditAction.Update)]
    public async Task Parses_The_Action_Filter_Case_Insensitively(string input, AuditAction expected)
    {
        AuditAction? captured = null;
        CaptureAction(a => captured = a);

        await Handler().Handle(new GetAuditEntriesQuery { Action = input }, CancellationToken.None);

        Assert.Equal(expected, captured);
    }

    // Tolerant, not strict — the same rule as the lab-order stage filter and the procedure-type category filter.
    // A stale bookmark should show the full ledger, not a French error about a query-string value.
    [Theory]
    [InlineData("Nonsense")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Ignores_An_Unrecognised_Action_Rather_Than_Refusing(string? input)
    {
        AuditAction? captured = AuditAction.Update;
        CaptureAction(a => captured = a);

        var result = await Handler().Handle(new GetAuditEntriesQuery { Action = input }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(captured);
    }

    private void CaptureAction(Action<AuditAction?> capture) =>
        _audit.Setup(r => r.GetFilteredAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(), It.IsAny<AuditAction?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string?, string?, DateTime?, DateTime?, AuditAction?, PageRequest?, CancellationToken>(
                (_, _, _, _, _, a, _, _) => capture(a))
            .ReturnsAsync(PagedResult<AuditEntry>.Unpaged(Array.Empty<AuditEntry>()));

    // ---------------------------------------------------------------- the projection

    [Fact]
    public async Task Projects_A_Person_With_Their_Email_And_French_Labels()
    {
        Respond(Row(entityType: nameof(Invoice), action: AuditAction.Update));

        var result = await Handler().Handle(new GetAuditEntriesQuery(), CancellationToken.None);

        var dto = Assert.Single(result.Value!.Items);
        Assert.Equal("owner@clinic.tn", dto.ActorLabel);
        Assert.False(dto.IsSystemActor);
        Assert.Equal("Note d'honoraires", dto.EntityLabel);
        Assert.Equal("Modification", dto.ActionLabel);
        // The stable keys travel too, so a client can filter and group without parsing French.
        Assert.Equal(nameof(Invoice), dto.EntityType);
        Assert.Equal(nameof(AuditAction.Update), dto.Action);
    }

    // A job's row must say so plainly. An owner scanning the ledger for a colleague to ask should not be left
    // hunting for a person who does not exist.
    [Fact]
    public async Task Marks_A_Job_Row_As_A_System_Actor_And_Names_The_Job()
    {
        Respond(Row(userId: $"{AuditActor.ProcessPrefix}StockExpiryJob", email: null));

        var result = await Handler().Handle(new GetAuditEntriesQuery(), CancellationToken.None);

        var dto = Assert.Single(result.Value!.Items);
        Assert.True(dto.IsSystemActor);
        Assert.Equal("Tâche automatique (StockExpiryJob)", dto.ActorLabel);
    }

    // Falls back to the raw id rather than to « — »: an account deleted before the row was read has no email
    // left, and a visible id is still traceable whereas a dash is not.
    [Fact]
    public async Task Falls_Back_To_The_Raw_Id_When_There_Is_No_Email()
    {
        Respond(Row(userId: "local|deleted-account", email: null));

        var result = await Handler().Handle(new GetAuditEntriesQuery(), CancellationToken.None);

        Assert.Equal("local|deleted-account", Assert.Single(result.Value!.Items).ActorLabel);
    }

    // The « Type » filter's options are ordered by the FRENCH label, which is what the reader sees — the
    // repository orders by the CLR name, which files « Note d'honoraires » under I and « Dépense » under E.
    [Fact]
    public async Task Orders_The_Entity_Type_Options_By_Their_French_Label()
    {
        _audit.Setup(r => r.GetRecordedEntityTypesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { nameof(Invoice), nameof(Expense), nameof(Patient) });

        var result = await Handler().Handle(new GetAuditEntriesQuery(), CancellationToken.None);

        Assert.Equal(
            new[] { "Dépense", "Note d'honoraires", "Patient" },
            result.Value!.EntityTypes.Select(t => t.Label));
        // …and each option still carries the stable key the filter is sent back with.
        Assert.Equal(
            new[] { nameof(Expense), nameof(Invoice), nameof(Patient) },
            result.Value!.EntityTypes.Select(t => t.Value));
    }

    // ---------------------------------------------------------------- labels

    // An unmapped aggregate degrades to its own CLR name rather than to « Inconnu ». This map is the one part of
    // the ledger a human still maintains, and « ProcedureTypeMaterial » at least tells an owner what was touched.
    [Fact]
    public void An_Unmapped_Entity_Type_Keeps_Its_Own_Name()
    {
        Assert.Equal("SomethingNewNobodyMapped", AuditLabels.Entity("SomethingNewNobodyMapped"));
    }

    [Theory]
    [InlineData(AuditAction.Insert, "Création")]
    [InlineData(AuditAction.Update, "Modification")]
    [InlineData(AuditAction.Delete, "Suppression")]
    public void Every_Action_Has_A_French_Label(AuditAction action, string expected)
    {
        Assert.Equal(expected, AuditLabels.Action(action));
    }

    // An unnamed process still reads as a process, not as a person called « unknown ».
    [Fact]
    public void An_Unnamed_Process_Reads_As_A_Bare_Automatic_Task()
    {
        Assert.Equal("Tâche automatique", AuditLabels.Actor(AuditActor.Unknown.UserId, null));
    }

    // ---------------------------------------------------------------- the actor seam

    // A signed-in user's identity outranks any declared process name, so a helper that calls RunAs while running
    // inside somebody's request cannot claim their work.
    [Fact]
    public void The_Token_Outranks_A_Declared_Process_Name()
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns("local|real-person");
        context.Setup(c => c.GetUserEmail()).Returns("person@clinic.tn");
        var provider = new AuditActorProvider(context.Object);

        provider.RunAs("SomeJob");

        Assert.Equal("local|real-person", provider.Current.UserId);
        Assert.Equal("person@clinic.tn", provider.Current.Email);
        Assert.False(provider.Current.IsProcess);
    }

    // No token and no declaration is still a row — `job|unknown`, not a skipped entry. A gap in the ledger is
    // indistinguishable from « nothing happened », which is the one thing it must never claim.
    [Fact]
    public void No_Token_And_No_Declaration_Resolves_To_An_Unknown_Process()
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns((string?)null);
        var provider = new AuditActorProvider(context.Object);

        Assert.Equal(AuditActor.Unknown.UserId, provider.Current.UserId);
        Assert.True(provider.Current.IsProcess);
    }

    [Fact]
    public void A_Job_Without_A_Token_Is_Recorded_Under_Its_Own_Name()
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns((string?)null);
        var provider = new AuditActorProvider(context.Object);

        provider.RunAs("NotificationJob");

        Assert.Equal($"{AuditActor.ProcessPrefix}NotificationJob", provider.Current.UserId);
        Assert.True(provider.Current.IsProcess);
        Assert.Null(provider.Current.Email);
    }

    // Resolve-once, and it is load-bearing rather than an optimisation: one operation must carry one actor even
    // when it changes the caller's own account mid-flight (a role change bumping TokenVersion, a password change).
    [Fact]
    public void The_Actor_Is_Resolved_Once_Per_Scope()
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns("local|first");
        var provider = new AuditActorProvider(context.Object);

        var first = provider.Current;
        context.Setup(c => c.GetUserId()).Returns("local|second");

        Assert.Equal(first.UserId, provider.Current.UserId);
    }

    // A declaration arriving after the actor has been read would disagree with the rows already written, so the
    // first read wins.
    [Fact]
    public void RunAs_After_The_First_Read_Is_A_No_Op()
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns((string?)null);
        var provider = new AuditActorProvider(context.Object);

        _ = provider.Current;
        provider.RunAs("TooLateJob");

        Assert.Equal(AuditActor.Unknown.UserId, provider.Current.UserId);
    }

    // The console verbs' floor implementation (registered by AddInfrastructure with TryAdd, since they build no
    // Application container). Same first-read-wins rule.
    [Fact]
    public void The_Process_Only_Provider_Honours_RunAs_And_Defaults_To_Unknown()
    {
        var declared = new ProcessAuditActorProvider();
        declared.RunAs("reset-admin-password");
        Assert.Equal($"{AuditActor.ProcessPrefix}reset-admin-password", declared.Current.UserId);

        var silent = new ProcessAuditActorProvider();
        Assert.Equal(AuditActor.Unknown.UserId, silent.Current.UserId);

        var late = new ProcessAuditActorProvider();
        _ = late.Current;
        late.RunAs("verify-schema");
        Assert.Equal(AuditActor.Unknown.UserId, late.Current.UserId);
    }

    // A blank process name must not produce the bare prefix `job|`, which would render as an empty actor.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Process_Name_Falls_Back_To_Unknown(string blank)
    {
        Assert.Equal(AuditActor.Unknown.UserId, AuditActor.Process(blank).UserId);
    }
}

using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// [I6] The audit-ledger row's own invariants. Pure — no mocks, no context.
///
/// <para>Small surface, but two of these are the ones that decide whether the ledger can be trusted: an actor is
/// mandatory (a row nobody can be named for is still a row, and it says <c>job|unknown</c> rather than being
/// dropped), and the changed-field summary is capped <b>by the entity</b> rather than by whoever happens to be
/// writing it.</para>
/// </summary>
public class AuditEntryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime OccurredAt = new(2026, 8, 3, 9, 30, 0, DateTimeKind.Utc);

    private static AuditEntry Entry(
        Guid? clinicId = null,
        string userId = "local|abc",
        string? userEmail = "owner@clinic.tn",
        string entityType = nameof(Patient),
        string entityId = "11111111-1111-1111-1111-111111111111",
        AuditAction action = AuditAction.Delete,
        string? changedFields = "LastName: Ben Salah") =>
        new(clinicId ?? ClinicId, userId, userEmail, entityType, entityId, action, changedFields, OccurredAt);

    [Fact]
    public void Records_Every_Field_It_Was_Given()
    {
        var entry = Entry();

        Assert.Equal(ClinicId, entry.ClinicId);
        Assert.Equal("local|abc", entry.UserId);
        Assert.Equal("owner@clinic.tn", entry.UserEmail);
        Assert.Equal(nameof(Patient), entry.EntityType);
        Assert.Equal("11111111-1111-1111-1111-111111111111", entry.EntityId);
        Assert.Equal(AuditAction.Delete, entry.Action);
        Assert.Equal("LastName: Ben Salah", entry.ChangedFields);
        Assert.Equal(OccurredAt, entry.OccurredAt);
        Assert.NotEqual(Guid.Empty, entry.Id);
    }

    // A null clinic is legitimate — a job or a console verb can mutate a row with no clinic derivable from it.
    // Guid.Empty would be a sentinel reading as a real clinic in the (ClinicId, OccurredAt) index; null says
    // « unattributed », which is true and queryable (DEV-4).
    //
    // Constructed directly rather than through `Entry(clinicId: null)`: that helper coalesces a null argument to
    // the default clinic, so it cannot express the very case under test — it would have passed against a
    // non-nullable column.
    [Fact]
    public void Accepts_A_Null_Clinic()
    {
        var entry = new AuditEntry(
            clinicId: null,
            userId: $"{ClinicManagement.Application.Common.Interfaces.AuditActor.ProcessPrefix}verify-schema",
            userEmail: null,
            entityType: nameof(User),
            entityId: "local|orphan",
            action: AuditAction.Update,
            changedFields: null,
            occurredAt: OccurredAt);

        Assert.Null(entry.ClinicId);
    }

    // The actor is the one thing a row cannot be written without: an audit entry that cannot name anybody is
    // indistinguishable from a mutation nobody made.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_A_Blank_Actor(string blank)
    {
        Assert.Throws<ArgumentException>(() => Entry(userId: blank));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Refuses_A_Blank_Entity_Type(string blank)
    {
        Assert.Throws<ArgumentException>(() => Entry(entityType: blank));
    }

    // A row that cannot say WHICH record changed is unusable rather than partial — « un patient a été supprimé »
    // with no id answers nothing.
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Refuses_A_Blank_Entity_Id(string blank)
    {
        Assert.Throws<ArgumentException>(() => Entry(entityId: blank));
    }

    // Truncation belongs to the entity, not the caller: the cap is a property of the column, and leaving it to
    // every future writer is how one of them eventually forgets.
    [Fact]
    public void Truncates_An_Over_Long_Summary_And_Marks_It_Elided()
    {
        var summary = new string('x', AuditEntry.MaxChangedFieldsLength + 250);

        var entry = Entry(changedFields: summary);

        Assert.NotNull(entry.ChangedFields);
        Assert.Equal(AuditEntry.MaxChangedFieldsLength, entry.ChangedFields!.Length);
        // The ellipsis is what lets a reader tell a short list from a cut one.
        Assert.EndsWith("…", entry.ChangedFields);
    }

    [Fact]
    public void Keeps_A_Summary_That_Fits_Untouched()
    {
        var entry = Entry(changedFields: "Status: Issued → Cancelled");

        Assert.Equal("Status: Issued → Cancelled", entry.ChangedFields);
    }

    // Blank and whitespace normalise to null so « no summary » is one value, not three.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalises_An_Empty_Summary_To_Null(string? empty)
    {
        var entry = Entry(changedFields: empty);

        Assert.Null(entry.ChangedFields);
    }

    [Fact]
    public void Trims_The_Summary()
    {
        var entry = Entry(changedFields: "  Status  ");

        Assert.Equal("Status", entry.ChangedFields);
    }
}

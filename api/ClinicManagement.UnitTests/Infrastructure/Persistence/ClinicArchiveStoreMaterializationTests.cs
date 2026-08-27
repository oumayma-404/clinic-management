using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicManagement.Application.Features.Backup.Archive;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// The row round trip at the heart of the restore: a live entity read out to JSON and materialised back
/// <b>without any domain constructor running</b> (<c>clinic-data-archive-and-restore</c>, DEV-1).
///
/// <para><b>Why this is worth reaching private members for.</b> Every primary key in this product is a GUID minted
/// <i>inside</i> the constructor and half the timestamps are stamped there from <c>DateTime.UtcNow</c>, so building
/// entities the ordinary way gives every restored row a new identity and today's date — the exact opposite of a
/// restore, and the one property AC-3 rests on. The mechanism that avoids it is private to
/// <see cref="ClinicArchiveStore"/> and its public entry points query a database, which nothing in this project
/// does; so the pure halves are invoked directly, the technique this repo already sanctions for a private function
/// that <i>is</i> the fix.</para>
///
/// <para>The model is real (Npgsql configures it from a connection string it never opens) and the change tracker
/// needs no connection, so « staged as an insert with its own id » is directly observable.</para>
/// </summary>
public class ClinicArchiveStoreMaterializationTests
{
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>A visit recorded years ago — the date is the point, since a domain constructor would stamp today.</summary>
    private static readonly DateTime LongAgo = new(2019, 3, 14, 9, 30, 0, DateTimeKind.Utc);

    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=none;Password=none")
            .Options, null);

    private static Patient APatient() =>
        new(PatientId, ClinicA, "Béchir", "Trabelsi", new DateTime(1975, 6, 2, 0, 0, 0, DateTimeKind.Utc), "Male",
            new Email("bechir@example.tn"), new PhoneNumber("+21620123456"));

    // ------------------------------------------------------------------ the store's private halves

    private static JsonObject ReadRow(IEntityType entityType, object entity, IReadOnlySet<string>? redacted = null)
    {
        var row = typeof(ClinicArchiveStore)
            .GetMethod("ReadRow", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { entityType, entity, redacted });

        return (JsonObject)JsonSerializer.SerializeToNode(row, ClinicArchiveFormat.Json)!;
    }

    private static bool RowsMatch(IEntityType entityType, JsonObject archived, object live) =>
        (bool)typeof(ClinicArchiveStore)
            .GetMethod("RowsMatch", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { entityType, archived, live })!;

    private static object Materialize(Type clrType) =>
        typeof(ClinicArchiveStore)
            .GetMethod("Materialize", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { clrType })!;

    private static void StageInsert(
        ClinicArchiveStore store, IEntityType entityType, JsonObject row, Guid clinicId) =>
        typeof(ClinicArchiveStore)
            .GetMethod("StageInsert", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(store, new object?[] { entityType, row, clinicId });

    // ------------------------------------------------------------------ AC-3

    // [AC-3] The property the whole feature rests on: a row comes back with ITS OWN id and ITS OWN dates. A domain
    // constructor would mint a fresh GUID and stamp CreatedAt from UtcNow, at which point « still present » can
    // never be recognised and every restore duplicates the practice.
    [Fact]
    public void A_Restored_Row_Keeps_Its_Own_Id_And_Its_Own_Dates()
    {
        using var db = Context();
        var entityType = db.Model.FindEntityType(typeof(Patient))!;

        var archived = ReadRow(entityType, APatient());
        StageInsert(new ClinicArchiveStore(db), entityType, archived, ClinicA);

        var entry = Assert.Single(db.ChangeTracker.Entries<Patient>());

        Assert.Equal(EntityState.Added, entry.State);
        Assert.Equal(PatientId, entry.Entity.Id);
        Assert.Equal("Béchir", entry.Entity.FirstName);
        // Not today: the ctor's `CreatedAt = DateTime.UtcNow` never ran.
        Assert.Equal(APatient().CreatedAt.Date, entry.Entity.CreatedAt.Date);
        Assert.NotEqual(default, entry.Entity.CreatedAt);
    }

    // [AC-3] The value objects table-split into the row travel nested inside it rather than as tables of their own,
    // and must come back readable — a patient restored with no phone number is a patient nobody can call.
    [Fact]
    public void An_Owned_Value_Object_Survives_The_Round_Trip()
    {
        using var db = Context();
        var entityType = db.Model.FindEntityType(typeof(Patient))!;

        var archived = ReadRow(entityType, APatient());
        StageInsert(new ClinicArchiveStore(db), entityType, archived, ClinicA);

        var restored = Assert.Single(db.ChangeTracker.Entries<Patient>()).Entity;

        Assert.Equal("bechir@example.tn", restored.Email?.Value);
        Assert.Equal("+21620123456", restored.PhoneNumber?.Value);
    }

    // [AC-1] The clinic is re-stamped from the caller rather than trusted from the file — belt and braces at the
    // one place a mismatch would be unrecoverable rather than refused. Both doors have already checked it.
    [Fact]
    public void The_Clinic_Id_Is_Re_Stamped_From_The_Caller_Not_The_File()
    {
        using var db = Context();
        var entityType = db.Model.FindEntityType(typeof(Patient))!;

        var archived = ReadRow(entityType, APatient());
        StageInsert(new ClinicArchiveStore(db), entityType, archived, ClinicB);

        Assert.Equal(ClinicB, Assert.Single(db.ChangeTracker.Entries<Patient>()).Entity.ClinicId);
    }

    // The concurrency token maps onto PostgreSQL's `xmin`, so it is store-generated and means nothing outside its
    // own database. Archiving one would assert another database's transaction id.
    [Fact]
    public void The_Concurrency_Token_Is_Not_Archived()
    {
        using var db = Context();

        var archived = ReadRow(db.Model.FindEntityType(typeof(Patient))!, APatient());

        Assert.False(archived.ContainsKey(nameof(Patient.Version)));
        Assert.True(archived.ContainsKey(nameof(Patient.Id)));
    }

    // [EC] The cabinet's Google connection is a long-lived third-party credential, and a redacted column is written
    // as null rather than omitted — an absent key and a cleared one must not be the same thing on the way back.
    [Fact]
    public void A_Redacted_Column_Is_Written_As_Null_Rather_Than_Omitted()
    {
        using var db = Context();
        var entityType = db.Model.FindEntityType(typeof(Clinic))!;

        var clinic = new Clinic(ClinicA, "Cabinet Ben Ali", city: "Tunis");
        clinic.SetGoogleCalendarConnection("1//refresh-token-secret", "primary");

        var archived = ReadRow(entityType, clinic, ClinicArchiveScope.Redacted[nameof(Clinic)]);

        Assert.True(archived.ContainsKey(nameof(Clinic.GoogleRefreshToken)));
        Assert.Null(archived[nameof(Clinic.GoogleRefreshToken)]);
        // The ciphertext column is redacted for the same reason plus one of its own: the archive deliberately
        // does not carry the key ring, so a restored token would read as « connecté » and decrypt to nothing.
        Assert.True(archived.ContainsKey(nameof(Clinic.GoogleRefreshTokenProtected)));
        Assert.Null(archived[nameof(Clinic.GoogleRefreshTokenProtected)]);
        Assert.Null(archived[nameof(Clinic.GoogleCalendarId)]);
        // And the rest of the row is exactly what a restored cabinet must come back with.
        Assert.Equal("Cabinet Ben Ali", archived[nameof(Clinic.Name)]!.GetValue<string>());
    }

    // ------------------------------------------------------------------ AC-2 / AC-4, the discriminator

    // [AC-2] Present and IDENTICAL is a no-op, which is what makes a second restore change nothing. Compared on the
    // serialized form so a DateTime or a decimal is compared exactly as it was written — the only comparison that
    // cannot invent a difference the archive could not have recorded.
    [Fact]
    public void An_Unchanged_Row_Matches_Its_Archived_Copy()
    {
        using var db = Context();
        var entityType = db.Model.FindEntityType(typeof(Patient))!;

        var live = APatient();

        Assert.True(RowsMatch(entityType, ReadRow(entityType, live), live));
    }

    // [AC-4] Present and DIFFERENT is the case that must never be overwritten, so the comparison has to see an
    // ordinary edit. Work done after the archive was taken surviving the archive being put back rests on this.
    [Fact]
    public void A_Row_Edited_Since_The_Archive_Does_Not_Match_It()
    {
        using var db = Context();
        var entityType = db.Model.FindEntityType(typeof(Patient))!;

        var live = APatient();
        var archived = ReadRow(entityType, live);

        live.UpdateNotes("Allergie découverte en juin", null);

        Assert.False(RowsMatch(entityType, archived, live));
    }

    // [AC-4] A change inside an OWNED value object counts too — the patient's phone number is a column of the same
    // row, so a comparison stopping at the scalars would silently overwrite a corrected number.
    [Fact]
    public void A_Change_Inside_An_Owned_Value_Object_Is_A_Difference()
    {
        using var db = Context();
        var entityType = db.Model.FindEntityType(typeof(Patient))!;

        var live = APatient();
        var archived = ReadRow(entityType, live);

        live.UpdateContact(live.Email, new PhoneNumber("+21698765432"));

        Assert.False(RowsMatch(entityType, archived, live));
    }

    // ------------------------------------------------------------------ the materialisation itself

    // The private parameterless constructor, not GetUninitializedObject: it runs the FIELD INITIALISERS, including
    // the `private readonly List<T> _lines = new()` behind every collection navigation. Left null, EF's own fix-up
    // walks them the moment the entry is marked Added and NREs from inside the change tracker, on a restore.
    [Theory]
    [InlineData(typeof(Invoice), "_lines")]
    [InlineData(typeof(Invoice), "_payments")]
    [InlineData(typeof(Patient), "_flags")]
    public void A_Materialised_Entity_Has_Its_Collection_Fields_Initialised(Type clrType, string field)
    {
        var instance = Materialize(clrType);

        var value = clrType
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(instance);

        Assert.NotNull(value);
    }

    // A value object has no accessible parameterless constructor in every case and no collections to leave null,
    // which is what the uninitialised fall-back is for. It must not throw.
    [Fact]
    public void A_Type_With_No_Reachable_Constructor_Still_Materialises()
    {
        Assert.NotNull(Materialize(typeof(Email)));
        Assert.IsType<Email>(Materialize(typeof(Email)));
    }

    // ------------------------------------------------------------------ what an archive must carry

    // The derived guard for the defect that made a voided payment restore as live money. EF marks EVERY property
    // configured with `HasDefaultValue(...)` as `ValueGenerated.OnAdd`, so a predicate excluding `OnAdd` dropped
    // `Payment.IsVoided`, `Patient.IsArchived`, the clinic's billing settings and every devis's act ordering —
    // neither archived, nor restored, nor compared, with the DATABASE's default winning in silence.
    //
    // Derived over the whole model rather than listing today's eleven configurations, so the twelfth is covered
    // on the day it is written.
    [Fact]
    public void Every_Mapped_Column_With_A_Database_Default_Is_Archived()
    {
        using var db = Context();

        var missed = new List<string>();

        foreach (var table in ClinicArchiveScope.Resolve(db.Model).Tables)
        {
            var archived = ArchivedPropertyNames(table.EntityType);

            missed.AddRange(table.EntityType.GetProperties()
                .Where(p => !p.IsShadowProperty() && !p.IsConcurrencyToken && p.GetDefaultValue() is not null)
                .Where(p => !archived.Contains(p.Name))
                .Select(p => $"{table.Name}.{p.Name}"));
        }

        Assert.Empty(missed);
    }

    // Same root cause, on the PRIMARY KEY, where it is a silent total loss. There is no global
    // `ValueGeneratedNever` convention, so EF's own convention gives a single `Guid` key `ValueGenerated.OnAdd` —
    // and three archived configurations declare none, which left every ordonnance, certificat, antécédent médical
    // and antécédent familial with no key in the file: unrestorable, while the manifest declared their row counts
    // and the screen reported success.
    [Fact]
    public void Every_Planned_Tables_Key_Is_Archived()
    {
        using var db = Context();

        var missed = ClinicArchiveScope.Resolve(db.Model).Tables
            .Where(table => table.EntityType.FindPrimaryKey() is { Properties.Count: 1 } key
                            && key.Properties[0].ClrType == typeof(Guid)
                            && !ArchivedPropertyNames(table.EntityType).Contains(key.Properties[0].Name))
            .Select(table => table.Name)
            .ToList();

        Assert.Empty(missed);
    }

    // Archiving the column is necessary and not sufficient. EF omits a store-generated column whose value equals
    // the property's SENTINEL, and `HasDefaultValue(...)` makes a column store-generated — so with the sentinel
    // left at the CLR default, an archived `false` on a column defaulting to `true` reached the database as
    // `true`: a deactivated acte restored active, a clinic's VAT switched back on, silently and in the direction
    // that looks healthy.
    //
    // Derived over the model rather than over four types someone remembered: the invariant is that a value the
    // database would otherwise substitute is never the sentinel.
    [Fact]
    public void The_Sentinel_Of_Every_Defaulted_Column_Is_Its_Database_Default()
    {
        using var db = Context();

        var wrong = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties().Select(p => (Entity: e.ClrType.Name, Property: p)))
            .Where(x => !x.Property.IsConcurrencyToken && x.Property.GetDefaultValue() is not null)
            .Where(x => !Equals(x.Property.Sentinel, x.Property.GetDefaultValue()))
            .Select(x => $"{x.Entity}.{x.Property.Name}")
            .ToList();

        Assert.NotEmpty(db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => !p.IsConcurrencyToken && p.GetDefaultValue() is not null));
        Assert.Empty(wrong);
    }

    // And end to end through the restore: an acte archived as deactivated is one EF actually sends, rather than
    // one it leaves to a column that says « true ».
    [Fact]
    public void A_Deactivated_Row_Is_Not_Left_To_The_Columns_Own_Default()
    {
        using var db = Context();
        var entityType = db.Model.FindEntityType(typeof(ProcedureType))!;
        var isActive = entityType.FindProperty(nameof(ProcedureType.IsActive))!;

        Assert.Equal(true, isActive.GetDefaultValue());

        var row = new JsonObject
        {
            [nameof(ProcedureType.Id)] = Guid.NewGuid().ToString(),
            [nameof(ProcedureType.IsActive)] = false,
        };

        StageInsert(new ClinicArchiveStore(db), entityType, row, ClinicA);

        var entry = Assert.Single(db.ChangeTracker.Entries());

        Assert.Equal(false, entry.Property(nameof(ProcedureType.IsActive)).CurrentValue);
        // EF's own verdict, not that we set a flag: « store-generated » is what decides whether the column
        // appears in the INSERT at all.
        Assert.False(IsStoreGenerated(entry, isActive));
    }

    /// <summary>
    /// EF's own answer to « will this column be left to the database? ». Reached through the infrastructure entry
    /// because nothing public exposes it, and because a test asserting only that <c>IsModified</c> was set would
    /// assert our own call rather than the behaviour it is made for.
    /// </summary>
    private static bool IsStoreGenerated(EntityEntry entry, IProperty property)
    {
        var internalEntry = entry.GetType()
            .GetProperty("InternalEntry", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(entry)
            ?? ((IInfrastructure<object>)entry).GetInfrastructure();

        var method = internalEntry.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == "IsStoreGenerated" && m.GetParameters().Length == 1);

        return (bool)method.Invoke(internalEntry, new object?[] { property })!;
    }

    private static HashSet<string> ArchivedPropertyNames(IEntityType entityType)
    {
        var properties = (IEnumerable<IProperty>)typeof(ClinicArchiveStore)
            .GetMethod("ArchivedProperties", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { entityType })!;

        return properties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }
}

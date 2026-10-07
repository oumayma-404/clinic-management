using System.Text.Json.Nodes;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Documents;

/// <summary>
/// fix-document-cnam-accuracy #6: editing a document must not strip the issuing practitioner's cachet +
/// CNOMDT ordre. The re-apply merges the caller's live doctor identity (when they have one) with the
/// values already snapshotted on the stored document (when the editor is a secretary/admin with no doctor),
/// while always stripping any client-supplied reserved keys.
/// </summary>
public class PractitionerRenderSnapshotTests
{
    private const string StoredJson =
        "{\"foo\":\"bar\"," +
        "\"clinicCity\":\"Tunis\"," +
        "\"doctorOrdreNumber\":\"12345\"," +
        "\"doctorCachetKey\":\"cachet/stored\"," +
        "\"doctorCachetContentType\":\"image/png\"}";

    // [AC-3] ReadFrom pulls the four reserved values already snapshotted on a document.
    [Fact]
    public void ReadFrom_Reads_The_Reserved_Keys()
    {
        var snap = PractitionerRenderSnapshot.ReadFrom(StoredJson);

        Assert.Equal("Tunis", snap.ClinicCity);
        Assert.Equal("12345", snap.DoctorOrdreNumber);
        Assert.Equal("cachet/stored", snap.DoctorCachetKey);
        Assert.Equal("image/png", snap.DoctorCachetContentType);
    }

    [Fact]
    public void ReadFrom_Malformed_Json_Yields_Empty()
    {
        Assert.False(PractitionerRenderSnapshot.ReadFrom("not json").HasAny);
        Assert.False(PractitionerRenderSnapshot.ReadFrom("[]").HasAny); // non-object
    }

    // [AC-3] A secretary/admin edit (caller snapshot has the clinic city but no doctor values) preserves the
    // document's stored cachet + ordre via the per-field fallback.
    [Fact]
    public void OrElse_Preserves_Stored_Doctor_Identity_When_Caller_Has_None()
    {
        var caller = new PractitionerRenderSnapshot { ClinicCity = "Tunis" }; // no doctor record
        var stored = PractitionerRenderSnapshot.ReadFrom(StoredJson);

        var effective = caller.OrElse(stored);

        Assert.Equal("Tunis", effective.ClinicCity);
        Assert.Equal("12345", effective.DoctorOrdreNumber);
        Assert.Equal("cachet/stored", effective.DoctorCachetKey);
        Assert.Equal("image/png", effective.DoctorCachetContentType);
    }

    // [AC-3] A doctor editing their own document refreshes to their live cachet + ordre (caller values win).
    [Fact]
    public void OrElse_Prefers_Live_Caller_Doctor_Identity()
    {
        var caller = new PractitionerRenderSnapshot
        {
            ClinicCity = "Sfax",
            DoctorOrdreNumber = "99999",
            DoctorCachetKey = "cachet/live",
            DoctorCachetContentType = "image/jpeg",
        };
        var stored = PractitionerRenderSnapshot.ReadFrom(StoredJson);

        var effective = caller.OrElse(stored);

        Assert.Equal("Sfax", effective.ClinicCity);
        Assert.Equal("99999", effective.DoctorOrdreNumber);
        Assert.Equal("cachet/live", effective.DoctorCachetKey);
        Assert.Equal("image/jpeg", effective.DoctorCachetContentType);
    }

    // [AC-3] End-to-end for the secretary case: the editor rebuilds ContentJson without the reserved keys
    // (and a client-supplied doctorCachetKey must be ignored), yet the re-rendered doc keeps the stored
    // practitioner identity.
    [Fact]
    public void ApplyTo_Secretary_Edit_Preserves_Cachet_And_Strips_Client_Supplied_Key()
    {
        var stored = PractitionerRenderSnapshot.ReadFrom(StoredJson);
        var effective = new PractitionerRenderSnapshot { ClinicCity = "Tunis" }.OrElse(stored);

        // The structured editor's payload: no reserved keys except a spoofed cachet key a caller shouldn't set.
        var editorJson = "{\"objet\":\"Certificat\",\"doctorCachetKey\":\"cachet/HACKED\"}";

        var result = JsonNode.Parse(effective.ApplyTo(editorJson))!.AsObject();

        Assert.Equal("Certificat", (string?)result["objet"]);
        Assert.Equal("cachet/stored", (string?)result["doctorCachetKey"]); // stored value, NOT the spoofed one
        Assert.Equal("image/png", (string?)result["doctorCachetContentType"]);
        Assert.Equal("12345", (string?)result["doctorOrdreNumber"]);
        Assert.Equal("Tunis", (string?)result["clinicCity"]);
    }

    // ── The cabinet's letterhead: reserved like the cachet, and its two bands travel as one ────────────────────

    [Fact]
    public void ApplyTo_Writes_The_Servers_Letterhead_Over_A_Forged_One()
    {
        var snapshot = new PractitionerRenderSnapshot
        {
            LetterheadHeaderKey = "clinics/c/letterhead/a/header",
            LetterheadFooterKey = "clinics/c/letterhead/a/footer"
        };

        var result = JsonNode.Parse(snapshot.ApplyTo(
            "{\"letterheadHeaderKey\":\"clinics/OTHER/header\",\"letterheadFooterKey\":\"clinics/OTHER/footer\"}"))!.AsObject();

        Assert.Equal("clinics/c/letterhead/a/header", (string?)result["letterheadHeaderKey"]);
        Assert.Equal("clinics/c/letterhead/a/footer", (string?)result["letterheadFooterKey"]);
    }

    [Fact]
    public void ApplyTo_Strips_A_Client_Supplied_Letterhead_When_The_Cabinet_Has_None()
    {
        var result = JsonNode.Parse(new PractitionerRenderSnapshot { ClinicCity = "Tunis" }.ApplyTo(
            "{\"objet\":\"x\",\"letterheadHeaderKey\":\"clinics/OTHER/header\",\"letterheadFooterKey\":\"clinics/OTHER/footer\"}"))!.AsObject();

        Assert.False(result.ContainsKey("letterheadHeaderKey"));
        Assert.False(result.ContainsKey("letterheadFooterKey"));
        Assert.Equal("x", (string?)result["objet"]);
    }

    [Fact]
    public void ApplyTo_Writes_The_Page_Body_With_Its_Header_And_Strips_A_Forged_One()
    {
        var withBody = new PractitionerRenderSnapshot
        {
            LetterheadHeaderKey = "clinics/c/letterhead/a/header",
            LetterheadBodyKey = "clinics/c/letterhead/a/body"
        };
        const string forged = "{\"letterheadBodyKey\":\"clinics/OTHER/body\"}";

        var written = JsonNode.Parse(withBody.ApplyTo(forged))!.AsObject();
        var stripped = JsonNode.Parse(new PractitionerRenderSnapshot().ApplyTo(forged))!.AsObject();
        var orphan = JsonNode.Parse(
            new PractitionerRenderSnapshot { LetterheadBodyKey = "clinics/c/letterhead/a/body" }.ApplyTo("{}"))!.AsObject();

        Assert.Equal("clinics/c/letterhead/a/body", (string?)written["letterheadBodyKey"]);
        Assert.False(stripped.ContainsKey("letterheadBodyKey"));
        Assert.False(orphan.ContainsKey("letterheadBodyKey"));
    }

    [Fact]
    public void OrElse_Never_Borrows_A_Page_Body_From_Another_Upload()
    {
        var live = new PractitionerRenderSnapshot { LetterheadHeaderKey = "clinics/c/letterhead/b/header" };
        var stored = new PractitionerRenderSnapshot
        {
            LetterheadHeaderKey = "clinics/c/letterhead/a/header",
            LetterheadBodyKey = "clinics/c/letterhead/a/body"
        };

        Assert.Null(live.OrElse(stored).LetterheadBodyKey);
        Assert.Equal("clinics/c/letterhead/a/body", new PractitionerRenderSnapshot().OrElse(stored).LetterheadBodyKey);
    }

    [Fact]
    public void ApplyTo_Never_Writes_A_Footer_Without_Its_Header()
    {
        var result = JsonNode.Parse(
            new PractitionerRenderSnapshot { LetterheadFooterKey = "clinics/c/letterhead/a/footer" }.ApplyTo("{}"))!.AsObject();

        Assert.False(result.ContainsKey("letterheadFooterKey"));
    }

    [Fact]
    public void ReadFrom_Reads_The_Letterhead_A_Document_Was_Issued_With()
    {
        var snap = PractitionerRenderSnapshot.ReadFrom(
            "{\"letterheadHeaderKey\":\"clinics/c/letterhead/a/header\",\"letterheadFooterKey\":\"clinics/c/letterhead/a/footer\"}");

        Assert.True(snap.HasAny);
        Assert.Equal("clinics/c/letterhead/a/header", snap.LetterheadHeaderKey);
        Assert.Equal("clinics/c/letterhead/a/footer", snap.LetterheadFooterKey);
    }

    [Fact]
    public void OrElse_Never_Pairs_A_Header_With_Another_Uploads_Footer()
    {
        var live = new PractitionerRenderSnapshot { LetterheadHeaderKey = "clinics/c/letterhead/b/header" };
        var stored = new PractitionerRenderSnapshot
        {
            LetterheadHeaderKey = "clinics/c/letterhead/a/header",
            LetterheadFooterKey = "clinics/c/letterhead/a/footer"
        };

        var effective = live.OrElse(stored);

        Assert.Equal("clinics/c/letterhead/b/header", effective.LetterheadHeaderKey);
        Assert.Null(effective.LetterheadFooterKey);
    }

    [Fact]
    public void OrElse_Keeps_The_Stored_Letterhead_When_The_Cabinet_Has_Since_Withdrawn_It()
    {
        var stored = new PractitionerRenderSnapshot
        {
            LetterheadHeaderKey = "clinics/c/letterhead/a/header",
            LetterheadFooterKey = "clinics/c/letterhead/a/footer"
        };

        var effective = new PractitionerRenderSnapshot { ClinicCity = "Tunis" }.OrElse(stored);

        Assert.Equal("clinics/c/letterhead/a/header", effective.LetterheadHeaderKey);
        Assert.Equal("clinics/c/letterhead/a/footer", effective.LetterheadFooterKey);
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────
    // ResolveAsync: whose cachet a document carries.
    //
    // `MedicalDocumentsController` is `AnyClinicRole`, so the routine author of an ordonnance is reception — who
    // has no `Doctor` record. Resolving from the caller therefore produced a document with **no** cachet and no
    // n° d'ordre, silently, on the one class of document whose purpose is to carry them. Resolution is now
    // chosen-practitioner → caller → none, and the chosen id is tenant-checked before it is believed.
    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // The named practitioner wins over the caller's own record. This is the case the feature exists for: a
    // secretary (or an admin who is not a dentist) types the prescription, and it carries the prescriber.
    [Fact]
    public async Task ResolveAsync_Prefers_The_Chosen_Practitioner_Over_The_Caller()
    {
        var chosen = DoctorWithIdentity(ClinicId, "chosen", "cachet/chosen");
        var caller = DoctorWithIdentity(ClinicId, "caller", "cachet/caller");

        var snap = await Resolve(chosen.Id, chosen, caller);

        Assert.Equal("chosen", snap.DoctorOrdreNumber);
        Assert.Equal("cachet/chosen", snap.DoctorCachetKey);
    }

    // No practitioner named: the caller's own record, which is the single-dentist cabinet and stays correct.
    [Fact]
    public async Task ResolveAsync_Falls_Back_To_The_Caller_When_None_Is_Chosen()
    {
        var caller = DoctorWithIdentity(ClinicId, "caller", "cachet/caller");

        var snap = await Resolve(issuingDoctorId: null, chosen: null, caller: caller);

        Assert.Equal("caller", snap.DoctorOrdreNumber);
        Assert.Equal("cachet/caller", snap.DoctorCachetKey);
    }

    // ⚠️ The security case. A crafted id naming another practice's practitioner must not resolve — it would stamp
    // that practitioner's cachet onto this clinic's document, and the unauthenticated PdfGenerationJob would later
    // dereference the foreign storage key. It **falls through** to the caller rather than yielding nothing, the
    // same shape as PractitionerAttribution.Resolve.
    [Fact]
    public async Task ResolveAsync_Rejects_A_Doctor_From_Another_Clinic_And_Falls_Through()
    {
        var foreign = DoctorWithIdentity(OtherClinicId, "foreign", "cachet/foreign");
        var caller = DoctorWithIdentity(ClinicId, "caller", "cachet/caller");

        var snap = await Resolve(foreign.Id, foreign, caller);

        Assert.Equal("caller", snap.DoctorOrdreNumber);
        Assert.Equal("cachet/caller", snap.DoctorCachetKey);
        Assert.DoesNotContain("foreign", snap.DoctorCachetKey);
    }

    // An id that no longer exists is the stale-bookmark case: fall through, never fail the render.
    [Fact]
    public async Task ResolveAsync_Falls_Through_When_The_Chosen_Doctor_Is_Gone()
    {
        var caller = DoctorWithIdentity(ClinicId, "caller", "cachet/caller");

        var snap = await Resolve(Guid.NewGuid(), chosen: null, caller: caller);

        Assert.Equal("caller", snap.DoctorOrdreNumber);
    }

    // Guid.Empty means "not supplied" — the shape a form posts for an unset select. Treated as absent, and it must
    // not cost a repository round trip pretending to look it up.
    [Fact]
    public async Task ResolveAsync_Treats_An_Empty_Guid_As_Absent()
    {
        var caller = DoctorWithIdentity(ClinicId, "caller", "cachet/caller");
        var doctors = new Mock<IDoctorRepository>();
        doctors.Setup(r => r.GetByUserIdAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(caller);

        var snap = await PractitionerRenderSnapshot.ResolveAsync(
            Guid.Empty, "user-1", ClinicId, doctors.Object, ClinicRepo().Object, CancellationToken.None);

        Assert.Equal("caller", snap.DoctorOrdreNumber);
        doctors.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Nobody resolvable at all: empty doctor fields, never a throw. The renderer prints the document without a
    // cachet, which is the honest outcome — and on the update path OrElse then keeps whatever the document already
    // held, so an edit by reception cannot blank an existing practitioner's identity.
    [Fact]
    public async Task ResolveAsync_Yields_Empty_Doctor_Fields_When_Nobody_Resolves()
    {
        var snap = await Resolve(issuingDoctorId: null, chosen: null, caller: null);

        Assert.Null(snap.DoctorOrdreNumber);
        Assert.Null(snap.DoctorCachetKey);
        Assert.Equal("Tunis", snap.ClinicCity); // the cabinet still resolves
    }

    private static async Task<PractitionerRenderSnapshot> Resolve(
        Guid? issuingDoctorId, Doctor? chosen, Doctor? caller)
    {
        var doctors = new Mock<IDoctorRepository>();

        if (issuingDoctorId is { } id && id != Guid.Empty)
        {
            doctors.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(chosen);
        }

        doctors.Setup(r => r.GetByUserIdAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(caller);

        return await PractitionerRenderSnapshot.ResolveAsync(
            issuingDoctorId, "user-1", ClinicId, doctors.Object, ClinicRepo().Object, CancellationToken.None);
    }

    private static Mock<IClinicRepository> ClinicRepo()
    {
        // Named arguments: `address`, `phone`, `email`, `code` and `city` are five adjacent optional strings.
        var clinic = new Clinic(
            ClinicId,
            name: "Cabinet",
            address: "1 rue de Rome",
            phone: "71000000",
            email: "cabinet@example.com",
            city: "Tunis");
        var repo = new Mock<IClinicRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        return repo;
    }

    private static Doctor DoctorWithIdentity(Guid clinicId, string ordre, string cachetKey)
    {
        var doctor = new Doctor(Guid.NewGuid(), clinicId, "Amine", "Ben Salah", "Dentist");
        doctor.SetOrdreNumber(ordre);
        doctor.SetCachet(cachetKey, "image/png");
        return doctor;
    }
}

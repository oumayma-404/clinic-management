using System.Text.Json.Nodes;
using ClinicManagement.Application.Features.Documents;
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
}

using System.Text.Json;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Domain.ValueObjects;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Documents;

/// <summary>
/// The <c>bulletin-cnam</c> mandatory-field gate (<c>adoption-qa-k</c> K2, plus K7's identifiant length).
/// </summary>
/// <remarks>
/// <para>
/// Before this, the document handlers validated exactly two things — « honoraires is retired » and « a liaison
/// needs a recipient ». A bulletin was saveable with no identifiant, no régime, no lien, no act and no code
/// conventionnel, and <b>every one of those degraded silently by design</b>: the renderer's <c>DrawLeft</c> returns
/// on a blank string and its régime/lien <c>switch</c>es tick nothing for a value they do not recognise. That is
/// correct behaviour for a renderer, which is why the check belongs at the write.
/// </para>
/// <para>
/// ⚠️ The highest-value case in this file is <see cref="Every_Regime_And_Lien_Value_Is_Accepted"/>. The régime and
/// lien values are French strings matched by an <c>==</c> in the renderer's two <c>switch</c>es, so a mismatch in
/// casing or in a single accent — « Convention bilatérale » carries one — printed an <b>empty</b> régime box while
/// every layer reported success. Nothing else in the suite can fail on that: it is a silent no-op, not an
/// exception. Pairs with <c>CnamClosedSetContractTests</c>, which pins the browser's copy of the same sets.
/// </para>
/// </remarks>
public class BulletinMandatoryFieldsTests
{
    // A bulletin that passes every check — each test below removes or corrupts exactly one field, so a failure
    // names the field rather than "something in the payload".
    private static Dictionary<string, string> ValidContent() => new()
    {
        ["identifiantUnique"] = "1234567890",
        ["regime"] = CnamInfo.RegimeCnss,
        ["maladeLien"] = CnamInfo.LienAssureLuiMeme,
        ["acts"] = "[{\"date\":\"2026-07-20\",\"codeActe\":\"DCH020030\",\"honoraires\":\"30\"}]",
        ["doctorCodeProfessionnel"] = "PS-001",
    };

    private static string Json(Dictionary<string, string> content) => JsonSerializer.Serialize(content);

    private static string? Validate(Dictionary<string, string> content) =>
        BulletinCnamValidation.Validate(Json(content));

    private static string? ValidateWithout(string key)
    {
        var content = ValidContent();
        content.Remove(key);
        return Validate(content);
    }

    // ===================== The happy path =====================

    [Fact] // [K2] A complete bulletin is accepted.
    public void A_Complete_Bulletin_Is_Accepted()
    {
        Assert.Null(Validate(ValidContent()));
    }

    // ===================== One case per mandatory field =====================

    [Theory] // [K2] Each mandatory field, absent, is refused with a message naming it.
    [InlineData("identifiantUnique", "identifiant")]
    [InlineData("regime", "régime")]
    [InlineData("maladeLien", "lien de parenté")]
    [InlineData("acts", "acte")]
    [InlineData("doctorCodeProfessionnel", "code conventionnel")]
    public void A_Missing_Mandatory_Field_Is_Refused_By_Name(string key, string expectedFragment)
    {
        var problem = ValidateWithout(key);

        Assert.NotNull(problem);
        Assert.Contains(expectedFragment, problem, StringComparison.OrdinalIgnoreCase);
    }

    [Theory] // [K2] A blank value is refused exactly like an absent key — whitespace is not a value.
    [InlineData("identifiantUnique")]
    [InlineData("regime")]
    [InlineData("maladeLien")]
    [InlineData("doctorCodeProfessionnel")]
    public void A_Blank_Mandatory_Field_Is_Refused(string key)
    {
        var content = ValidContent();
        content[key] = "   ";

        Assert.NotNull(Validate(content));
    }

    [Fact] // [K2] Every problem is reported at once, not one refusal at a time.
    public void All_Problems_Are_Reported_In_One_Message()
    {
        // A dentist filling a CNAM form should not discover the five mandatory fields across five saves. The
        // editor shows the same list before Save is reachable; this is the backstop.
        var problem = BulletinCnamValidation.Validate("{}");

        Assert.NotNull(problem);
        Assert.Contains("identifiant", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("régime", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lien de parenté", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("acte", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("code conventionnel", problem, StringComparison.OrdinalIgnoreCase);
    }

    // ===================== The closed sets, byte for byte =====================

    [Fact] // [K2] Every régime and lien the form offers is accepted, exactly as written.
    public void Every_Regime_And_Lien_Value_Is_Accepted()
    {
        foreach (var regime in CnamInfo.AllowedRegimes)
        {
            var content = ValidContent();
            content["regime"] = regime;
            Assert.Null(Validate(content));
        }

        foreach (var lien in CnamInfo.AllowedLiens)
        {
            var content = ValidContent();
            content["maladeLien"] = lien;
            // « Enfant » / « Ascendant » also need a rang — supply one so this case tests membership alone.
            if (CnamInfo.LienRequiresRang(lien))
            {
                content["maladeLienRang"] = "1";
            }
            Assert.Null(Validate(content));
        }
    }

    [Theory] // [K2] A near-miss is refused rather than silently ticking nothing — the defect this closes.
    [InlineData("cnss")]                    // casing
    [InlineData("CNSS ")]                   // the value is trimmed, but a trailing-space-only variant of a
                                            //   *different* spelling must still fail — see the next cases
    [InlineData("Convention bilaterale")]   // ⚠️ the accent — the exact silent failure
    [InlineData("convention bilatérale")]   // casing on the accented form
    [InlineData("CNAM")]                    // a plausible but wrong régime
    public void A_Near_Miss_Regime_Is_Refused(string regime)
    {
        var content = ValidContent();
        content["regime"] = regime;

        var problem = Validate(content);

        // "CNSS " trims to the valid "CNSS", so that one case is legitimately accepted; every other spelling is a
        // refusal naming the régime. Asserted this way round so the trimming behaviour stays deliberate.
        if (regime.Trim() == CnamInfo.RegimeCnss)
        {
            Assert.Null(problem);
        }
        else
        {
            Assert.NotNull(problem);
            Assert.Contains("régime", problem, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory] // [K2] Same for the lien.
    [InlineData("assuré lui-même")]
    [InlineData("Assure lui-meme")]  // no accents
    [InlineData("Époux")]            // a plausible but wrong lien
    public void A_Near_Miss_Lien_Is_Refused(string lien)
    {
        var content = ValidContent();
        content["maladeLien"] = lien;

        var problem = Validate(content);

        Assert.NotNull(problem);
        Assert.Contains("lien de parenté", problem, StringComparison.OrdinalIgnoreCase);
    }

    // ===================== Rang, where the lien requires one =====================

    [Theory] // [K2] « Enfant » and « Ascendant » identify a person by their rang, so it is mandatory.
    [InlineData(CnamInfo.LienEnfant)]
    [InlineData(CnamInfo.LienAscendant)]
    public void A_Lien_Requiring_A_Rang_Is_Refused_Without_One(string lien)
    {
        var content = ValidContent();
        content["maladeLien"] = lien;

        var problem = Validate(content);

        Assert.NotNull(problem);
        Assert.Contains("rang", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Theory] // [K2] The other two liens name exactly one person — demanding a rang would ask for a non-value.
    [InlineData(CnamInfo.LienAssureLuiMeme)]
    [InlineData(CnamInfo.LienConjoint)]
    public void A_Lien_Naming_One_Person_Needs_No_Rang(string lien)
    {
        var content = ValidContent();
        content["maladeLien"] = lien;

        Assert.Null(Validate(content));
    }

    // ===================== Acts =====================

    [Theory] // [K2] An empty or unreadable acts payload counts as no acts, and is refused.
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"date\":\"2026-07-20\"}")] // an object, not an array
    [InlineData("")]
    public void A_Bulletin_With_No_Readable_Acts_Is_Refused(string actsJson)
    {
        // A malformed payload is refused rather than saved as an act-less form: the renderer already treats it as
        // zero acts, so accepting it here would produce a bulletin claiming no care was given.
        var content = ValidContent();
        content["acts"] = actsJson;

        var problem = Validate(content);

        Assert.NotNull(problem);
        Assert.Contains("acte", problem, StringComparison.OrdinalIgnoreCase);
    }

    // ===================== K7 — the identifiant fits the printed comb =====================

    [Theory] // [K7] Up to the number of printed cells is accepted; separators are not digits.
    [InlineData("1234567890")]        // exactly the 10 cells
    [InlineData("12345")]             // shorter is fine — the comb is left-aligned
    [InlineData("12 34 56 78 90")]    // spaces a free-text field collects
    [InlineData("1234-5678-90")]
    public void An_Identifiant_That_Fits_The_Comb_Is_Accepted(string identifiant)
    {
        var content = ValidContent();
        content["identifiantUnique"] = identifiant;

        Assert.Null(Validate(content));
    }

    [Theory] // [K7] More digits than cells is refused — the renderer used to drop the tail without a trace.
    [InlineData("12345678901")]       // one too many
    [InlineData("12345678901234")]
    [InlineData("12 34 56 78 90 12")] // 12 digits once the spaces are ignored
    public void An_Over_Length_Identifiant_Is_Refused(string identifiant)
    {
        var content = ValidContent();
        content["identifiantUnique"] = identifiant;

        var problem = Validate(content);

        Assert.NotNull(problem);
        // The message states both numbers, because « corrigez l'identifiant » with no target is unactionable.
        Assert.Contains(CnamInfo.CountIdentifiantDigits(identifiant).ToString(), problem);
        Assert.Contains(CnamInfo.IdentifiantUniqueDigits.ToString(), problem);
    }

    // ===================== Payload robustness =====================

    [Theory] // [K2] Unreadable content is its own message — not five field refusals about nothing.
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")] // valid JSON, but an array where an object is expected
    public void Unreadable_Content_Is_Refused_As_Such(string contentJson)
    {
        var problem = BulletinCnamValidation.Validate(contentJson);

        Assert.NotNull(problem);
        Assert.Contains("illisible", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Theory] // [K2] An empty payload is a bulletin with nothing filled in, NOT an unreadable one.
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Content_Reports_The_Missing_Fields_Rather_Than_Illisible(string? contentJson)
    {
        var problem = BulletinCnamValidation.Validate(contentJson);

        Assert.NotNull(problem);
        Assert.DoesNotContain("illisible", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("identifiant", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // [K2] A non-string JSON value does not crash the gate.
    public void A_Non_String_Json_Value_Is_Tolerated()
    {
        // The editor writes strings, but a hand-edited or older payload may not — and a validation gate that
        // throws on a number would turn « incomplete » into a 500.
        var problem = BulletinCnamValidation.Validate(
            "{\"identifiantUnique\":1234567890,\"regime\":\"CNSS\",\"maladeLien\":\"Conjoint\"," +
            "\"acts\":\"[{\\\"date\\\":\\\"2026-07-20\\\"}]\",\"doctorCodeProfessionnel\":\"PS-001\"}");

        Assert.Null(problem);
    }
}

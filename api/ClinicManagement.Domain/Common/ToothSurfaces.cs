namespace ClinicManagement.Domain.Common;

/// <summary>
/// The five faces of a tooth — <b>M</b>ésiale, <b>O</b>cclusale, <b>D</b>istale, <b>V</b>estibulaire,
/// <b>L</b>inguale — and the one rule that reads them.
///
/// <para>
/// ⚠️ <b><see cref="Normalize"/> was written twice, byte for byte</b>, once in <c>ToothState</c> and once in
/// <c>DentalRecordAct</c>. Both are entry points for the same string from the same picker, so a sixth letter or
/// a reworded refusal had two places to reach and no test comparing them. It lives here now, beside
/// <see cref="FdiTooth"/>, for the reason that one does.
/// </para>
///
/// <para>
/// ⚠️ <b>Palatine and linguale are the same letter</b>, deliberately: <c>L</c>. The picker offers five faces
/// and the column is <c>varchar(5)</c>, so a sixth code would be a schema change and a mirror change on the
/// client. Recorded here so the collapse is a known simplification rather than a discovery.
/// </para>
/// </summary>
public static class ToothSurfaces
{
    /// <summary>The only accepted letters, in the order the picker offers them.</summary>
    public const string Alphabet = "MODVL";

    /// <summary>The French refusal for a letter outside <see cref="Alphabet"/>, stated once.</summary>
    public static string Refuse(char surface) =>
        $"Surface invalide : '{surface}'. Valeurs autorisées : M, O, D, V, L.";

    /// <summary>
    /// Trim, upper-case and validate a stored surface string; null for "no faces recorded".
    /// </summary>
    /// <exception cref="ArgumentException">A letter outside <see cref="Alphabet"/>.</exception>
    public static string? Normalize(string? surfaces, string paramName)
    {
        if (string.IsNullOrWhiteSpace(surfaces))
        {
            return null;
        }

        var normalized = surfaces.Trim().ToUpperInvariant();
        foreach (var c in normalized)
        {
            if (Alphabet.IndexOf(c) < 0)
            {
                throw new ArgumentException(Refuse(c), paramName);
            }
        }

        return normalized;
    }

    /// <summary>The distinct faces in a stored string. Empty for null — "no faces recorded", not "no faces".</summary>
    public static IReadOnlySet<char> Parse(string? surfaces) =>
        string.IsNullOrWhiteSpace(surfaces)
            ? new HashSet<char>()
            : surfaces.Trim().ToUpperInvariant().Where(c => Alphabet.IndexOf(c) >= 0).ToHashSet();

    /// <summary>
    /// Does work covering <paramref name="treated"/> answer a diagnosis recorded on <paramref name="diagnosed"/>?
    ///
    /// <para>
    /// ⚠️ <b>This is the rule that stopped soigner la mésiale from deleting the diagnosis on the occlusale.</b>
    /// <c>DentalRecordLinker</c> closed every open diagnosis on any treated tooth, comparing tooth numbers and
    /// nothing else — a hard delete, no tombstone — so a carie MOD half restored lost the record of the two
    /// faces still to do. It never failed anywhere: the chart simply stopped saying the work was needed.
    /// </para>
    ///
    /// <para>
    /// The test is deliberately <b>conservative</b>, and only withholds closure in the one case that is
    /// unambiguously wrong. An <b>unfaced treatment</b> (an extraction, a couronne) resolves anything on that
    /// tooth. An <b>unfaced diagnosis</b> named no place, so faced work on the tooth is taken to answer it —
    /// which keeps today's behaviour for « À traiter » and « Fracture », and keeps « N dents à traiter » from
    /// inflating with diagnoses nothing can ever close. Closure is withheld only when the diagnosis names a
    /// face the work did not reach.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <paramref name="treated"/> is the <b>union across the whole séance</b>, never one act: a visit filling
    /// M with one act and O with another has together answered a carie MO, and per-act comparison would leave it
    /// open with both faces restored.
    /// </para>
    /// </summary>
    public static bool Resolves(IReadOnlySet<char> treated, IReadOnlySet<char> diagnosed) =>
        treated.Count == 0 || diagnosed.Count == 0 || diagnosed.IsSubsetOf(treated);
}

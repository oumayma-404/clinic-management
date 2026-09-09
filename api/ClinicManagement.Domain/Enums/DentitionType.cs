namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Which set of teeth a patient is charted on — a property of the <see cref="Entities.Patient"/>, asked once.
///
/// <para>
/// It used to be asked three times: a toggle on the odontogram, another in the fiche de soins editor, and a per-fiche
/// <c>IsAdultTeeth</c> flag shown as a badge in the actes dentaires list. All three answered the same question about the
/// same patient, and nothing kept them agreeing.
/// </para>
///
/// <para>
/// ⚠️ <b>Three values, and the third is a reversal.</b> This enum shipped with two on the argument that the escape
/// hatch — the field being editable at any time — made a <c>Mixed</c> value unnecessary. It did not: real dentition
/// passes through a mixed stage, a seven-year-old carries deciduous and permanent teeth at once, and with two values
/// a patient marked <see cref="Child"/> could not be charted on a permanent molar until their record was switched to
/// <see cref="Adult"/>, at which point the remaining baby teeth became unchartable. The odontogram had already grown a
/// third *view* (`DentitionView.mixed`) to render that mouth; what was missing was a way for the patient record to
/// say so. <see cref="Mixed"/> is that.
/// </para>
///
/// <para>
/// ⚠️ <b>The French labels do not name ages, they name dentitions</b> — « denture temporaire », « denture mixte »,
/// « denture définitive ». « Adulte » / « Enfant » is gone from the interface: it described the patient rather than
/// the mouth, and there is no third patient. The <b>stored</b> names stay <see cref="Child"/> / <see cref="Adult"/>
/// (English keys, French display, the standing convention for a persisted closed set) so no existing row moves.
/// </para>
///
/// <para>
/// ⚠️ <see cref="Mixed"/> is <b>2</b>, appended rather than inserted: the values are persisted through
/// <c>HasConversion&lt;int&gt;()</c>, so renumbering would silently repoint every stored row.
/// </para>
///
/// <para>
/// Note that <c>FdiTooth</c>/<c>tooth-multiselect</c> classify each tooth by its own FDI range, so a *stored* record
/// holding both dentitions keeps rendering correctly — this enum governs what can be charted next, never how history
/// is read back.
/// </para>
/// </summary>
public enum DentitionType
{
    /// <summary>Deciduous / « denture temporaire » — FDI quadrants 5–8. Assumed under 6 years old.</summary>
    Child = 0,

    /// <summary>Permanent / « denture définitive » — FDI quadrants 1–4. Assumed from 13 years old.</summary>
    Adult = 1,

    /// <summary>
    /// « Denture mixte » — both sets present at once, FDI quadrants 1–8. Assumed between 6 and 12 years old
    /// inclusive.
    ///
    /// <para>⚠️ Appended as <b>2</b>, never inserted between the other two — see the type's own remark.</para>
    /// </summary>
    Mixed = 2,
}

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
/// ⚠️ **Deliberately two values, with a known limitation.** Real dentition passes through a *mixed* stage — a
/// seven-year-old carries deciduous and permanent teeth at once — so a patient marked <see cref="Child"/> cannot be
/// charted on a permanent molar until their record is switched to <see cref="Adult"/>, and the remaining baby teeth
/// then become unchartable. This was chosen knowingly over a third `Mixed` value; the escape hatch is that the field
/// is editable on the patient at any time. Note that <c>FdiTooth</c>/<c>tooth-multiselect</c> still classify each
/// tooth by its own FDI range, so a *stored* record holding both dentitions keeps rendering correctly — this enum
/// governs what can be charted next, never how history is read back.
/// </para>
/// </summary>
public enum DentitionType
{
    /// <summary>Deciduous / « dents de lait » — FDI quadrants 5–8.</summary>
    Child = 0,

    /// <summary>Permanent / « dents définitives » — FDI quadrants 1–4.</summary>
    Adult = 1,
}

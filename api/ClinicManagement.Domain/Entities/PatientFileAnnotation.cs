using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A marker somebody dropped on the surface of a 3D model (<c>mesh-interactive-viewer</c>).
///
/// <para>⚠️ <b>The point is in the FILE's own coordinates, never the viewer's.</b> The viewer moves the mesh onto
/// the origin so it can be orbited, and storing the moved position would put every marker in the wrong place the
/// moment anything about that centring changed — including a later version of the app that centred differently.
/// The scene is a rendering decision; the file is the record.</para>
///
/// <para>⚠️ <b>And the coordinates carry no unit, because the formats do not.</b> STL, PLY and OBJ hold bare
/// floats, so <c>X = 48.2</c> is 48.2 of whatever the exporter had in mind. Nothing here converts, scales or
/// interprets them: they are stored exactly as the file gave them, and what a length <i>means</i> stays a
/// question the reader answers in the viewer. A column named <c>XMillimetres</c> would have been a lie in the
/// schema.</para>
/// </summary>
public class PatientFileAnnotation : Entity<Guid>, IAuditable
{
    public Guid PatientFileId { get; private set; }

    /// <summary>The owning clinic, denormalised from <see cref="PatientFile"/>. See <see cref="PatientFolder.ClinicId"/>.</summary>
    public Guid ClinicId { get; private set; }

    public double X { get; private set; }
    public double Y { get; private set; }
    public double Z { get; private set; }

    /// <summary>
    /// The surface normal where the marker was dropped. Kept so the viewer can dim a marker on the far side of
    /// the model — a facing test, which is the only affordable stand-in for real occlusion on a mesh of a
    /// million triangles.
    /// </summary>
    public double NormalX { get; private set; }
    public double NormalY { get; private set; }
    public double NormalZ { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>Who dropped it. Shown nowhere yet; recorded because « who marked this? » is asked of a shared model.</summary>
    public string? CreatedBy { get; private set; }

    public PatientFile File { get; private set; } = null!;

    private PatientFileAnnotation() { } // For EF Core

    public PatientFileAnnotation(
        Guid id,
        Guid patientFileId,
        Guid clinicId,
        double x,
        double y,
        double z,
        double normalX,
        double normalY,
        double normalZ,
        string label,
        DateTime createdAtUtc,
        string? createdBy)
    {
        Id = id;
        PatientFileId = patientFileId;
        ClinicId = clinicId;
        X = x;
        Y = y;
        Z = z;
        NormalX = normalX;
        NormalY = normalY;
        NormalZ = normalZ;
        Label = Clean(label);
        CreatedAt = createdAtUtc;
        CreatedBy = createdBy;
    }

    public void Rename(string label, DateTime nowUtc)
    {
        Label = Clean(label);
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// ⚠️ An empty label is allowed and is <b>not</b> the same as no marker. Somebody who clears the text still
    /// wants the pin where they put it; refusing here would make « delete the words » mean « delete the marker »,
    /// which is not what the field looks like it does. The viewer numbers an unnamed marker for them.
    /// </summary>
    public const int MaxLabelLength = 200;

    private static string Clean(string label)
    {
        var trimmed = (label ?? string.Empty).Trim();
        return trimmed.Length <= MaxLabelLength ? trimmed : trimmed[..MaxLabelLength];
    }
}

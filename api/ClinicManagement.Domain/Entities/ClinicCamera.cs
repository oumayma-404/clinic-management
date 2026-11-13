using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A camera this cabinet imports photos from — what « pairing » a camera means in this product
/// (<c>features/camera-photo-import</c>).
///
/// <para>⚠️ <b>It lives on the server, not in one PC's settings</b>, because what it remembers is needed on every
/// path: the browser reading an SD card on the reception PC, the Windows app with the camera on USB, a phone picking
/// from its gallery. It remembers three things no photo can say about itself — whose agenda its photos belong to
/// (<see cref="DoctorId"/>), how wrong its clock is (<see cref="ClockOffsetMinutes"/>), and when it was last imported
/// from (<see cref="LastImportedAt"/>, the start of the next import's range).</para>
///
/// <para>⚠️ <b><see cref="DoctorId"/> is nullable with no foreign key</b> — the <c>MedicalDocument.DentalRecordId</c>
/// shape. A cabinet's shared camera belongs to nobody, and a retired doctor must not take the camera with them.</para>
///
/// <para>⚠️ <b>An <see cref="AggregateRoot{TId}"/> on purpose</b>: the audit interceptor writes one row per mutated
/// root, so renaming a camera or moving it to another doctor is attributable.</para>
/// </summary>
public class ClinicCamera : AggregateRoot<Guid>
{
    public const int FingerprintLength = 64;
    public const int MaxMakeLength = 60;
    public const int MaxModelLength = 100;
    public const int MaxSerialSuffixLength = 8;
    public const int MaxLabelLength = 60;

    /// <summary>A day either way: beyond that the clock is not « wrong », it was never set, and no offset fixes it.</summary>
    public const int MaxClockOffsetMinutes = 24 * 60;

    public Guid ClinicId { get; private set; }

    /// <summary>Lower-case hex SHA-256 of <c>Make|Model|BodySerialNumber</c> (or <c>Make|Model</c> with no serial).</summary>
    public string Fingerprint { get; private set; } = string.Empty;

    public string Make { get; private set; } = string.Empty;

    public string Model { get; private set; } = string.Empty;

    /// <summary>The last characters of the body's serial number, for telling two identical bodies apart on screen.</summary>
    public string? SerialSuffix { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public Guid? DoctorId { get; private set; }

    /// <summary>Minutes to ADD to this camera's clock to get the clinic's wall clock.</summary>
    public int ClockOffsetMinutes { get; private set; }

    public DateTime? LastImportedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    private ClinicCamera() { } // For EF Core

    public static ClinicCamera Register(
        Guid id,
        Guid clinicId,
        string fingerprint,
        string make,
        string model,
        string? serialSuffix,
        DateTime nowUtc)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("Le cabinet est requis.", nameof(clinicId));
        }

        if (!PhotoCaptureFacts.IsCaptureKey(fingerprint))
        {
            throw new ArgumentException("L'empreinte de l'appareil doit être un SHA-256.", nameof(fingerprint));
        }

        var cleanMake = Clip(make, MaxMakeLength) ?? string.Empty;
        var cleanModel = Clip(model, MaxModelLength) ?? string.Empty;

        if (cleanMake.Length == 0 && cleanModel.Length == 0)
        {
            throw new ArgumentException("Un appareil doit porter au moins une marque ou un modèle.", nameof(model));
        }

        return new ClinicCamera
        {
            Id = id,
            ClinicId = clinicId,
            Fingerprint = fingerprint.Trim().ToLowerInvariant(),
            Make = cleanMake,
            Model = cleanModel,
            SerialSuffix = Clip(serialSuffix, MaxSerialSuffixLength),
            Label = DefaultLabel(cleanMake, cleanModel),
            ClockOffsetMinutes = 0,
            CreatedAt = nowUtc,
        };
    }

    /// <summary>
    /// What a camera is called until somebody renames it. Canon writes « Canon » + « Canon EOS 250D » and Nikon
    /// « NIKON CORPORATION » + « NIKON Z 50 », so the model alone is the name whenever it already says the brand.
    /// </summary>
    public static string DefaultLabel(string make, string model)
    {
        var brand = make.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var label = model.Length == 0
            ? make
            : brand.Length == 0 || model.Contains(brand, StringComparison.OrdinalIgnoreCase)
                ? model
                : $"{brand} {model}";

        return Clip(label, MaxLabelLength) ?? model;
    }

    public void Rename(string label)
    {
        var clean = label?.Trim();
        if (string.IsNullOrEmpty(clean))
        {
            throw new ArgumentException("Le nom de l'appareil est requis.", nameof(label));
        }

        if (clean.Length > MaxLabelLength)
        {
            throw new ArgumentException($"Le nom de l'appareil ne peut dépasser {MaxLabelLength} caractères.", nameof(label));
        }

        Label = clean;
        Touch();
    }

    public void AssignDoctor(Guid? doctorId)
    {
        DoctorId = doctorId == Guid.Empty ? null : doctorId;
        Touch();
    }

    public void SetClockOffset(int minutes)
    {
        if (Math.Abs(minutes) > MaxClockOffsetMinutes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minutes), "Un décalage d'horloge de plus d'un jour ne se corrige pas : réglez l'heure de l'appareil.");
        }

        ClockOffsetMinutes = minutes;
        Touch();
    }

    /// <summary>
    /// Moves the « since the last import » mark forward, never back: two PCs importing the same card in either
    /// order must leave the later instant, or the next import re-offers photos already filed.
    /// </summary>
    public bool RecordImport(DateTime importedAtUtc)
    {
        if (LastImportedAt is { } last && last >= importedAtUtc)
        {
            return false;
        }

        LastImportedAt = importedAtUtc;
        Touch();
        return true;
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;

    private static string? Clip(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

namespace ClinicManagement.Domain.Common;

/// <summary>
/// What a camera import records about one photo, validated once for the two rows that carry it — the
/// <see cref="Entities.PatientFile"/> and, on the clinic-PC install where an original goes up in parts, the
/// <see cref="Entities.FileUploadSession"/> it is built from.
///
/// <para>⚠️ <b>The capture time is clamped, not refused.</b> A camera whose clock was never set writes 2000-01-01, and
/// one reset by a dead battery can write a date in the future; neither is a reason to refuse a photo the dentist took
/// today. Outside the plausible window the time is simply not recorded — the photo files, and nothing pretends to know
/// when it was taken.</para>
///
/// <para>⚠️ <b>The key is computed by the page and only checked for shape here.</b> It identifies a shot, not its bytes
/// (camera fingerprint + original time + original name + original size), so the server cannot recompute it from what
/// it receives — the cloud copy is a different file. It is used for one thing, refusing the same shot twice in one
/// cabinet, so a client that lies about it can only refuse its own photos.</para>
/// </summary>
public sealed class PhotoCaptureFacts
{
    /// <summary>Lower-case hex SHA-256.</summary>
    public const int CaptureKeyLength = 64;

    public const int MaxCameraModelLength = 100;

    /// <summary>The first camera this product could plausibly meet; a clock never set reads 2000-01-01 or earlier.</summary>
    public static readonly DateTime EarliestPlausibleUtc = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>How far ahead of the server a camera clock may run before its time is not believed.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromHours(1);

    public DateTime? CapturedAtUtc { get; }

    public string CaptureKey { get; }

    public string? CameraModel { get; }

    private PhotoCaptureFacts(DateTime? capturedAtUtc, string captureKey, string? cameraModel)
    {
        CapturedAtUtc = capturedAtUtc;
        CaptureKey = captureKey;
        CameraModel = cameraModel;
    }

    /// <summary>
    /// Builds the facts, normalising what can be normalised. Throws only on a key that is not a SHA-256 — the
    /// handlers check <see cref="IsCaptureKey"/> first and answer with a code, so the throw is the backstop.
    /// </summary>
    public static PhotoCaptureFacts Create(
        DateTime? capturedAtUtc,
        string captureKey,
        string? cameraModel,
        DateTime nowUtc)
    {
        if (!IsCaptureKey(captureKey))
        {
            throw new ArgumentException("La clé de prise de vue doit être une empreinte SHA-256.", nameof(captureKey));
        }

        return new PhotoCaptureFacts(
            Plausible(capturedAtUtc, nowUtc),
            captureKey.Trim().ToLowerInvariant(),
            NormalizeModel(cameraModel));
    }

    public static bool IsCaptureKey(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is { Length: CaptureKeyLength } && trimmed.All(Uri.IsHexDigit);
    }

    /// <summary>The capture time when a camera clock could have produced it, else null.</summary>
    public static DateTime? Plausible(DateTime? capturedAtUtc, DateTime nowUtc)
    {
        if (capturedAtUtc is not { } value)
        {
            return null;
        }

        var utc = value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
        return utc < EarliestPlausibleUtc || utc > nowUtc + FutureTolerance ? null : utc;
    }

    private static string? NormalizeModel(string? model)
    {
        var trimmed = model?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        // A display snapshot, never a key: clipping a firmware string that ran long beats refusing the photo over it.
        return trimmed.Length <= MaxCameraModelLength ? trimmed : trimmed[..MaxCameraModelLength];
    }
}

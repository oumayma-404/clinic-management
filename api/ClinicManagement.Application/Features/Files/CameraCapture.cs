using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Application.Features.Files;

/// <summary>
/// What a camera import sends beside a photo's bytes (<c>features/camera-photo-import</c>), shared by the two doors
/// that store a file — the single upload and the resumable one — so they cannot read the same fields differently.
///
/// <para>⚠️ <b>The time arrives as the clinic's wall clock, not as an instant.</b> A camera's EXIF time carries no
/// zone; the page corrects the camera clock and sends the result as « 2026-11-12T09:14:03 » with no offset, and the
/// one place that knows Tunisia is UTC+1 (<see cref="ClinicClock"/>) turns it into UTC. Only when the camera wrote
/// <c>OffsetTimeOriginal</c> does the page send that offset, and then it is exact.</para>
/// </summary>
public sealed record CameraCaptureInput(
    string? CaptureKey,
    DateTime? CapturedAtLocal,
    int? CapturedAtOffsetMinutes,
    string? CameraModel)
{
    public static readonly CameraCaptureInput None = new(null, null, null, null);

    /// <summary>A camera photo is one that names its shot; everything else is an ordinary upload.</summary>
    public bool IsCamera => !string.IsNullOrWhiteSpace(CaptureKey);

    /// <summary>The validated facts, null for an ordinary upload, or the refusal a malformed key earns.</summary>
    public Result<PhotoCaptureFacts?> ToFacts(DateTime nowUtc)
    {
        if (!IsCamera)
        {
            return Result<PhotoCaptureFacts?>.Success(null);
        }

        if (!PhotoCaptureFacts.IsCaptureKey(CaptureKey))
        {
            return Result<PhotoCaptureFacts?>.Failure(
                CameraImportRefusals.InvalidKey(), CameraImportRefusals.InvalidKeyCode);
        }

        if (CapturedAtOffsetMinutes is { } offset && Math.Abs(offset) > 14 * 60)
        {
            return Result<PhotoCaptureFacts?>.Failure(
                CameraImportRefusals.InvalidTime(), CameraImportRefusals.InvalidTimeCode);
        }

        return Result<PhotoCaptureFacts?>.Success(
            PhotoCaptureFacts.Create(ToUtc(), CaptureKey!, CameraModel, nowUtc));
    }

    private DateTime? ToUtc()
    {
        if (CapturedAtLocal is not { } local)
        {
            return null;
        }

        var wallClock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return CapturedAtOffsetMinutes is { } offset
            ? DateTime.SpecifyKind(wallClock.AddMinutes(-offset), DateTimeKind.Utc)
            : ClinicClock.ToUtc(wallClock);
    }
}

/// <summary>The import's refusals, each sentence beside its code (<c>SubscriptionRefusals</c>' shape).</summary>
public static class CameraImportRefusals
{
    public const string AlreadyImportedCode = "photo-already-imported";
    public const string InvalidKeyCode = "photo-capture-key-invalid";
    public const string InvalidTimeCode = "photo-capture-time-invalid";
    public const string RangeInvalidCode = "photo-import-range-invalid";
    public const string RangeTooLongCode = "photo-import-range-too-long";
    public const string TooManyKeysCode = "photo-import-too-many-keys";
    public const string CameraDoctorUnknownCode = "camera-doctor-unknown";
    public const string CameraNotFoundCode = "camera-not-found";
    public const string FolderKeyUnknownCode = "folder-key-unknown";
    public const string OriginalNotCameraCode = "file-original-not-camera";
    public const string OriginalInvalidCode = "file-original-invalid";

    /// <summary>The longest range one import reads: a card left a month in the camera is the outer case.</summary>
    public const int MaxRangeDays = 31;

    /// <summary>One request's worth of keys — a card's whole content in a handful of calls.</summary>
    public const int MaxKeysPerRequest = 2000;

    public static string AlreadyImported() =>
        "Cette photo a déjà été rangée dans le dossier d'un patient.";

    public static string InvalidKey() =>
        "Cette photo n'a pas pu être identifiée. Relancez l'import.";

    public static string InvalidTime() =>
        "L'heure de prise de vue de cette photo est illisible.";

    public static string RangeInvalid() =>
        "Les dates de l'import sont illisibles.";

    public static string RangeTooLong() =>
        $"Un import couvre au plus {MaxRangeDays} jours. Importez la carte en plusieurs fois.";

    public static string TooManyKeys() =>
        $"Trop de photos à vérifier d'un coup (au plus {MaxKeysPerRequest}).";

    public static string CameraDoctorUnknown() =>
        "Ce praticien n'appartient pas à ce cabinet.";

    public static string CameraNotFound() =>
        "Cet appareil est introuvable.";

    public static string FolderKeyUnknown() =>
        "Ce dossier n'existe pas.";

    public static string OriginalNotCamera() =>
        "Seule une photo de l'appareil peut avoir un original conservé au cabinet.";

    public static string OriginalInvalid() =>
        "Les informations sur l'original sont incomplètes.";
}

/// <summary>
/// PostgreSQL 23505, matched on the provider's <c>SqlState</c> without referencing Npgsql — the shape four
/// handlers each carry a private copy of; the camera import uses this one.
/// </summary>
public static class UniqueViolation
{
    public static bool Is(DbUpdateException ex)
    {
        for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505")
            {
                return true;
            }
        }

        return false;
    }
}

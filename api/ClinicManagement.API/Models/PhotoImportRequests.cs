namespace ClinicManagement.API.Models;

/// <summary>« Changer de patient » (<c>features/camera-photo-import</c>).</summary>
public class MoveFileToPatientRequest
{
    public Guid TargetPatientId { get; set; }

    /// <summary>The version the form read — a concurrent rename makes the move a 409.</summary>
    public uint Version { get; set; }
}

/// <summary>A clinic PC kept a camera photo's full-size original.</summary>
public class RecordFileOriginalRequest
{
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string HeldBy { get; set; } = string.Empty;
}

/// <summary>The shots of a card, by key — which of them are already filed?</summary>
public class KnownCapturesRequest
{
    public List<string> CaptureKeys { get; set; } = new();
}

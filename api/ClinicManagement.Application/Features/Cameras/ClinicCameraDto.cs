using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Cameras;

/// <summary>A paired camera, as « Importer les photos » and the « Appareils photo » card read it.</summary>
public sealed class ClinicCameraDto
{
    public Guid Id { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? SerialSuffix { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid? DoctorId { get; set; }
    public int ClockOffsetMinutes { get; set; }
    public DateTime? LastImportedAt { get; set; }

    /// <summary>Round-tripped by the settings card so a concurrent rename is a 409, not a silent overwrite.</summary>
    public uint Version { get; set; }

    public static ClinicCameraDto From(ClinicCamera camera) => new()
    {
        Id = camera.Id,
        Fingerprint = camera.Fingerprint,
        Make = camera.Make,
        Model = camera.Model,
        SerialSuffix = camera.SerialSuffix,
        Label = camera.Label,
        DoctorId = camera.DoctorId,
        ClockOffsetMinutes = camera.ClockOffsetMinutes,
        LastImportedAt = camera.LastImportedAt,
        Version = camera.Version,
    };
}

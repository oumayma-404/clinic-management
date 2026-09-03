using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// A marker on a 3D model, as the browser sees it (<c>mesh-interactive-viewer</c>).
///
/// <para>⚠️ <b>The coordinates go out exactly as they came in, in the file's own units — which is to say, in no
/// unit at all.</b> STL, PLY and OBJ record none, so the viewer chooses how to read them and says which it
/// chose. Nothing on this DTO is named « millimetres », because the server has no basis for that claim.</para>
/// </summary>
public class PatientFileAnnotationDto
{
    public Guid Id { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public double NormalX { get; set; }
    public double NormalY { get; set; }
    public double NormalZ { get; set; }

    public string Label { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public static class PatientFileAnnotationMapping
{
    public static PatientFileAnnotationDto ToDto(this PatientFileAnnotation annotation) => new()
    {
        Id = annotation.Id,
        X = annotation.X,
        Y = annotation.Y,
        Z = annotation.Z,
        NormalX = annotation.NormalX,
        NormalY = annotation.NormalY,
        NormalZ = annotation.NormalZ,
        Label = annotation.Label,
        CreatedAt = annotation.CreatedAt,
        CreatedBy = annotation.CreatedBy,
    };
}

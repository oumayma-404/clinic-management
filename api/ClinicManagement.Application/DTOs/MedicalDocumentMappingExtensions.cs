using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// The one <see cref="MedicalDocument"/> → <see cref="MedicalDocumentDto"/> mapping.
///
/// <para>⚠️ There were <b>four</b> byte-identical copies of this initializer — create, update, get-one and
/// get-many — which is this codebase's dominant defect shape: a correct answer written once per call site. Adding
/// `Version` to the DTO meant editing four places and any one of them silently returning 0, which reads as
/// « not supplied » and turns the concurrency check off for whichever screen read from it. One mapper is what
/// makes the next field impossible to half-add.</para>
/// </summary>
public static class MedicalDocumentMappingExtensions
{
    public static MedicalDocumentDto ToDto(this MedicalDocument document) => new()
    {
        Id = document.Id,
        PatientId = document.PatientId,
        PatientName = document.PatientName,
        PatientAge = document.PatientAge,
        DocumentType = document.DocumentType,
        DocumentDate = document.DocumentDate,
        RecipientDoctorName = document.RecipientDoctorName,
        RecipientDoctorSpecialty = document.RecipientDoctorSpecialty,
        ContentJson = document.ContentJson,
        ClinicName = document.ClinicName,
        ClinicAddress = document.ClinicAddress,
        ClinicPhone = document.ClinicPhone,
        DoctorName = document.DoctorName,
        DoctorSpecialty = document.DoctorSpecialty,
        IsDraft = document.IsDraft,
        FileId = document.FileId,
        AppointmentId = document.AppointmentId,
        Version = document.Version,
        CreatedAt = document.CreatedAt,
        UpdatedAt = document.UpdatedAt,
    };
}

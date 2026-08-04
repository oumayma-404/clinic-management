using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>Create a draft invoice (no number consumed). Optionally pre-filled from a dental record/appointment.</summary>
public class CreateInvoiceCommand : IRequest<Result<InvoiceDto>>
{
    public Guid PatientId { get; set; }
    public Guid? DentalRecordId { get; set; }
    public Guid? AppointmentId { get; set; }

    /// <summary>
    /// Which practitioner earned this note (L9 attribution). Optional: omit it and the visit's practitioner is
    /// used, else the caller's own <c>Doctor</c> record, else none — <c>PractitionerAttribution</c> owns that
    /// precedence, and it is the *only* place it is written.
    /// </summary>
    public Guid? DoctorId { get; set; }

    public List<InvoiceLineRequest> Lines { get; set; } = new();
}

public class CreateInvoiceCommandHandler : IRequestHandler<CreateInvoiceCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicContext _clinicContext;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateInvoiceCommandHandler> _logger;

    public CreateInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        IDoctorRepository doctorRepository,
        IClinicContext clinicContext,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CreateInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _doctorRepository = doctorRepository;
        _clinicContext = clinicContext;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(CreateInvoiceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<InvoiceDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
            {
                return Result<InvoiceDto>.Failure("Patient introuvable.");
            }

            // The visit this note bills, when it was raised from an appointment context (AC-P6.12). The column
            // has existed since the invoice was written and nothing ever populated it, so nothing ever checked
            // it either: an id is now verified to be a real appointment of THIS clinic AND of this patient.
            // Without that, a crafted request could pin another clinic's visit onto the invoice — and a visit
            // belonging to a different patient would make « facturé » appear on the wrong record.
            // Captured while the appointment is already loaded below — it is the middle term of the attribution
            // precedence, and re-reading the appointment for it would be a second query for a row in hand.
            Guid? appointmentDoctorId = null;

            if (request.AppointmentId.HasValue)
            {
                var appointment = await _appointmentRepository.GetByIdAsync(request.AppointmentId.Value, cancellationToken);
                if (appointment == null || appointment.ClinicId != clinicId)
                {
                    return Result<InvoiceDto>.Failure("Rendez-vous introuvable.");
                }
                if (appointment.PatientId != request.PatientId)
                {
                    return Result<InvoiceDto>.Failure(
                        "Ce rendez-vous appartient à un autre patient : la facture ne peut pas y être rattachée.");
                }
                appointmentDoctorId = appointment.DoctorId;
            }

            var invoice = new Invoice(
                Guid.NewGuid(),
                clinicId,
                request.PatientId,
                request.DentalRecordId,
                request.AppointmentId);

            // L9 — attribute the note to a practitioner. `SetDoctor` and not a ctor argument, deliberately: the
            // answer is a *derivation* over three sources, and threading it through the ctor of every construction
            // path is how one of them ends up passing the caller where the appointment was known.
            invoice.SetDoctor(await ResolveDoctorAsync(request.DoctorId, appointmentDoctorId, clinicId, cancellationToken));

            if (request.Lines.Count > 0)
            {
                invoice.SetLines(request.Lines.Select(l => (l.Designation, l.Quantity, l.UnitPriceHt, l.DentalRecordId, l.DentalActCodeId, l.CodeActe)));
            }

            await _invoiceRepository.AddAsync(invoice, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created draft invoice {InvoiceId} for patient {PatientId}", invoice.Id, invoice.PatientId);

            return Result<InvoiceDto>.Success(invoice.ToDto(patient.GetFullName()));
        }
        catch (ArgumentException ex)
        {
            return Result<InvoiceDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error creating invoice");
            return Result<InvoiceDto>.Failure("Erreur lors de la création de la facture.");
        }
    }

    /// <summary>
    /// The practitioner this note is attributed to, through the one shared precedence rule
    /// (<see cref="PractitionerAttribution"/>).
    /// <para>
    /// The caller's own <c>Doctor</c> record is the last resort rather than the first: a secretary raising a note
    /// for a dentist's work must not credit themselves, and in the common Tunisian single-dentist practice the
    /// owner *is* the caller, so the fall-back is right exactly where it is right.
    /// </para>
    /// </summary>
    private async Task<Guid?> ResolveDoctorAsync(
        Guid? explicitDoctorId, Guid? appointmentDoctorId, Guid clinicId, CancellationToken cancellationToken)
    {
        var clinicDoctorIds = await PractitionerAttribution.LoadClinicDoctorIdsAsync(
            _doctorRepository, clinicId, cancellationToken);

        Guid? callerDoctorId = null;
        var userId = _clinicContext.GetUserId();
        if (!string.IsNullOrEmpty(userId))
        {
            var caller = await _doctorRepository.GetByUserIdAsync(userId, cancellationToken);
            callerDoctorId = caller?.Id;
        }

        return PractitionerAttribution.Resolve(
            explicitDoctorId, appointmentDoctorId, callerDoctorId, clinicDoctorIds);
    }

}

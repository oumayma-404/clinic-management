using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Invoices.Queries;

/// <summary>
/// Every act this patient's fiches de soins have recorded, already priced as billable lines.
///
/// <para><b>Why it exists.</b> The note d'honoraires editor in the Documents module could offer acts from the
/// <b>catalogue</b> only — a tarif, not what was actually done — so a fee note for work already carried out was
/// retyped from the patient's own record in another tab. This read is what lets that editor propose the séances
/// themselves.</para>
///
/// <para>⚠️ <b>It exists as a read rather than as a rule in the browser precisely because the rule already has an
/// owner.</b> <see cref="DentalRecordInvoiceLines"/> is the single authority on how recorded work becomes money —
/// per-tooth provenance, the legacy fallback, the « (dents 16, 26) » designation — and its own summary records
/// that the rule was <b>moved</b> server-side rather than copied when a second caller appeared. Every field the
/// rule needs is already on <c>DentalRecordActDto</c>, so re-deriving it in TypeScript would compile, look
/// right, and be the second pricing authority that helper was written to remove.</para>
///
/// <para>⚠️ <b>One read for the whole patient, not one per fiche.</b> The picker lists every act at once, and a
/// call per séance is an N+1 over a history that routinely runs to dozens.</para>
///
/// <para>It reads; it writes nothing, mints no number and moves no balance. What the editor saves is a printable
/// <c>MedicalDocument</c> — the numbered fiscal note is still raised in Factures.</para>
/// </summary>
public class GetPatientBillableActLinesQuery : IRequest<Result<IEnumerable<BillableActLineDto>>>
{
    public Guid PatientId { get; set; }
}

public class GetPatientBillableActLinesQueryHandler
    : IRequestHandler<GetPatientBillableActLinesQuery, Result<IEnumerable<BillableActLineDto>>>
{
    private readonly IDentalRecordRepository _dentalRecords;
    private readonly IPatientRepository _patients;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetPatientBillableActLinesQueryHandler(
        IDentalRecordRepository dentalRecords,
        IPatientRepository patients,
        ICurrentClinicResolver clinicResolver)
    {
        _dentalRecords = dentalRecords;
        _patients = patients;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IEnumerable<BillableActLineDto>>> Handle(
        GetPatientBillableActLinesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // DentalRecord carries no ClinicId and is outside the global filter, so verifying the owning
            // patient is the sole tenant guard for this read — same rule as GetDentalRecordsQuery.
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<IEnumerable<BillableActLineDto>>.Failure(
                    clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patients.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<IEnumerable<BillableActLineDto>>.Failure("Patient introuvable.");
            }

            var records = await _dentalRecords.GetByPatientIdAsync(request.PatientId, cancellationToken);

            // Newest séance first: a fee note is almost always raised for work just done, so the acts the
            // reader is looking for are the ones at the top of the list.
            var lines = records
                .OrderByDescending(record => record.InterventionDate)
                .ThenByDescending(record => record.Id)
                .SelectMany(record => DentalRecordInvoiceLines.For(record).Select((line, index) =>
                    new BillableActLineDto
                    {
                        // Composed rather than an act id, because the fallback line of a legacy fiche with no
                        // acts has none — and the browser only needs to tell one offered row from another.
                        Key = $"{record.Id}:{index}",
                        DentalRecordId = record.Id,
                        InterventionDate = record.InterventionDate,
                        Designation = line.Designation,
                        Quantity = line.Quantity,
                        UnitPriceHt = line.UnitPriceHt,
                    }))
                .ToList();

            return Result<IEnumerable<BillableActLineDto>>.Success(lines);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<BillableActLineDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

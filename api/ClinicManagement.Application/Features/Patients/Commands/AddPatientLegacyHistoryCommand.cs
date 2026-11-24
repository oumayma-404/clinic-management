using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Patients.Commands;

/// <summary>
/// Appends lines from the cabinet's previous software to a patient's « Ancien logiciel » history — the door the
/// migration import writes through. Append, never replace: two old records of one person (merged on import) each
/// bring their lines, and a re-run is kept idempotent by the import's own state file, not by deleting here.
/// All lines are refused together if one is invalid, so a half-written history cannot exist.
/// </summary>
public class AddPatientLegacyHistoryCommand : IRequest<Result<int>>
{
    public const int MaxEntriesPerCall = 500;

    public Guid PatientId { get; set; }
    public List<LegacyHistoryEntryInput> Entries { get; set; } = new();
}

public class LegacyHistoryEntryInput
{
    public DateOnly? Date { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Details { get; set; }
}

public class AddPatientLegacyHistoryCommandHandler : IRequestHandler<AddPatientLegacyHistoryCommand, Result<int>>
{
    private readonly IPatientRepository _patients;
    private readonly IPatientLegacyHistoryRepository _history;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public AddPatientLegacyHistoryCommandHandler(
        IPatientRepository patients,
        IPatientLegacyHistoryRepository history,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patients = patients;
        _history = history;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(AddPatientLegacyHistoryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Entries.Count == 0)
            {
                return Result<int>.Failure("Aucune ligne à ajouter.");
            }
            if (request.Entries.Count > AddPatientLegacyHistoryCommand.MaxEntriesPerCall)
            {
                return Result<int>.Failure(
                    $"Trop de lignes en un envoi ({request.Entries.Count}, maximum {AddPatientLegacyHistoryCommand.MaxEntriesPerCall}).");
            }

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<int>.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patients.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinic.Value)
            {
                return Result<int>.Failure("Patient introuvable.");
            }

            // Positions continue after the lines already there, so a second batch (a merged record) keeps its own
            // order instead of interleaving with the first on a shared day.
            var start = (await _history.GetByPatientAsync(request.PatientId, cancellationToken)).Count;
            var entries = new List<PatientLegacyHistoryEntry>(request.Entries.Count);
            for (var i = 0; i < request.Entries.Count; i++)
            {
                var input = request.Entries[i];
                try
                {
                    // The patient's clinic, not the caller's — equal by the check above, true by construction.
                    entries.Add(new PatientLegacyHistoryEntry(
                        Guid.NewGuid(), patient.Id, patient.ClinicId,
                        input.Date, input.Category, input.Title, input.Details, start + i));
                }
                catch (ArgumentException ex)
                {
                    return Result<int>.Failure($"Ligne {i + 1} : {ex.Message.Split(" (Parameter")[0]}");
                }
            }

            await _history.AddRangeAsync(entries, cancellationToken);
            // No write to the patient row: its `xmin` is what an open « Modifier » form holds.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<int>.Success(entries.Count);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<int>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

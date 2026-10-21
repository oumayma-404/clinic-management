using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.DoctorLeaves.Queries;

/// <summary>The cabinet's congés touching an inclusive day window — what the agenda draws.</summary>
public class GetClinicDoctorLeavesQuery : IRequest<Result<List<DoctorLeaveDto>>>
{
    /// <summary>Wider than any agenda view (a phone Mois is six weeks); a request past it is a mistake, not a view.</summary>
    public const int MaxWindowDays = 400;

    public DateOnly FromDay { get; set; }
    public DateOnly ToDay { get; set; }
}

public class GetClinicDoctorLeavesQueryHandler : IRequestHandler<GetClinicDoctorLeavesQuery, Result<List<DoctorLeaveDto>>>
{
    private readonly IDoctorLeaveRepository _leaves;
    private readonly IDoctorRepository _doctors;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetClinicDoctorLeavesQueryHandler(
        IDoctorLeaveRepository leaves,
        IDoctorRepository doctors,
        ICurrentClinicResolver clinicResolver)
    {
        _leaves = leaves;
        _doctors = doctors;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<List<DoctorLeaveDto>>> Handle(GetClinicDoctorLeavesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.ToDay < request.FromDay)
                return Result<List<DoctorLeaveDto>>.Failure("La fin de la période doit suivre son début.");
            if (request.ToDay.DayNumber - request.FromDay.DayNumber + 1 > GetClinicDoctorLeavesQuery.MaxWindowDays)
                return Result<List<DoctorLeaveDto>>.Failure(
                    $"La période demandée dépasse {GetClinicDoctorLeavesQuery.MaxWindowDays} jours.");

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<List<DoctorLeaveDto>>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var leaves = await _leaves.GetOverlappingAsync(clinic.Value, request.FromDay, request.ToDay, cancellationToken);
            if (leaves.Count == 0)
                return Result<List<DoctorLeaveDto>>.Success(new List<DoctorLeaveDto>());

            // The roster, not a by-id read: bounded by the cabinet's staff, and it already resolves a retired name.
            var names = (await _doctors.GetByClinicIdAsync(clinic.Value, cancellationToken))
                .ToDictionary(d => d.Id, d => d.FullName);
            return Result<List<DoctorLeaveDto>>.Success(leaves
                .Select(l => l.ToDto(names.TryGetValue(l.DoctorId, out var name) ? name : string.Empty))
                .ToList());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<List<DoctorLeaveDto>>.Failure("Erreur lors de la récupération des congés.", ex);
        }
    }
}

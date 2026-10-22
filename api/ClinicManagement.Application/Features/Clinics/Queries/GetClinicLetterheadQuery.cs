using MediatR;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Queries;

/// <summary>Whether the caller's cabinet has its own letterhead.</summary>
public class GetClinicLetterheadQuery : IRequest<Result<ClinicLetterheadDto>>
{
}

public class GetClinicLetterheadQueryHandler : IRequestHandler<GetClinicLetterheadQuery, Result<ClinicLetterheadDto>>
{
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicRepository _clinicRepository;

    public GetClinicLetterheadQueryHandler(ICurrentClinicResolver clinicResolver, IClinicRepository clinicRepository)
    {
        _clinicResolver = clinicResolver;
        _clinicRepository = clinicRepository;
    }

    public async Task<Result<ClinicLetterheadDto>> Handle(GetClinicLetterheadQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicId = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicId.IsFailure)
            {
                return Result<ClinicLetterheadDto>.FailureFrom(clinicId);
            }

            var clinic = await _clinicRepository.GetByIdAsync(clinicId.Value, cancellationToken);
            return clinic == null
                ? Result<ClinicLetterheadDto>.Failure("Clinique introuvable.")
                : Result<ClinicLetterheadDto>.Success(ClinicLetterheadDto.From(clinic));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<ClinicLetterheadDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

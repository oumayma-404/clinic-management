using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.CnamNomenclature.Commands;

// Update a valeur de la lettre clé (VLC) — the dinar value per lettre clé (FR-5.2). AdminOnly. Target by
// id (from the admin screen). Unknown id → not-found failure.
public class UpdateCnamLetterValueCommand : IRequest<Result<CnamLetterValueDto>>
{
    public Guid Id { get; set; }
    public decimal Value { get; set; }
}

public class UpdateCnamLetterValueCommandHandler : IRequestHandler<UpdateCnamLetterValueCommand, Result<CnamLetterValueDto>>
{
    private readonly ICnamCatalogRepository _repository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateCnamLetterValueCommandHandler> _logger;

    public UpdateCnamLetterValueCommandHandler(
        ICnamCatalogRepository repository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<UpdateCnamLetterValueCommandHandler> logger)
    {
        _repository = repository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<CnamLetterValueDto>> Handle(UpdateCnamLetterValueCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Authoritative tenant guard: resolve the caller's clinic from the DB rather than relying on the
            // EF global query filter, which is FAIL-OPEN — a token minted without a clinic_id claim leaves it
            // inactive, and this row could then be reached by id from another clinic (audit § 2, finding 10).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<CnamLetterValueDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var value = await _repository.GetLetterValueByIdAsync(request.Id, cancellationToken);
            if (value is null || value.ClinicId != clinicResult.Value)
            {
                return Result<CnamLetterValueDto>.Failure("Valeur de la lettre clé introuvable.");
            }

            try
            {
                value.SetValue(request.Value);
            }
            catch (ArgumentException ex)
            {
                return Result<CnamLetterValueDto>.Failure(ex.Message);
            }

            await _repository.UpdateLetterValueAsync(value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated CNAM letter value {Id} ({Cle})", value.Id, value.LettreCle);
            return Result<CnamLetterValueDto>.Success(CnamEntryMapper.ToDto(value));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error updating CNAM letter value {Id}", request.Id);
            return Result<CnamLetterValueDto>.Failure("Erreur lors de la mise à jour de la valeur.");
        }
    }
}

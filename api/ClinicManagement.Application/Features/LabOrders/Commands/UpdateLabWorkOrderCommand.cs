using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.LabOrders.Commands;

public class UpdateLabWorkOrderCommand : IRequest<Result<LabWorkOrderDto>>
{
    public Guid Id { get; set; }
    public int? ToothNumber { get; set; }
    public string Prosthetist { get; set; } = string.Empty;
    public string WorkDescription { get; set; } = string.Empty;
    public DateTime? SentDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public decimal? Cost { get; set; }
    public string? Notes { get; set; }
}

public class UpdateLabWorkOrderCommandHandler : IRequestHandler<UpdateLabWorkOrderCommand, Result<LabWorkOrderDto>>
{
    private readonly ILabWorkOrderRepository _labWorkOrderRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateLabWorkOrderCommandHandler(
        ILabWorkOrderRepository labWorkOrderRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _labWorkOrderRepository = labWorkOrderRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LabWorkOrderDto>> Handle(UpdateLabWorkOrderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Prosthetist))
                return Result<LabWorkOrderDto>.Failure("Le prothésiste est requis.");
            if (string.IsNullOrWhiteSpace(request.WorkDescription))
                return Result<LabWorkOrderDto>.Failure("La description du travail est requise.");
            if (request.Cost.HasValue && request.Cost.Value < 0)
                return Result<LabWorkOrderDto>.Failure("Le coût ne peut pas être négatif.");

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<LabWorkOrderDto>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var order = await _labWorkOrderRepository.GetByIdAsync(request.Id, cancellationToken);
            if (order == null || order.ClinicId != clinic.Value)
                return Result<LabWorkOrderDto>.Failure("Bon de laboratoire introuvable.");

            order.UpdateDetails(
                request.Prosthetist.Trim(),
                request.WorkDescription.Trim(),
                request.ToothNumber,
                request.SentDate,
                request.ExpectedDate,
                request.Cost,
                string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim());

            await _labWorkOrderRepository.UpdateAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<LabWorkOrderDto>.Success(order.ToDto());
        }
        catch (ArgumentException ex)
        {
            return Result<LabWorkOrderDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<LabWorkOrderDto>.Failure($"Erreur lors de la mise à jour du bon de laboratoire : {ex.Message}");
        }
    }
}

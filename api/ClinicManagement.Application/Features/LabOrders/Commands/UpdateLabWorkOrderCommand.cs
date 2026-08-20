using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Suppliers;
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

    /// <summary>
    /// The séance this prothèse belongs to (AC-23). ⚠️ <b>Not tri-state</b> — this command already replaces every
    /// other field wholesale, so sending null detaches the bon, which is the « ce n'était pas cette séance »
    /// correction. Making one field of a replace-semantics command behave as patch-semantics is how a link gets
    /// silently kept when the user meant to clear it.
    /// </summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>
    /// The laboratory as a fournisseur. ⚠️ <b>Not tri-state, unlike <c>UpdateStockItemCommand.SupplierId</c></b>,
    /// and for the reason stated on <see cref="AppointmentId"/> above: this command replaces every field
    /// wholesale, so null detaches. The stock one is a patch in practice — « Désactiver » and the adjust dialog
    /// both post partial bodies — which is exactly the difference that makes one a tri-state and the other not.
    /// </summary>
    public Guid? SupplierId { get; set; }
}

public class UpdateLabWorkOrderCommandHandler : IRequestHandler<UpdateLabWorkOrderCommand, Result<LabWorkOrderDto>>
{
    private readonly ILabWorkOrderRepository _labWorkOrderRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateLabWorkOrderCommandHandler(
        ILabWorkOrderRepository labWorkOrderRepository,
        IAppointmentRepository appointmentRepository,
        ISupplierRepository supplierRepository,
        IExpenseRepository expenseRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _labWorkOrderRepository = labWorkOrderRepository;
        _appointmentRepository = appointmentRepository;
        _supplierRepository = supplierRepository;
        _expenseRepository = expenseRepository;
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

            // The bon's OWN patient, not one from the request: this command cannot move a bon between patients,
            // so the appointment must belong to the patient the bon already names.
            var link = await LabOrderAppointmentLink.ValidateAsync(
                _appointmentRepository, request.AppointmentId, clinic.Value, order.PatientId, cancellationToken);
            if (link.IsFailure)
                return Result<LabWorkOrderDto>.Failure(link.Error!);

            var supplier = await SupplierLink.ResolveAsync(
                _supplierRepository, clinic.Value, request.SupplierId, cancellationToken);
            if (supplier.IsFailure)
                return Result<LabWorkOrderDto>.FailureFrom(supplier);

            order.UpdateDetails(
                request.Prosthetist.Trim(),
                request.WorkDescription.Trim(),
                request.ToothNumber,
                request.SentDate,
                request.ExpectedDate,
                request.Cost,
                string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                request.AppointmentId,
                supplier.Value?.Id);

            // The second door onto the caisse posting, and the one that is easy to miss: a bon routinely arrives
            // before the laboratory's facture does, so it is received with no coût and edited to enter it after.
            // Wired only to the status transition, that bon would owe a dépense with nothing left to post it.
            await LabOrderCaisseExpense.PostIfDueAsync(_expenseRepository, order, clinic.Value, cancellationToken);

            await _labWorkOrderRepository.UpdateAsync(order, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<LabWorkOrderDto>.Success(order.ToDto(supplier: supplier.Value));
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

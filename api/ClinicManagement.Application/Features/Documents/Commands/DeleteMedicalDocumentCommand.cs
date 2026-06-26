using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Documents.Commands;

public class DeleteMedicalDocumentCommand : IRequest<Result<bool>>
{
    public Guid Id { get; set; }
}

public class DeleteMedicalDocumentCommandHandler : IRequestHandler<DeleteMedicalDocumentCommand, Result<bool>>
{
    private readonly IMedicalDocumentRepository _documentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteMedicalDocumentCommandHandler(
        IMedicalDocumentRepository documentRepository,
        IUnitOfWork unitOfWork)
    {
        _documentRepository = documentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(DeleteMedicalDocumentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _documentRepository.GetByIdAsync(request.Id, cancellationToken);
            if (document == null)
            {
                return Result<bool>.Failure("Medical document not found");
            }

            await _documentRepository.DeleteAsync(document, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Error deleting medical document: {ex.Message}");
        }
    }
}









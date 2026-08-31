using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Import;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

/// <summary>
/// The CSV import's <b>dry run</b> (L5): what the file's columns are, what mapping was applied, and what would
/// happen to every row — without writing anything.
///
/// <para>⚠️ <b>A query, deliberately, even though the endpoint is a POST.</b> Two reasons. It writes nothing, so it
/// must not appear in <c>Features.Patients.Commands</c> — <c>RealtimeBroadcastBehavior</c> derives its resource key
/// from the namespace, and a dry run that told every open client in the practice that the patient list had changed
/// would be announcing an import that has not happened. And the verb has to be POST regardless: the input is a
/// file. The batch CNAM estimate is the same shape and the same reasoning.</para>
///
/// <para>It is re-sent on every mapping change, which is why it takes the mapping as input rather than remembering
/// one: there is <b>no server-side staging</b> of the uploaded file. A staging table would need an owner, a
/// lifetime and a pruner nobody would write, and its rows would outlive the browser tab that created them; sending
/// the file again costs one upload of a text file the client already holds.</para>
/// </summary>
public class PreviewPatientImportQuery : IRequest<Result<PatientImportPreviewDto>>
{
    public byte[] FileContent { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// <c>field token → column index</c>, or null/empty to let the server detect it from the headers. A negative
    /// index means « do not import this field », which is how the operator un-maps a column detection guessed.
    /// </summary>
    public Dictionary<string, int>? Mapping { get; set; }
}

public class PreviewPatientImportQueryHandler
    : IRequestHandler<PreviewPatientImportQuery, Result<PatientImportPreviewDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public PreviewPatientImportQueryHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<PatientImportPreviewDto>> Handle(
        PreviewPatientImportQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicId = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicId.IsFailure)
            {
                return Result<PatientImportPreviewDto>.FailureFrom(clinicId);
            }

            var existing = await _patientRepository.GetIdentitiesAsync(clinicId.Value, cancellationToken);

            var plan = PatientImportPlanner.Build(request.FileContent, request.Mapping, existing);
            if (plan.IsFailure)
            {
                return Result<PatientImportPreviewDto>.FailureFrom(plan);
            }

            return Result<PatientImportPreviewDto>.Success(PatientImportMapping.ToPreview(plan.Value!));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientImportPreviewDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

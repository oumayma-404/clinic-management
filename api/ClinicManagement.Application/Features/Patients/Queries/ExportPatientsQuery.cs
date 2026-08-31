using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Patients.Queries;

/// <summary>
/// « Exporter » on the patients page — the same filtered list as a CSV, <b>and recorded</b>.
///
/// <para><b>Why this is its own query rather than the controller re-sending <see cref="GetPatientsQuery"/>.</b>
/// The export and the list read are the same read: the export is simply <c>GetPatientsQuery</c> with no paging,
/// which is what makes « honours the filters, exports the whole filtered set, never the current page » true by
/// construction. But only one of the two may write an audit row — auditing the shared query would put a ledger
/// entry on every page turn of the patients screen, which is both noise and a lie about what happened. So the
/// export gets a door of its own that reuses the read and adds the recording.</para>
///
/// <para>⚠️ <b>A Query that writes, deliberately</b> — <c>BuildClinicArchiveQuery</c>'s precedent and its exact
/// reason: a Command here would broadcast into the clinic's group on every export, because
/// <c>RealtimeBroadcastBehavior</c> derives its key from the namespace, and « something changed » is false.</para>
///
/// <para>⚠️ <b>The role gate stays on the controller and is NOT narrowed here.</b> Reception legitimately exports
/// patient lists — a recall list, a contact list for the accountant — and this feature is about making the export
/// attributable and bounded, not about taking it away. What the audit found was that the same data the ZIP
/// archive guards with four controls was reachable through this door with none.</para>
/// </summary>
public class ExportPatientsQuery : IRequest<Result<IReadOnlyList<PatientDto>>>
{
    public string? SearchTerm { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
    public bool FlaggedOnly { get; set; }
}

public class ExportPatientsQueryHandler
    : IRequestHandler<ExportPatientsQuery, Result<IReadOnlyList<PatientDto>>>
{
    private readonly ISender _sender;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IAuditActorProvider _auditActor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ExportPatientsQueryHandler> _logger;

    public ExportPatientsQueryHandler(
        ISender sender,
        ICurrentClinicResolver clinicResolver,
        IAuditEntryRepository auditEntries,
        IAuditActorProvider auditActor,
        IUnitOfWork unitOfWork,
        ILogger<ExportPatientsQueryHandler> logger)
    {
        _sender = sender;
        _clinicResolver = clinicResolver;
        _auditEntries = auditEntries;
        _auditActor = auditActor;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<PatientDto>>> Handle(
        ExportPatientsQuery request, CancellationToken cancellationToken)
    {
        var clinicId = await _clinicResolver.GetClinicIdAsync(cancellationToken);
        if (clinicId.IsFailure)
        {
            return Result<IReadOnlyList<PatientDto>>.Failure(clinicId.Error!);
        }

        // The read itself, unchanged and unpaged — see the class note on why it is reused rather than repeated.
        var page = await _sender.Send(
            new GetPatientsQuery
            {
                SearchTerm = request.SearchTerm,
                CreatedFrom = request.CreatedFrom,
                CreatedTo = request.CreatedTo,
                FlaggedOnly = request.FlaggedOnly,
            },
            cancellationToken);

        if (page.IsFailure)
        {
            return Result<IReadOnlyList<PatientDto>>.Failure(page.Error!, page.Code);
        }

        var rows = page.Value!.Items;

        // Recorded BEFORE the file is handed back, and NOT best-effort: an unrecorded export that succeeds is
        // exactly the guarantee this feature exists to make true.
        try
        {
            await PatientExportLedger.RecordAsync(
                _auditEntries,
                _unitOfWork,
                _auditActor.Current,
                clinicId.Value,
                rows.Count,
                PatientExportLedger.DescribeFilters(
                    request.SearchTerm, request.CreatedFrom, request.CreatedTo, request.FlaggedOnly),
                DateTime.UtcNow,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Refused a patient CSV export for clinic {ClinicId}: the ledger row failed.", clinicId.Value);
            return Result<IReadOnlyList<PatientDto>>.Failure(
                PatientExportLedger.UnrecordableMessage, PatientExportLedger.UnrecordableCode);
        }

        return Result<IReadOnlyList<PatientDto>>.Success(rows);
    }
}

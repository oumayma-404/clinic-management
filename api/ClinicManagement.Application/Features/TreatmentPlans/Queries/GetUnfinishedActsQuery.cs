using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.TreatmentPlans.Queries;

/// <summary>
/// « Suites à planifier » — every act a dentist marked « non terminé » that nothing has picked up yet.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>It exists because « Traitements en cours » structurally cannot see these acts.</b> That list reads
/// treatment plans, so it shows an unfinished act the moment the act is on a devis — and an act done as a
/// one-off, left unfinished, has no devis by definition. That is precisely the case the whole continuation
/// feature is about, and it was the one case no worklist in the product could surface: the dentist had to
/// remember, at the moment of booking, that the séance of three weeks ago was left half done.
/// </para>
/// <para>
/// ⚠️ <b>It is a statement, not a question — the opposite of <c>GetContinuableActsQuery</c>.</b> That one offers
/// every recent act because nothing can know which was unfinished; this one carries only what a human ticked.
/// So the same flag is a <i>sort</i> there and a <i>filter</i> here, and neither is the other's bug.
/// </para>
/// <para>
/// ⚠️ <b>« Already being continued » is asked of <c>ContinuationTracking</c> and never of the flag.</b> The tick
/// is never cleared automatically — it records what the dentist saw that day, and un-ticking it behind their
/// back would rewrite the clinical record to match a booking — so a list keyed on the flag alone would keep
/// chasing work that is already on a devis, and its own button would then mint a second devis over it. The two
/// questions have different owners and this reads both.
/// </para>
/// <para>
/// ⚠️ <b>It lives under <c>Features/TreatmentPlans</c></b>, like « Traitements en cours » and for the same
/// mechanical reason: <c>RealtimeResourceResolver</c> derives the broadcast key from the namespace, so a folder
/// of its own would emit a key <c>clinic-hub.ts</c> does not declare and <c>RealtimeResourceResolverTests</c>
/// would fail the build in both directions.
/// </para>
/// <para>
/// Paged, like every list read. Ask for page 1 of size 1 and read <c>TotalCount</c> to render a chip: the total
/// is exact whatever page was requested, so a chip and the list it opens cannot disagree.
/// </para>
/// </remarks>
public class GetUnfinishedActsQuery : IRequest<Result<PagedResult<UnfinishedActDto>>>
{
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public class GetUnfinishedActsQueryHandler
    : IRequestHandler<GetUnfinishedActsQuery, Result<PagedResult<UnfinishedActDto>>>
{
    private readonly IDentalRecordRepository _recordRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetUnfinishedActsQueryHandler> _logger;

    public GetUnfinishedActsQueryHandler(
        IDentalRecordRepository recordRepository,
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetUnfinishedActsQueryHandler> logger)
    {
        _recordRepository = recordRepository;
        _planRepository = planRepository;
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<PagedResult<UnfinishedActDto>>> Handle(
        GetUnfinishedActsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PagedResult<UnfinishedActDto>>.FailureFrom(clinicResult);
            }
            var clinicId = clinicResult.Value;
            var paging = PageRequest.From(request.Page, request.PageSize);

            // Tunisian midnight, not UTC's — a séance recorded at 00:30 local belongs to the day that just
            // began. The window is `ContinuationTracking`'s, shared with the dialog that offers these acts, so
            // this list cannot chase an act that list would no longer offer.
            var since = ClinicClock.StartOfLocalDayUtc(
                ClinicClock.ClinicToday().AddDays(-ContinuationTracking.LookbackDays));

            var records = await _recordRepository.GetWithUnfinishedActsAsync(clinicId, since, cancellationToken);
            if (records.Count == 0)
            {
                return Result<PagedResult<UnfinishedActDto>>.Success(PagedResult<UnfinishedActDto>.Empty(paging));
            }

            // Which of those fiches a devis already speaks for — the one owner of that rule, asked over the
            // whole set in a single read rather than once per row.
            var recordIds = records.Select(r => r.Id).ToList();
            var plans = await _planRepository.GetByLinkedDentalRecordsAsync(
                clinicId, recordIds, cancellationToken);
            var tracked = ContinuationTracking.TrackedRecordIds(plans);

            var open = records.Where(r => !tracked.Contains(r.Id)).ToList();
            if (open.Count == 0)
            {
                return Result<PagedResult<UnfinishedActDto>>.Success(PagedResult<UnfinishedActDto>.Empty(paging));
            }

            // Which fiches are already on a note, and what is still owed on it. `InvoiceLinkChoice.ByKey`
            // filters cancelled notes and keeps draft ones — a Draft still collects, and has no number, which
            // is why the row branches on the ID and never on the number.
            var invoiceLinks = InvoiceLinkChoice.ByKey(
                (await _invoiceRepository.GetDentalRecordLinksAsync(clinicId, cancellationToken))
                    .Select(l => (l.DentalRecordId, l.InvoiceId, l.Number, l.Status)));

            var outstandingByInvoice = new Dictionary<Guid, decimal>();
            foreach (var invoiceId in open
                .Where(r => invoiceLinks.ContainsKey(r.Id))
                .Select(r => invoiceLinks[r.Id].InvoiceId)
                .Distinct())
            {
                var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, cancellationToken);
                if (invoice != null && invoice.ClinicId == clinicId)
                {
                    outstandingByInvoice[invoiceId] = invoice.Outstanding;
                }
            }

            /*
             * The rows, flattened to one per (fiche, act) — a séance can hold two unfinished acts and they are
             * two separate pieces of work to book.
             *
             * Ordered oldest FIRST, which is the opposite of every other list in this area and is the point of
             * this one: `continuable-acts` answers « which séance am I continuing? » about a patient in front of
             * you, so the last one wins; this answers « what has been forgotten? », and the answer is the thing
             * that has been waiting longest. `ActId` last so `OFFSET` cannot show one act on two pages.
             */
            var rows = open
                .SelectMany(record => record.Acts
                    .Where(a => a.IsUnfinished)
                    .Select(act => new { Record = record, Act = act }))
                .OrderBy(x => x.Record.InterventionDate)
                .ThenBy(x => x.Act.Id)
                .ToList();

            var page = PagedResult<(Guid RecordId, Guid ActId)>.FromSource(
                rows.Select(x => (x.Record.Id, x.Act.Id)).ToList(), paging);

            var onPage = page.Items
                .Select(key => rows.First(x => x.Record.Id == key.RecordId && x.Act.Id == key.ActId))
                .ToList();

            // Names and bookings resolved over the PAGE only — the whole point of paging is that the reads
            // behind a row are proportional to the rows drawn, not to the rows matched.
            var patientIds = onPage.Select(x => x.Record.PatientId).Distinct().ToList();
            var patients = await _patientRepository.GetByIdsAsync(clinicId, patientIds, cancellationToken);

            /*
             * A séance already booked for the patient, so the row can say « un rendez-vous est déjà prévu »
             * rather than sending somebody to book a second one.
             *
             * ⚠️ Per PATIENT, never per act: nothing links a booking to an act that has no treatment behind it,
             * which is exactly what these acts are. The DTO's own note says the row must be worded to match —
             * claiming « cet acte est booké » from this evidence is how a worklist starts lying.
             *
             * `TreatmentPlanWorkflowProjection.IsLive` is the product's one answer to « does this appointment
             * still book anything », so a cancelled or missed séance correctly leaves the row asking again.
             */
            // ⚠️ Plain UTC, and deliberately not routed through `ClinicClock`: « is this slot still to come » is
            // an instant comparison, not a question about the clinic's day. `ClinicClock` is what the lookback
            // window above needs — a date boundary — and using it here would answer a different question.
            // `GetVisitsToCloseQuery` draws its `nowUtc` the same way.
            var nowUtc = DateTime.UtcNow;
            var upcoming = (await _appointmentRepository.GetByClinicIdAsync(
                    clinicId, startDate: nowUtc, endDate: null, cancellationToken: cancellationToken))
                .Where(a => a.PatientId.HasValue
                            && patientIds.Contains(a.PatientId.Value)
                            && TreatmentPlanWorkflowProjection.IsLive(a.Status))
                .GroupBy(a => a.PatientId!.Value)
                .ToDictionary(g => g.Key, g => g.Min(a => a.AppointmentDateTime));

            var items = onPage.Select(x =>
            {
                var billed = invoiceLinks.TryGetValue(x.Record.Id, out var link);
                return new UnfinishedActDto
                {
                    DentalRecordId = x.Record.Id,
                    ActId = x.Act.Id,
                    PatientId = x.Record.PatientId,
                    // Null rather than "" when the patient could not be read — « je ne sais pas », which the row
                    // renders as a placeholder instead of a nameless row nobody can act on.
                    PatientName = patients.TryGetValue(x.Record.PatientId, out var patient)
                        ? patient.GetFullName()
                        : null,
                    InterventionDate = x.Record.InterventionDate,
                    ProcedureName = x.Act.ProcedureName,
                    ProcedureTypeId = x.Act.ProcedureTypeId,
                    ToothNumbers = x.Act.ToothNumbers.ToList(),
                    Cost = x.Act.Cost,
                    InvoiceId = billed ? link.InvoiceId : null,
                    InvoiceNumber = billed ? link.Number : null,
                    InvoiceOutstanding = billed
                        ? outstandingByInvoice.GetValueOrDefault(link.InvoiceId)
                        : 0m,
                    NextAppointmentAt = upcoming.TryGetValue(x.Record.PatientId, out var at) ? at : null,
                };
            }).ToList();

            return Result<PagedResult<UnfinishedActDto>>.Success(
                new PagedResult<UnfinishedActDto>(items, page.Page, page.PageSize, page.TotalCount));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reading unfinished acts");
            return Result<PagedResult<UnfinishedActDto>>.Failure(
                "Erreur lors du chargement des suites à planifier.");
        }
    }
}

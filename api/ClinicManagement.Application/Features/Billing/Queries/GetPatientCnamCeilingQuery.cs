using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Billing.Queries;

/// <summary>
/// « Plafond annuel CNAM » for one patient (L10) — the ceiling, what this clinic has consumed of it this year, and
/// what is left.
///
/// <para><b>Why it exists.</b> <c>CnamReimbursementCalculator.Estimate</c> is <c>coefficient × VLC × rate</c> with
/// no cap and no memory, so « Remboursement indicatif » told a patient who had exhausted their ceiling in March
/// exactly what it told one who had never claimed. The disclaimer beside it named only the age band.</para>
///
/// <para><b>Consumption is measured from this clinic's issued invoices</b>, because that is the only place an act's
/// money exists: the product records no BS1 submission carrying an amount. Draft invoices are excluded (nothing has
/// been claimed) and cancelled ones are void — the same rule « Solde patient » applies, and deliberately so: a
/// patient reading two figures on one screen must not find them derived from two different sets of invoices.</para>
///
/// <para>⚠️ <b>The year is the clinic's, through <see cref="ClinicClock"/>.</b> A ceiling is annual and a document's
/// year is its legal identity here — the same reason the three money-document sequences take their year from
/// <c>ClinicClock.ClinicYear()</c>. A UTC year boundary would file a 1 January 00:30 Tunis invoice into the year
/// that had just closed and reset a ceiling an hour early.</para>
///
/// <para>⚠️ <b>Per-patient, so it is deliberately reachable by reception</b> (<c>AnyClinicRole</c> via the
/// controller's class policy) — exactly like « Solde patient ». The rule I1 draws is per-patient money yes,
/// clinic-wide aggregates no, and « combien reste-t-il à ce patient ? » is asked at the desk while the patient is
/// standing there.</para>
/// </summary>
public class GetPatientCnamCeilingQuery : IRequest<Result<CnamCeilingDto>>
{
    public Guid PatientId { get; set; }

    /// <summary>
    /// The year to report on. Omit for the current clinic year — which is what every caller does; it exists so a
    /// reader can check last year's consumption without the read having to guess what « annual » meant then.
    /// </summary>
    public int? Year { get; set; }
}

public class GetPatientCnamCeilingQueryHandler : IRequestHandler<GetPatientCnamCeilingQuery, Result<CnamCeilingDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICnamBillingCalculator _cnamBillingCalculator;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetPatientCnamCeilingQueryHandler> _logger;

    public GetPatientCnamCeilingQueryHandler(
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICnamBillingCalculator cnamBillingCalculator,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetPatientCnamCeilingQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _cnamBillingCalculator = cnamBillingCalculator;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<CnamCeilingDto>> Handle(
        GetPatientCnamCeilingQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<CnamCeilingDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
            {
                throw new NotFoundException("Patient introuvable.");
            }

            var year = request.Year ?? ClinicClock.ClinicYear();
            // The clinic's own year, as an inclusive UTC range. `LastTickOfLocalDayUtc` and not the next midnight:
            // the invoice filter is inclusive on both ends, so the exclusive bound would count a 31 December
            // 00:00-next-day invoice in both years (finding #20).
            var from = ClinicClock.StartOfLocalDayUtc(new DateTime(year, 1, 1));
            var to = ClinicClock.LastTickOfLocalDayUtc(new DateTime(year, 12, 31));

            // Unpaged deliberately — this is a total for the year, which the paging primitive models as a
            // first-class case rather than as one huge page.
            var invoices = (await _invoiceRepository.GetFilteredAsync(
                    clinicId, from, to, request.PatientId, cancellationToken: cancellationToken)).Items
                .Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Cancelled)
                .ToList();

            var consuming = 0m;
            var horsPlafond = 0m;

            foreach (var invoice in invoices)
            {
                var lines = invoice.Lines
                    .Select(l => new CnamBillingLine(l.DentalActCodeId, l.LineTotalHt))
                    .ToList();

                // Dated per invoice, not once for the year: the reimbursement rate turns on the patient's age at
                // the care date (70 % for 4–18), so a patient who turned 19 in June has two rates in one year and
                // one care date for the lot would misprice half of it.
                var careDate = invoice.IssueDate ?? invoice.CreatedAt;
                var consumption = await _cnamBillingCalculator.ComputeCeilingConsumptionAsync(
                    lines, patient.DateOfBirth, careDate, cancellationToken);

                consuming += consumption.Consuming;
                horsPlafond += consumption.HorsPlafond;
            }

            var dependants = patient.CnamInfo?.DependantCount ?? 0;
            var custom = patient.CnamInfo?.AnnualCeilingOverride;
            var ceiling = CnamPlafond.EffectiveCeiling(dependants, custom);
            // `EffectiveCeiling` ignores a non-positive override, so « was a real override used? » has to be asked
            // the same way it does rather than by `custom is not null` — otherwise a 0 typed into the field would
            // make the screen claim a recorded ceiling while showing a computed one.
            var usedOverride = custom is { } value && value > 0m;

            consuming = InvoiceCalculator.RoundMoney(consuming);
            horsPlafond = InvoiceCalculator.RoundMoney(horsPlafond);

            return Result<CnamCeilingDto>.Success(new CnamCeilingDto
            {
                Year = year,
                Ceiling = InvoiceCalculator.RoundMoney(ceiling),
                BaseCeiling = usedOverride ? null : InvoiceCalculator.RoundMoney(CnamPlafond.BaseCeiling(dependants)),
                DentalAllowance = usedOverride ? null : CnamPlafond.DentalAllowance,
                DependantCount = dependants,
                CeilingIsDefault = !usedOverride,
                Consumed = consuming,
                HorsPlafond = horsPlafond,
                // Floored: a ceiling has no negative remainder, and « −80,000 DT » on a patient screen reads as a
                // debt to CNAM rather than as « exhausted ». `Exhausted` is what says that.
                Remaining = InvoiceCalculator.RoundMoney(Math.Max(0m, ceiling - consuming)),
                Exhausted = consuming >= ceiling,
                InvoiceCount = invoices.Count
            });
        }
        catch (Exception ex) when (ex is not ConflictException and not NotFoundException)
        {
            _logger.LogError(ex, "Error computing the CNAM ceiling for patient {PatientId}", request.PatientId);
            return Result<CnamCeilingDto>.Failure("Erreur lors du calcul du plafond CNAM.");
        }
    }
}

using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Clinics.Queries;

/// <summary>What the reminder editor shows while the doctor types: the text a sample patient would receive, and the count.</summary>
public sealed record ReminderPreviewDto
{
    /// <summary>The reminder as a sample patient would receive it, for this clinic, tomorrow at 14:30.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// What the save holds to the limit: the doctor's text plus the date, the clinic's name and the room kept for a
    /// name and the acts. The figure the counter shows — not the sample's own length, which shrinks to fit.
    /// </summary>
    public required int UsedLength { get; init; }

    public required int MaxLength { get; init; }

    /// <summary>The refusal the save would answer with, or null when this wording may be saved.</summary>
    public string? Refusal { get; init; }

    public string? RefusalCode { get; init; }
}

/// <summary>
/// Renders a draft reminder wording through the same <see cref="ReminderText"/> the dispatcher sends with, and judges
/// it with the same <see cref="ReminderTemplateRules"/> the save applies — so the editor's preview and counter cannot
/// disagree with what is saved or sent. Writes nothing.
/// </summary>
public sealed class PreviewReminderTextQuery : IRequest<Result<ReminderPreviewDto>>
{
    public string? Template { get; init; }
}

public sealed class PreviewReminderTextQueryHandler
    : IRequestHandler<PreviewReminderTextQuery, Result<ReminderPreviewDto>>
{
    /// <summary>A long, ordinary name, so the preview shows what a real one costs.</summary>
    private const string SampleFirstName = "Mohamed";
    private const string SampleLastName = "Ben Abdallah";
    private static readonly IReadOnlyList<string> SampleActs = ["Détartrage", "Obturation composite"];

    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicRepository _clinics;

    public PreviewReminderTextQueryHandler(ICurrentClinicResolver clinicResolver, IClinicRepository clinics)
    {
        _clinicResolver = clinicResolver;
        _clinics = clinics;
    }

    public async Task<Result<ReminderPreviewDto>> Handle(
        PreviewReminderTextQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicId = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicId.IsFailure)
            {
                return Result<ReminderPreviewDto>.FailureFrom(clinicId);
            }

            var clinic = await _clinics.GetByIdAsync(clinicId.Value, cancellationToken);
            var clinicName = clinic?.Name ?? string.Empty;

            var tomorrowAt1430 = ClinicClock.ToUtc(ClinicClock.ClinicToday().AddDays(1).AddHours(14).AddMinutes(30));
            var rendered = ReminderText.Render(new ReminderTextInput(
                request.Template, SampleFirstName, SampleLastName, tomorrowAt1430, SampleActs, clinicName));
            var refusal = ReminderTemplateRules.CheckTemplate(request.Template, clinicName);

            return Result<ReminderPreviewDto>.Success(new ReminderPreviewDto
            {
                Text = rendered.Text,
                UsedLength = ReminderText.WorstCaseLength(request.Template, clinicName),
                MaxLength = ReminderText.SmsMaxLength,
                Refusal = refusal?.Sentence,
                RefusalCode = refusal?.Code,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<ReminderPreviewDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

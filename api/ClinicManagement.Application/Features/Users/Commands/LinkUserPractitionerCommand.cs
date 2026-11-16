using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Users.Commands;

/// <summary>
/// « Lier à un praticien » (<c>cabinet-de-groupe</c> US-3): an admin or doctor account with no practitioner record
/// gets one — a record already on the roster, linked, or a new one created from three fields.
///
/// <para>It repairs the accounts the two old doors left without one: an admin dentist invited before an admin could
/// carry a practitioner, and a dentist whose roster record was added in Paramètres before their account existed.
/// Without a record they print no cachet and no n° d'ordre, and their work is credited to nobody.</para>
///
/// <para>⚠️ An account that already has a record is refused, never re-pointed: moving a link would take one
/// record's history away from the person it was recorded for. Merging two records is not offered at all.</para>
/// </summary>
public class LinkUserPractitionerCommand : IRequest<Result>
{
    public string TargetUserId { get; set; } = string.Empty;

    /// <summary>A practitioner of the roster to link — this cabinet's, active, linked to nobody.</summary>
    public Guid? ExistingDoctorId { get; set; }

    /// <summary>Or the three fields of a new practitioner record.</summary>
    public DoctorPersonalInfoDto? DoctorInfo { get; set; }
}

public class LinkUserPractitionerCommandHandler : IRequestHandler<LinkUserPractitionerCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IDoctorRepository _doctors;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LinkUserPractitionerCommandHandler> _logger;

    public LinkUserPractitionerCommandHandler(
        IUserRepository users,
        IDoctorRepository doctors,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<LinkUserPractitionerCommandHandler> logger)
    {
        _users = users;
        _doctors = doctors;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(LinkUserPractitionerCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result.Failure("Session invalide, veuillez vous reconnecter.");
            }

            // The class policy is AdminOnly, and the DB role is the authoritative one.
            var admin = await _users.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin is null || !admin.IsAdmin())
            {
                return Result.Failure("Seuls les administrateurs peuvent lier un compte à un praticien.");
            }

            var target = await _users.GetByIdAsync(request.TargetUserId, cancellationToken);
            if (target is null || target.ClinicId != admin.ClinicId)
            {
                return Result.Failure("Utilisateur introuvable.");
            }
            if (target.IsSecretary())
            {
                return Result.Failure(PractitionerLink.NotAPractitionerMessage);
            }
            // Checked here rather than left to the unique index on Doctors.UserId, which would answer a 500.
            if (await _doctors.GetByUserIdAsync(target.Id, cancellationToken) is not null)
            {
                return Result.Failure(PractitionerLink.AlreadyLinkedMessage, PractitionerLink.AlreadyLinkedCode);
            }

            if (request.ExistingDoctorId.HasValue)
            {
                var linkable = await PractitionerLink.FindLinkableAsync(
                    _doctors, admin.ClinicId, request.ExistingDoctorId.Value, cancellationToken);
                if (linkable.IsFailure)
                {
                    return Result.Failure(linkable.Error!, linkable.Code);
                }

                linkable.Value!.LinkToUser(target.Id);
                _doctors.Update(linkable.Value);
            }
            else if (PractitionerLink.HasPractitioner(request.DoctorInfo))
            {
                var info = request.DoctorInfo!;
                var doctor = new Doctor(
                    Guid.NewGuid(),
                    admin.ClinicId,
                    info.FirstName.Trim(),
                    info.LastName.Trim(),
                    info.Specialty.Trim(),
                    info.Phone,
                    target.Email);
                doctor.LinkToUser(target.Id);
                await _doctors.AddAsync(doctor, cancellationToken);
            }
            else
            {
                return Result.Failure(
                    "Choisissez un praticien existant, ou renseignez le prénom, le nom et la spécialité.");
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Linking an account to a practitioner failed.");
            return Result.Failure("Le praticien n'a pas pu être lié.", ex);
        }
    }
}

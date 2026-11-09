using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Commands;

/// <summary>
/// « Schéma de pointage » in Mon profil and on the lock: her own schéma, drawn twice on the client. Hashed like a
/// password — it is what proves at the borne that the tile tapped is hers. Replacing one asks her account password:
/// the lock may be showing on a shared reception PC under her session, and a colleague must not redraw it.
/// </summary>
public class SetMyAttendanceSchemaCommand : IRequest<Result<AttendancePersonSettingDto>>
{
    public List<int>? Pattern { get; set; }

    /// <summary>Her account password — required once she has a schéma, and counted like a sign-in when wrong.</summary>
    public string? CurrentPassword { get; set; }
}

public class SetMyAttendanceSchemaCommandHandler : IRequestHandler<SetMyAttendanceSchemaCommand, Result<AttendancePersonSettingDto>>
{
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly ILocalAuthService _hasher;
    private readonly ILoginAttemptTracker _attempts;
    private readonly IUnitOfWork _unitOfWork;

    public SetMyAttendanceSchemaCommandHandler(
        IUserRepository users, IClinicRepository clinics, IClinicContext clinicContext, ILocalAuthService hasher,
        ILoginAttemptTracker attempts, IUnitOfWork unitOfWork)
    {
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _hasher = hasher;
        _attempts = attempts;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AttendancePersonSettingDto>> Handle(SetMyAttendanceSchemaCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await AttendanceRules.ResolveAsync(_clinicContext, _users, _clinics, adminOnly: false, cancellationToken);
            if (resolved.IsFailure)
                return Result<AttendancePersonSettingDto>.FailureFrom(resolved);
            var (caller, _) = resolved.Value;
            if (!caller.IsAttendanceTracked)
                return Result<AttendancePersonSettingDto>.Failure(AttendanceRules.NotTrackedSelf);
            if (AttendanceRules.SchemaRefusalFor(request.Pattern) is { } refusal)
                return Result<AttendancePersonSettingDto>.Failure(refusal);

            // Her first schéma needs nothing more: there is nothing yet a colleague could take over.
            if (caller.HasAttendanceSchema)
            {
                if (string.IsNullOrEmpty(request.CurrentPassword))
                    return Result<AttendancePersonSettingDto>.Failure(
                        AttendanceRules.SchemaPasswordRequired, AttendanceRules.SchemaPasswordRequiredCode);

                // The sign-in's own two brakes, checked before the password so a guess here is stopped like one there.
                if (_attempts.IsLockedOutForCurrentSource(caller.Id) || caller.IsLockedOut())
                    return Result<AttendancePersonSettingDto>.Failure(
                        LoginCommandHandler.LockedOutError, ClinicAuthRefusals.TooManyAttempts);

                if (!caller.IsLocalAccount()
                    || _hasher.VerifyPassword(caller.PasswordHash!, request.CurrentPassword) == PasswordVerificationOutcome.Failed)
                {
                    _attempts.RecordFailure(caller.Id);
                    caller.RecordFailedLogin();
                    _users.Update(caller);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    return Result<AttendancePersonSettingDto>.Failure(
                        AttendanceRules.SchemaPasswordWrong, AttendanceRules.SchemaPasswordWrongCode);
                }
            }

            caller.SetAttendanceSchema(_hasher.HashPassword(AttendanceRules.PatternText(request.Pattern!)));
            _users.Update(caller);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<AttendancePersonSettingDto>.Success(AttendanceRules.ToSetting(caller, DateTime.UtcNow));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<AttendancePersonSettingDto>.Failure("Erreur lors de l'enregistrement du schéma.", ex);
        }
    }
}

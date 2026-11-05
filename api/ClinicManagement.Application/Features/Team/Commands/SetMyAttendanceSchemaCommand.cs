using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Commands;

/// <summary>
/// « Schéma de pointage » in Mon profil: her own schéma, drawn twice on the client. Hashed like a password — it is
/// what proves at the borne that the tile tapped is hers.
/// </summary>
public class SetMyAttendanceSchemaCommand : IRequest<Result<AttendancePersonSettingDto>>
{
    public List<int>? Pattern { get; set; }
}

public class SetMyAttendanceSchemaCommandHandler : IRequestHandler<SetMyAttendanceSchemaCommand, Result<AttendancePersonSettingDto>>
{
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly ILocalAuthService _hasher;
    private readonly IUnitOfWork _unitOfWork;

    public SetMyAttendanceSchemaCommandHandler(
        IUserRepository users, IClinicRepository clinics, IClinicContext clinicContext, ILocalAuthService hasher,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _hasher = hasher;
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

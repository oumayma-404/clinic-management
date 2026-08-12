using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Auth.Queries;

/// <summary>
/// What « Sécurité » shows about this account's second factor
/// (<c>hosted-security-hardening</c> FR-1.5).
/// </summary>
/// <param name="IsEnrolled">Whether a factor is set up and confirmed.</param>
/// <param name="IsRequired">
/// Whether this account is <b>obliged</b> to hold one — the deployment requires it of administrators and this
/// account is one.
///
/// <para>⚠️ It carries the deployment's answer rather than the role alone, so the screen's wording follows the
/// same rule the refusal does. A voluntarily-enrolled administrator on a profile that requires nothing is
/// <c>IsEnrolled</c> without being <c>IsRequired</c>, and may disable theirs.</para>
/// </param>
/// <param name="RecoveryCodesRemaining">Unused codes left. Null when nothing is enrolled.</param>
/// <param name="EnrolledAt">When the current factor was confirmed.</param>
public record TotpStateDto(bool IsEnrolled, bool IsRequired, int? RecoveryCodesRemaining, DateTime? EnrolledAt);

public class GetTotpStateQuery : IRequest<Result<TotpStateDto>>;

public class GetTotpStateQueryHandler : IRequestHandler<GetTotpStateQuery, Result<TotpStateDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly ISecondFactorPolicy _secondFactorPolicy;

    public GetTotpStateQueryHandler(
        IClinicContext clinicContext,
        IUserRepository userRepository,
        ISecondFactorPolicy secondFactorPolicy)
    {
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _secondFactorPolicy = secondFactorPolicy;
    }

    public async Task<Result<TotpStateDto>> Handle(GetTotpStateQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Result<TotpStateDto>.Failure(ErrorMessages.Generic);
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user is null)
            {
                return Result<TotpStateDto>.Failure(ErrorMessages.Generic);
            }

            return Result<TotpStateDto>.Success(new TotpStateDto(
                IsEnrolled: user.IsTotpEnrolled,
                IsRequired: _secondFactorPolicy.RequiresAdminSecondFactor && user.IsAdmin(),
                RecoveryCodesRemaining: user.IsTotpEnrolled ? user.UnusedRecoveryCodeCount : null,
                EnrolledAt: user.TotpEnrolledAt));
        }
        catch (Exception)
        {
            return Result<TotpStateDto>.Failure(ErrorMessages.Generic);
        }
    }
}

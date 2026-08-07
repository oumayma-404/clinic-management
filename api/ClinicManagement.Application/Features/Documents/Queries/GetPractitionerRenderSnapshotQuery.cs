using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Documents.Queries;

/// <summary>
/// Resolves the current practitioner's cachet + CNOMDT ordre and the cabinet city for the immediate
/// "Download" PDF path (Part C, FR-3.2/FR-3.3/FR-6.1). The download endpoint builds its render model on the
/// frontend, which cannot know the server-side cachet storage key — so the controller overlays these
/// authoritative values before rendering, keeping the downloaded copy identical to the stored one (which
/// the background job renders from the ContentJson snapshot).
/// </summary>
public class GetPractitionerRenderSnapshotQuery : IRequest<Result<PractitionerRenderSnapshotDto>>
{
    /// <summary>
    /// The practitioner the document names, when the editor chose one. Resolved ahead of the caller's own doctor
    /// record and tenant-checked (<c>PractitionerRenderSnapshot.ResolveAsync</c>) — so the previewed and printed
    /// copy carries the named practitioner's cachet, not the cachet of whoever pressed the button.
    /// </summary>
    public Guid? IssuingDoctorId { get; set; }
}

public class PractitionerRenderSnapshotDto
{
    public string? ClinicCity { get; set; }
    public string? ClinicEmail { get; set; }
    public string? DoctorOrdreNumber { get; set; }
    public string? DoctorCachetKey { get; set; }
    public string? DoctorCachetContentType { get; set; }
}

public class GetPractitionerRenderSnapshotQueryHandler
    : IRequestHandler<GetPractitionerRenderSnapshotQuery, Result<PractitionerRenderSnapshotDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IClinicContext _clinicContext;

    public GetPractitionerRenderSnapshotQueryHandler(
        IUserRepository userRepository,
        IDoctorRepository doctorRepository,
        IClinicRepository clinicRepository,
        IClinicContext clinicContext)
    {
        _userRepository = userRepository;
        _doctorRepository = doctorRepository;
        _clinicRepository = clinicRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<PractitionerRenderSnapshotDto>> Handle(
        GetPractitionerRenderSnapshotQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<PractitionerRenderSnapshotDto>.Failure("Utilisateur non authentifié.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<PractitionerRenderSnapshotDto>.Failure("Utilisateur introuvable.");
            }

            var snapshot = await PractitionerRenderSnapshot.ResolveAsync(
                request.IssuingDoctorId, userId, user.ClinicId,
                _doctorRepository, _clinicRepository, cancellationToken);

            return Result<PractitionerRenderSnapshotDto>.Success(new PractitionerRenderSnapshotDto
            {
                ClinicCity = snapshot.ClinicCity,
                ClinicEmail = snapshot.ClinicEmail,
                DoctorOrdreNumber = snapshot.DoctorOrdreNumber,
                DoctorCachetKey = snapshot.DoctorCachetKey,
                DoctorCachetContentType = snapshot.DoctorCachetContentType
            });
        }
        catch (Exception)
        {
            // Best-effort per the download contract: the sole caller guards on IsSuccess and renders without
            // the overlay on failure, so a transient DB error must surface as a failed Result — never a throw
            // that aborts the whole PDF download.
            return Result<PractitionerRenderSnapshotDto>.Failure("Impossible de résoudre les informations du praticien.");
        }
    }
}

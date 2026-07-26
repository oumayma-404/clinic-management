using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Cloud admin backfill (see <see cref="IClinicAdminBackfill"/>). Runs at startup with no clinic in scope,
/// so the global query filter is inactive and it reads across all clinics. For each clinic with no active
/// admin, promotes its earliest active user (the creator) to "admin" and pushes the role to Auth0
/// (best-effort). Idempotent: a clinic that already has an active admin is skipped.
/// </summary>
public class ClinicAdminBackfill : IClinicAdminBackfill
{
    private readonly ApplicationDbContext _context;
    private readonly IAuth0ManagementService _auth0ManagementService;
    private readonly ILogger<ClinicAdminBackfill> _logger;

    public ClinicAdminBackfill(
        ApplicationDbContext context,
        IAuth0ManagementService auth0ManagementService,
        ILogger<ClinicAdminBackfill> logger)
    {
        _context = context;
        _auth0ManagementService = auth0ManagementService;
        _logger = logger;
    }

    public async Task BackfillAsync(CancellationToken cancellationToken = default)
    {
        var clinicIds = await _context.Clinics.Select(c => c.Id).ToListAsync(cancellationToken);
        var repaired = 0;

        foreach (var clinicId in clinicIds)
        {
            var users = await _context.Users
                .Where(u => u.ClinicId == clinicId)
                .OrderBy(u => u.CreatedAt)
                .ToListAsync(cancellationToken);

            if (users.Count == 0)
            {
                continue; // orphan clinic — nothing to promote
            }
            if (users.Any(u => u.IsActive && u.IsAdmin()))
            {
                continue; // already has an active admin
            }

            // Prefer the earliest active user (the creator); fall back to the earliest user overall.
            var promote = users.FirstOrDefault(u => u.IsActive) ?? users[0];
            promote.PromoteToAdmin();
            await _context.SaveChangesAsync(cancellationToken);
            repaired++;

            try
            {
                await _auth0ManagementService.UpdateUserMetadataAsync(promote.Id, clinicId, "admin", cancellationToken);
            }
            catch (Exception ex)
            {
                // Best-effort, matching the app's Auth0-metadata convention — never fail the backfill.
                _logger.LogWarning(ex, "Auth0 metadata push failed promoting {UserId} in clinic {ClinicId}", promote.Id, clinicId);
            }
        }

        if (repaired > 0)
        {
            _logger.LogInformation("Cloud admin backfill promoted an admin in {Count} clinic(s).", repaired);
        }
        else
        {
            _logger.LogInformation("Cloud admin backfill: every clinic already has an active admin.");
        }
    }
}

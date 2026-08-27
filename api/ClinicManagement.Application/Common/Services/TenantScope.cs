using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// Scoped, single-assignment <see cref="ITenantScope"/>. Registered in <c>AddInfrastructure</c> rather than
/// <c>AddApplication</c> so the console verbs — which build their container from that method alone — can
/// declare themselves cross-clinic instead of reading everything by accident.
/// </summary>
public class TenantScope : ITenantScope
{
    private readonly ILogger<TenantScope> _logger;

    public TenantScope(ILogger<TenantScope> logger)
    {
        _logger = logger;
    }

    public TenantScopeKind Kind { get; private set; } = TenantScopeKind.Unset;

    public Guid? ClinicId { get; private set; }

    public string? SystemWideReason { get; private set; }

    public void UseClinic(Guid clinicId)
    {
        if (clinicId == Guid.Empty)
        {
            // Guid.Empty is what the filter compares against when nothing was set, so accepting it here would
            // make "scoped to the empty clinic" and "unscoped" indistinguishable in the generated SQL.
            throw new ArgumentException("Un identifiant de clinique vide ne peut pas définir la portée.", nameof(clinicId));
        }

        switch (Kind)
        {
            case TenantScopeKind.Clinic when ClinicId == clinicId:
                return;
            case TenantScopeKind.Clinic:
                throw new InvalidOperationException(
                    $"La portée est déjà limitée à la clinique {ClinicId}; elle ne peut pas passer à {clinicId}.");
            case TenantScopeKind.SystemWide:
                throw new InvalidOperationException(
                    $"La portée est déjà inter-cliniques ({SystemWideReason}); elle ne peut pas être restreinte.");
        }

        Kind = TenantScopeKind.Clinic;
        ClinicId = clinicId;
    }

    public void UseSystemWide(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Une lecture inter-cliniques doit indiquer sa raison.", nameof(reason));
        }

        switch (Kind)
        {
            case TenantScopeKind.SystemWide:
                return;
            case TenantScopeKind.Clinic:
                throw new InvalidOperationException(
                    $"La portée est déjà limitée à la clinique {ClinicId}; elle ne peut pas être élargie ({reason}).");
        }

        Kind = TenantScopeKind.SystemWide;
        SystemWideReason = reason;
        _logger.LogInformation("Lecture inter-cliniques : {Reason}", reason);
    }
}

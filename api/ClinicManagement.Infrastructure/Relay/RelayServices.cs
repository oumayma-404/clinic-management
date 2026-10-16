using System.Reflection;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>This server's build: its newest migration plus the entry assembly's informational version (D10).</summary>
public sealed class RelayBuildInfo : IRelayBuildInfo
{
    private static readonly Lazy<string> Value = new(Compute);

    public string Current => Value.Value;

    private static string Compute()
    {
        var migration = typeof(ApplicationDbContext).Assembly.GetTypes()
            .Select(t => t.GetCustomAttribute<MigrationAttribute>()?.Id)
            .Where(id => id is not null)
            .Max(StringComparer.Ordinal) ?? "none";
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? "dev";
        return $"{migration}+{version}";
    }
}

public sealed class RelayKeyValidator : IRelayKeyValidator
{
    public bool IsValidPublicKey(string? publicKey) => RelaySecretEnvelope.IsValidPublicKey(publicKey);
}

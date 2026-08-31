using System.Text.Json;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// Reads and writes the audit chains' <b>sealed tips</b> — the record of how long each chain was when an
/// operator last confirmed it, held <b>outside the database it protects</b>.
///
/// <para><b>Why this exists.</b> Every check in <see cref="AuditChain.Walk"/> compares an entry against its
/// neighbour, so deleting the newest <i>k</i> rows removes no neighbour: the shortened chain verifies perfectly
/// and the next append re-links from whatever tip it finds. « Delete the last hour » was therefore the cheapest
/// possible attack on this ledger, and it cost nothing. Nothing inside the database can close that, because
/// anyone able to delete the rows can delete the record of them.</para>
///
/// <para>⚠️ <b>It lives beside the chain KEY, and for the same reason.</b> The key is deliberately not in the
/// database and not on the Data Protection ring, so that verification survives a restore; the seal has the
/// identical requirement. On a hosted deployment that means an operator-held path
/// (<c>Audit:ChainSealPath</c>), and on a clinic's own PC the install-local file beside
/// <c>audit-chain-key</c>.</para>
///
/// <para>⚠️ <b>Sealing is an operator ACTION, never automatic.</b> A seal written by the application on every
/// append would be rewritten by the same process an attacker already controls — it would record the truncated
/// tip a moment after the truncation and report clean for ever. <c>seal-audit-chain</c> refuses to seal a chain
/// that does not currently verify, so a seal always means « a person confirmed this chain was intact at this
/// length ».</para>
/// </summary>
public interface IAuditChainSealStore
{
    /// <summary>Every recorded seal, keyed by chain. Empty when nothing has been sealed yet.</summary>
    IReadOnlyDictionary<Guid, AuditChainSeal> Read();

    /// <summary>Replaces the whole record. Callers pass the complete set, never a delta.</summary>
    void Write(IReadOnlyCollection<AuditChainSeal> seals);

    /// <summary>Where the seals are stored — named in the operator's report so it can be backed up.</summary>
    string Location { get; }
}

/// <inheritdoc cref="IAuditChainSealStore"/>
public sealed class AuditChainSealStore : IAuditChainSealStore
{
    public const string ConfigKey = "Audit:ChainSealPath";

    /// <summary>Beside <c>audit-chain-key</c>, for the reason given on the interface.</summary>
    public const string DefaultFileName = "audit-chain-seals.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public AuditChainSealStore(IConfiguration configuration)
    {
        var configured = configuration[ConfigKey];

        Location = string.IsNullOrWhiteSpace(configured)
            ? LocalInstallPaths.LocalFile(DefaultFileName)
            : configured.Trim();
    }

    public string Location { get; }

    public IReadOnlyDictionary<Guid, AuditChainSeal> Read()
    {
        if (!File.Exists(Location))
        {
            return new Dictionary<Guid, AuditChainSeal>();
        }

        // ⚠️ An unreadable seal file is NOT treated as « no seals ». Falling back to an empty set would mean a
        // corrupted — or deliberately truncated — seal file silently restores the exact blindness this closes,
        // and the report would say « chaîne intacte ». Failing loud is the only safe direction here.
        var seals = JsonSerializer.Deserialize<List<AuditChainSeal>>(File.ReadAllText(Location), Json)
                    ?? throw new InvalidOperationException(
                        $"Le fichier de scellement « {Location} » est illisible. Restaurez-le depuis votre "
                        + "sauvegarde ; ne le supprimez pas — un fichier absent rend une troncature invisible.");

        return seals.ToDictionary(s => s.ChainKey);
    }

    public void Write(IReadOnlyCollection<AuditChainSeal> seals)
    {
        var directory = Path.GetDirectoryName(Location);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Written through a temp file and moved into place: a process killed mid-write must not leave a
        // half-parsed seal file, which Read() would then refuse — taking verify-schema down over an
        // interruption rather than over a real finding.
        var temporary = Location + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(seals.OrderBy(s => s.ChainKey), Json));
        File.Move(temporary, Location, overwrite: true);
    }
}

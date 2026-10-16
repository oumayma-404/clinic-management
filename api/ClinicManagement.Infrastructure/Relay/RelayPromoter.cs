using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicManagement.Infrastructure.Deployment;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>How <c>promote-relay</c> ended. The exit code is the contract; the sentence is for the person at the console.</summary>
public sealed record RelayPromotionResult(int ExitCode, string Sentence)
{
    public const int Promoted = 0;
    public const int CannotRun = 1;
    public const int CodeRefused = 2;
    public const int CloudAnswers = 3;
}

/// <summary>What a promoted PC keeps about its promotion, beside its other state in <c>.local/</c>.</summary>
public sealed record RelayPromotionRecord(Guid RelayId, Guid ClinicId, string ClinicName, DateTime PromotedAtUtc, string Nonce);

/// <summary>
/// Turns this PC de secours into the cabinet's local server (<c>clinic-pc-copy</c> D11, AC-9.3): a valid vendor code
/// for THIS PC, a cloud that no longer serves, a journal row, then the deployment kind. Afterwards it is an ordinary
/// <c>SelfHostedLan</c> install — the copy stops being followed because the relay kind is gone, not because of a flag.
///
/// <para>⚠️ <b>Everything that can refuse runs before anything is written</b>, so a refused promotion leaves the PC
/// exactly as it was, still a copy. ⚠️ <b>A cloud that still serves refuses the promotion, whatever the code
/// says</b>: two writable copies of one cabinet is the outcome this whole feature exists to prevent, and a code can be
/// used later than it was meant to be.</para>
/// </summary>
public sealed class RelayPromoter
{
    public const string RecordFileName = "relay-promotion.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _trustedPublicKey;
    private readonly Func<CancellationToken, Task<bool>> _cloudServes;
    private readonly Func<RelayPromotionClaim, DateTime, CancellationToken, Task> _recordInJournal;
    private readonly string _installLayerPath;
    private readonly RelayFollowerStateStore _states;
    private readonly string _localDir;

    public RelayPromoter(
        string trustedPublicKey,
        Func<CancellationToken, Task<bool>> cloudServes,
        Func<RelayPromotionClaim, DateTime, CancellationToken, Task> recordInJournal,
        string installLayerPath,
        RelayFollowerStateStore states,
        string? localDir = null)
    {
        _trustedPublicKey = trustedPublicKey;
        _cloudServes = cloudServes;
        _recordInJournal = recordInJournal;
        _installLayerPath = installLayerPath;
        _states = states;
        _localDir = localDir ?? LocalInstallPaths.LocalDir;
    }

    public async Task<RelayPromotionResult> PromoteAsync(
        RelayCredentials credentials, string code, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var (verdict, claim) = RelayPromotionCode.Verify(code, _trustedPublicKey, credentials.RelayId, credentials.ClinicId, nowUtc);
        if (verdict != RelayPromotionVerdict.Valid)
        {
            return new(RelayPromotionResult.CodeRefused, RelayPromotionCode.Sentence(verdict));
        }

        if (RelayInstallLayer.WhyNotARelay(_installLayerPath) is { } reason)
        {
            return new(RelayPromotionResult.CannotRun, reason);
        }

        if (await _cloudServes(cancellationToken))
        {
            return new(RelayPromotionResult.CloudAnswers,
                "Le cloud du cabinet répond encore : ce PC n'a pas été promu. Une promotion ne se fait que lorsque le "
                + "cloud est perdu — sinon le cabinet travaillerait sur deux serveurs à la fois.");
        }

        // The record first: a promotion nobody can find in the journal afterwards must not happen.
        await _recordInJournal(claim!, nowUtc, cancellationToken);
        RelayInstallLayer.SwitchToLocalServer(_installLayerPath, nowUtc);

        var state = _states.Load();
        _states.Save(state with { Released = true, ReleasedAtUtc = state.ReleasedAtUtc ?? nowUtc });
        AtomicFile.Write(Path.Combine(_localDir, RecordFileName), JsonSerializer.Serialize(
            new RelayPromotionRecord(credentials.RelayId, credentials.ClinicId, credentials.ClinicName, nowUtc, claim!.Nonce), Json));

        return new(RelayPromotionResult.Promoted,
            $"Ce PC est maintenant le serveur local {OfTheCabinet(credentials.ClinicName)}. Redémarrez-le pour qu'il "
            + "fonctionne comme tel. À refaire sur ce serveur : les rappels SMS / WhatsApp, le lien Google Agenda et le "
            + "code du cabinet (Paramètres), ainsi que la copie automatique des archives ; les postes du cabinet doivent "
            + "maintenant se connecter à ce PC.");
    }

    /// <summary>« du cabinet « X » », or « du cabinet » alone — a cabinet may have no name, and « «  » » reads as a fault.</summary>
    public static string OfTheCabinet(string? clinicName) =>
        string.IsNullOrWhiteSpace(clinicName) ? "du cabinet" : $"du cabinet « {clinicName.Trim()} »";

    public static RelayPromotionRecord? LoadRecord(string? localDir = null)
    {
        var path = Path.Combine(localDir ?? LocalInstallPaths.LocalDir, RecordFileName);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<RelayPromotionRecord>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the cloud still serves: its own <c>/health</c> answering with a healthy database. Anything else — no
    /// answer, an error, a page that is not ours (a lapsed domain answers 200 to every path) — is a cloud that is gone.
    /// </summary>
    public static async Task<bool> CloudServesAsync(HttpClient http, string cloudUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(cloudUrl.TrimEnd('/') + "/health", cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return false;
            }

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return body.RootElement.TryGetProperty("checks", out var checks)
                   && checks.ValueKind == JsonValueKind.Object
                   && checks.TryGetProperty("database", out var database)
                   && string.Equals(database.GetString(), "Healthy", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>
/// The deployment kind as the installer wrote it into <c>appsettings.Install.json</c>, the layer it owns.
///
/// <para>⚠️ <b>The installer recognises a PC de secours by the substring <c>ClinicRelay</c> anywhere in that file</b>
/// (<c>clinic-setup.iss</c> <c>ExistingInstall</c>), so a promoted file must not carry it at all — then a later
/// installer run sees the cabinet's server, and <c>/RELAY</c> refuses as it does on any server.</para>
/// </summary>
public static class RelayInstallLayer
{
    private static readonly JsonSerializerOptions Write = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Null when the file says this PC is a PC de secours; otherwise why the promotion cannot touch it.</summary>
    public static string? WhyNotARelay(string path)
    {
        var profile = Profile(Read(path, out var error));
        if (error is not null)
        {
            return error;
        }

        var kind = profile is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        return string.Equals(kind, nameof(DeploymentKind.ClinicRelay), StringComparison.OrdinalIgnoreCase)
            ? null
            : $"Le fichier {Path.GetFileName(path)} ne dit pas que ce PC est un PC de secours : rien n'a été changé.";
    }

    /// <summary>Writes <c>SelfHostedLan</c> over <c>ClinicRelay</c>, keeping every other key; the old file is kept beside it.</summary>
    public static void SwitchToLocalServer(string path, DateTime nowUtc)
    {
        var root = Read(path, out var error) ?? throw new InvalidOperationException(error);
        var deployment = Section(root, "Deployment") as JsonObject
                         ?? throw new InvalidOperationException("The Deployment section is missing.");
        var key = deployment.Select(p => p.Key).First(k => string.Equals(k, "Profile", StringComparison.OrdinalIgnoreCase));
        deployment[key] = nameof(DeploymentKind.SelfHostedLan);

        File.Copy(path, $"{path}.bak-{nowUtc:yyyyMMdd-HHmmss}", overwrite: true);
        AtomicFile.Write(path, root.ToJsonString(Write));
    }

    private static JsonNode? Read(string path, out string? error)
    {
        error = null;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (root is JsonObject)
            {
                return root;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = $"Le fichier {Path.GetFileName(path)} n'a pas pu être lu ({ex.Message}) : rien n'a été changé.";
            return null;
        }

        error = $"Le fichier {Path.GetFileName(path)} est illisible : rien n'a été changé.";
        return null;
    }

    private static JsonNode? Profile(JsonNode? root) => Section(Section(root, "Deployment"), "Profile");

    private static JsonNode? Section(JsonNode? node, string name) =>
        node is JsonObject obj
            ? obj.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value
            : null;
}

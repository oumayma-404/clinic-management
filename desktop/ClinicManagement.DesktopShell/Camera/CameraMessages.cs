using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>
/// The page's answer about one photo it was handed: <c>camera-ack:{json}</c>.
///
/// <para><c>filed</c> carries the new file's id and its patient, and <see cref="KeepOriginal"/> when the cloud
/// stored a reduced copy (a clinic serving itself stored the original and keeps nothing here).</para>
/// </summary>
public sealed record CameraAck(
    string ItemId, string Outcome, Guid? FileId, Guid? PatientId, string? PatientName, bool KeepOriginal)
{
    public const string Filed = "filed";
    public const string Already = "already";
    public const string Skipped = "skipped";
    public const string Failed = "failed";

    public const int MaxPatientName = 200;

    public static CameraAck? TryParse(string? json)
    {
        var root = CameraMessages.Object(json);
        if (root is not { } r)
        {
            return null;
        }

        var itemId = CameraMessages.Text(r, "itemId", 16);
        var outcome = CameraMessages.Text(r, "outcome", 10);
        if (!CameraJournal.IsItemId(itemId) || outcome is not (Filed or Already or Skipped or Failed))
        {
            return null;
        }

        var fileId = CameraMessages.GuidOf(r, "fileId");
        var patientId = CameraMessages.GuidOf(r, "patientId");

        // A filed photo names where it went, or it cannot be kept, recorded or opened.
        if (outcome == Filed && (fileId is null || patientId is null))
        {
            return null;
        }

        var keep = r.TryGetProperty("keepOriginal", out var k) && k.ValueKind == JsonValueKind.True;
        return new CameraAck(
            itemId!, outcome, fileId, patientId, CameraMessages.Text(r, "patientName", MaxPatientName), keep);
    }
}

/// <summary>« Chercher l'appareil » — every connected source, or the one a notice named; older photos on request.</summary>
public sealed record CameraScanRequest(string? SourceKey, bool IncludeOlder)
{
    public const string Unplugged = "L'appareil photo n'est plus branché. Rebranchez-le (ou insérez sa carte), puis réessayez.";

    public static CameraScanRequest Parse(string? json)
    {
        if (CameraMessages.Object(json) is not { } root)
        {
            return new CameraScanRequest(null, false);
        }

        var sourceKey = CameraMessages.Text(root, "sourceKey", 64);
        return new CameraScanRequest(
            CameraMessages.IsSourceKey(sourceKey) ? sourceKey : null,
            root.TryGetProperty("includeOlder", out var older) && older.ValueKind == JsonValueKind.True);
    }

    /// <summary>
    /// Which connected sources this scan may read.
    ///
    /// <para>⚠️ <b>Never a device somebody chose to ignore</b>, whatever the page asks: « Ignorer cet appareil » is
    /// said about a colleague's phone, and the scan copies up to a thousand photos off whatever it reads.</para>
    ///
    /// <para>⚠️ <b>A named device is read alone or not at all</b> — gone, it answers <see cref="Unplugged"/>, never
    /// « every other device instead ». And « plus anciennes », which names none, reads only what the batch on
    /// screen came from (<paramref name="lastLook"/>).</para>
    /// </summary>
    public CameraScanChoice Choose(
        IReadOnlyList<ICameraSource> connected, IReadOnlyCollection<string> ignored, IReadOnlyCollection<string> lastLook)
    {
        var allowed = connected.Where(s => !ignored.Contains(s.SourceKey)).ToList();
        var skipped = connected.Count - allowed.Count;

        if (SourceKey is { } named)
        {
            var only = allowed.Where(s => s.SourceKey == named).ToList();
            return only.Count == 0
                ? new CameraScanChoice(Array.Empty<ICameraSource>(), skipped, Unplugged)
                : new CameraScanChoice(only, skipped, null);
        }

        return IncludeOlder
            ? new CameraScanChoice(allowed.Where(s => lastLook.Contains(s.SourceKey)).ToList(), skipped, null)
            : new CameraScanChoice(allowed, skipped, null);
    }
}

/// <summary>What one scan reads, how many connected devices it left alone, and why it reads nothing.</summary>
public sealed record CameraScanChoice(IReadOnlyList<ICameraSource> Sources, int Ignored, string? Refusal);

/// <summary>
/// Parsing for the page's <c>camera-*</c> messages. The page is web content and some of this text lands in a file
/// on disk (the originals' index) or names one to open, so every value is checked for shape and length, and anything
/// else reads as absent.
/// </summary>
public static class CameraMessages
{
    public const int MaxFileIds = 500;

    /// <summary><c>r12</c> or <c>r12:{json}</c> — the shape the bridge's request helper posts.</summary>
    public static bool TrySplitRequest(string rest, out string requestId, out string? json)
    {
        var separator = rest.IndexOf(':');
        requestId = separator < 0 ? rest : rest[..separator];
        json = separator < 0 ? null : rest[(separator + 1)..];
        return RelayInstaller.IsRequestId(requestId);
    }

    public static bool IsSourceKey(string? value) =>
        value is { Length: > 5 and <= 64 }
        && (value.StartsWith("card:", StringComparison.Ordinal) || value.StartsWith("wpd:", StringComparison.Ordinal))
        && value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or ':');

    public static Guid? FileIdOf(string? json) => Object(json) is { } root ? GuidOf(root, "fileId") : null;

    public static string? SourceKeyOf(string? json) =>
        Object(json) is { } root && Text(root, "sourceKey", 64) is { } key && IsSourceKey(key) ? key : null;

    public static bool? OnOf(string? json) =>
        Object(json) is { } root && root.TryGetProperty("on", out var on)
            ? on.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;

    public static IReadOnlyList<Guid> FileIdsOf(string? json)
    {
        if (Object(json) is not { } root
            || !root.TryGetProperty("fileIds", out var ids)
            || ids.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Guid>();
        }

        return ids.EnumerateArray()
            .Take(MaxFileIds)
            .Select(id => id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .Distinct()
            .ToList();
    }

    internal static JsonElement? Object(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 64_000)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? Text(JsonElement root, string name, int max)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > max || text.Any(char.IsControl) ? null : text;
    }

    internal static Guid? GuidOf(JsonElement root, string name) =>
        Text(root, name, 40) is { } text && Guid.TryParse(text, out var id) && id != Guid.Empty ? id : null;
}

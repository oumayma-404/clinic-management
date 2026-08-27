namespace ClinicManagement.Infrastructure.Storage;

/// <summary>
/// The single composer of a <b>new</b> blob's storage key (multi-tenant-cloud US-5): every key written from now
/// on is <c>clinics/{clinicId}/…</c>, in both backends.
///
/// <para><b>Why the composition is here and not at the call sites.</b> « Which clinic owns this blob » must have
/// one answer. Before US-5 it had two: four upload sites prefixed a path of their own with a bare
/// <c>{clinicId}/</c> while four wrote a flat <c>{guid}-{timestamp}</c> with no clinic in it at all — and a third
/// answer was one new upload away. Both <see cref="ClinicManagement.Application.Common.Interfaces.IFileStorage"/>
/// upload overloads therefore require the clinic id, so an unprefixed key is not something a caller can write.</para>
///
/// <para>⚠️ <b>Reading is deliberately not symmetrical.</b> Download and delete pass the stored key through
/// verbatim — a row written before US-5 holds a flat key and must keep resolving with <b>no backfill</b>
/// (amendment M2). Composing on the read side would strand every one of them.</para>
/// </summary>
public static class ClinicStorageKey
{
    /// <summary>Top-level segment every new key starts with.</summary>
    public const string Prefix = "clinics";

    /// <summary>
    /// Builds the key a blob is stored under: <paramref name="relativePath"/> below the owning clinic, or a
    /// unique generated leaf when the caller has no deterministic path of its own.
    /// </summary>
    /// <param name="clinicId">The clinic the blob belongs to.</param>
    /// <param name="relativePath">
    /// A path <b>within</b> the clinic (e.g. <c>logo</c>, <c>doctors/{id}/cachet</c>) — never carrying a clinic
    /// segment of its own. Null or blank means « give me a unique key ».
    /// </param>
    public static string Compose(Guid clinicId, string? relativePath = null)
    {
        if (clinicId == Guid.Empty)
        {
            // A blob with no owning clinic has nowhere correct to live, and clinics/00000000-…/ is a bucket
            // nothing would ever look in again. Fail where the mistake is, not on the read months later.
            throw new InvalidOperationException("A storage key cannot be composed without an owning clinic id.");
        }

        var leaf = string.IsNullOrWhiteSpace(relativePath)
            ? $"{Guid.NewGuid()}-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : Normalize(relativePath);

        return $"{Prefix}/{clinicId}/{leaf}";
    }

    /// <summary>
    /// Rejects a relative path that would climb out of its own clinic. MinIO object names are flat strings with
    /// no traversal semantics, so only the local-disk backend can be *escaped* — but a key like
    /// <c>clinics/A/../B/logo</c> would name clinic B's blob on disk and clinic A's in MinIO, and the two backends
    /// disagreeing about what a key means is worse than either behaviour.
    /// </summary>
    private static string Normalize(string relativePath)
    {
        var trimmed = relativePath.Trim().Replace('\\', '/');

        if (trimmed.StartsWith('/')
            || trimmed.Split('/').Any(segment => segment == ".."))
        {
            throw new InvalidOperationException(
                $"Invalid storage path escapes its clinic prefix: {relativePath}");
        }

        return trimmed;
    }
}

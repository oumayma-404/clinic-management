using System.IO.Compression;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Relay.Queries;

/// <summary>Resolves the calling PC and refuses a build that differs from the cloud's (D10) — the copy calls, never the heartbeat.</summary>
internal static class RelayFeedGate
{
    public static async Task<Result<ClinicRelay>> OpenAsync(
        IClinicContext context, IClinicRelayRepository relays, ITenantScope scope, IRelayBuildInfo build,
        string? relayBuild, CancellationToken cancellationToken)
    {
        var resolved = await RelayPrincipal.ResolveAsync(context, relays, scope, cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved;
        }

        return string.Equals(relayBuild, build.Current, StringComparison.Ordinal)
            ? resolved
            : Result<ClinicRelay>.Failure(RelayRefusals.VersionMismatch, RelayRefusals.VersionMismatchCode);
    }
}

/// <summary>Every key changed after <c>After</c>, as current rows or tombstones, in one snapshot (D3, D6b, D12).</summary>
public sealed record GetRelayChangesQuery(long After, string? Fingerprint, string? RelayBuild) : IRequest<Result<RelayFeedBatch>>;

public sealed class GetRelayChangesQueryHandler : IRequestHandler<GetRelayChangesQuery, Result<RelayFeedBatch>>
{
    private readonly IClinicContext _context;
    private readonly IClinicRelayRepository _relays;
    private readonly ITenantScope _scope;
    private readonly IRelayBuildInfo _build;
    private readonly IClinicRelayRowStore _rows;

    public GetRelayChangesQueryHandler(
        IClinicContext context, IClinicRelayRepository relays, ITenantScope scope, IRelayBuildInfo build,
        IClinicRelayRowStore rows)
    {
        _context = context;
        _relays = relays;
        _scope = scope;
        _build = build;
        _rows = rows;
    }

    public async Task<Result<RelayFeedBatch>> Handle(GetRelayChangesQuery request, CancellationToken cancellationToken)
    {
        var relay = await RelayFeedGate.OpenAsync(_context, _relays, _scope, _build, request.RelayBuild, cancellationToken);
        if (relay.IsFailure)
        {
            return Result<RelayFeedBatch>.FailureFrom(relay);
        }

        var batch = await _rows.ReadChangesAsync(relay.Value!.ClinicId, Math.Max(0, request.After), request.Fingerprint,
            new RelayOutboundWrap(relay.Value.PublicKey!), cancellationToken);
        return Result<RelayFeedBatch>.Success(batch);
    }
}

/// <summary>The clinic's relay scope (or some of its tables) as one consistent, gzip'd snapshot on a self-deleting file.</summary>
public sealed record GetRelaySnapshotQuery(IReadOnlyList<string>? Tables, string? RelayBuild) : IRequest<Result<Stream>>;

public sealed class GetRelaySnapshotQueryHandler : IRequestHandler<GetRelaySnapshotQuery, Result<Stream>>
{
    private readonly IClinicContext _context;
    private readonly IClinicRelayRepository _relays;
    private readonly ITenantScope _scope;
    private readonly IRelayBuildInfo _build;
    private readonly IClinicRelayRowStore _rows;

    public GetRelaySnapshotQueryHandler(
        IClinicContext context, IClinicRelayRepository relays, ITenantScope scope, IRelayBuildInfo build,
        IClinicRelayRowStore rows)
    {
        _context = context;
        _relays = relays;
        _scope = scope;
        _build = build;
        _rows = rows;
    }

    public async Task<Result<Stream>> Handle(GetRelaySnapshotQuery request, CancellationToken cancellationToken)
    {
        var relay = await RelayFeedGate.OpenAsync(_context, _relays, _scope, _build, request.RelayBuild, cancellationToken);
        if (relay.IsFailure)
        {
            return Result<Stream>.FailureFrom(relay);
        }

        // Written to a temp file first, so the snapshot's transaction is not held open for the length of a cabinet download.
        var path = Path.Combine(Path.GetTempPath(), $"relay-snapshot-{Guid.NewGuid():N}.json.gz");
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            await _rows.WriteSnapshotAsync(relay.Value!.ClinicId, request.Tables,
                new RelayOutboundWrap(relay.Value.PublicKey!), gzip, cancellationToken);
        }

        Stream result = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        return Result<Stream>.Success(result);
    }
}

/// <summary>The cloud's half of the hourly check (D25).</summary>
public sealed record GetRelayDigestQuery(string? RelayBuild) : IRequest<Result<IReadOnlyList<RelayTableDigest>>>;

public sealed class GetRelayDigestQueryHandler : IRequestHandler<GetRelayDigestQuery, Result<IReadOnlyList<RelayTableDigest>>>
{
    private readonly IClinicContext _context;
    private readonly IClinicRelayRepository _relays;
    private readonly ITenantScope _scope;
    private readonly IRelayBuildInfo _build;
    private readonly IClinicRelayRowStore _rows;

    public GetRelayDigestQueryHandler(
        IClinicContext context, IClinicRelayRepository relays, ITenantScope scope, IRelayBuildInfo build,
        IClinicRelayRowStore rows)
    {
        _context = context;
        _relays = relays;
        _scope = scope;
        _build = build;
        _rows = rows;
    }

    public async Task<Result<IReadOnlyList<RelayTableDigest>>> Handle(GetRelayDigestQuery request, CancellationToken cancellationToken)
    {
        var relay = await RelayFeedGate.OpenAsync(_context, _relays, _scope, _build, request.RelayBuild, cancellationToken);
        if (relay.IsFailure)
        {
            return Result<IReadOnlyList<RelayTableDigest>>.FailureFrom(relay);
        }

        return Result<IReadOnlyList<RelayTableDigest>>.Success(await _rows.DigestAsync(relay.Value!.ClinicId, cancellationToken));
    }
}

/// <summary>One stored object the clinic's rows name, by storage key — the files half of the copy (spec Part A).</summary>
public sealed record GetRelayBlobQuery(string StorageKey, string? RelayBuild) : IRequest<Result<Stream>>;

public sealed class GetRelayBlobQueryHandler : IRequestHandler<GetRelayBlobQuery, Result<Stream>>
{
    private readonly IClinicContext _context;
    private readonly IClinicRelayRepository _relays;
    private readonly ITenantScope _scope;
    private readonly IRelayBuildInfo _build;
    private readonly IRelayBlobIndex _blobs;
    private readonly IFileStorage _storage;

    public GetRelayBlobQueryHandler(
        IClinicContext context, IClinicRelayRepository relays, ITenantScope scope, IRelayBuildInfo build,
        IRelayBlobIndex blobs, IFileStorage storage)
    {
        _context = context;
        _relays = relays;
        _scope = scope;
        _build = build;
        _blobs = blobs;
        _storage = storage;
    }

    public async Task<Result<Stream>> Handle(GetRelayBlobQuery request, CancellationToken cancellationToken)
    {
        var relay = await RelayFeedGate.OpenAsync(_context, _relays, _scope, _build, request.RelayBuild, cancellationToken);
        if (relay.IsFailure)
        {
            return Result<Stream>.FailureFrom(relay);
        }

        // Only a key one of this clinic's rows names: a request cannot reach another clinic's object by guessing.
        if (string.IsNullOrWhiteSpace(request.StorageKey)
            || !await _blobs.ClinicNamesKeyAsync(relay.Value!.ClinicId, request.StorageKey, cancellationToken))
        {
            return Result<Stream>.Failure("Fichier introuvable.", "relay_blob_not_found");
        }

        return Result<Stream>.Success(await _storage.DownloadAsync(request.StorageKey, cancellationToken));
    }
}

/// <summary>Which stored objects a clinic's rows name (the archive's declared blob columns).</summary>
public interface IRelayBlobIndex
{
    Task<bool> ClinicNamesKeyAsync(Guid clinicId, string storageKey, CancellationToken cancellationToken);

    /// <summary>Every storage key the clinic's rows name — what the PC must hold on its own disk.</summary>
    Task<IReadOnlyList<string>> ListKeysAsync(Guid clinicId, CancellationToken cancellationToken);
}

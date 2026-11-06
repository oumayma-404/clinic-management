using ClinicManagement.Application.Features.Platform.Dtos;

namespace ClinicManagement.API.Models;

/// <summary>The news form as the console posts it, for the preview and the test send.</summary>
public record PlatformNewsDraftRequest(string? Subject, IReadOnlyList<PlatformNewsItemRequest>? Items);

/// <summary>The news form for the real send. <c>RequestId</c> is minted once per form, so a double-click sends once.</summary>
public record SendPlatformNewsRequest(Guid RequestId, string? Subject, IReadOnlyList<PlatformNewsItemRequest>? Items);

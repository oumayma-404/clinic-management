using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Files;
using MediatR;

namespace ClinicManagement.Application.Features.Meta.Queries;

/// <summary>
/// Publishes the patient-file door's own rules, so the browser can refuse an oversized or unsupported file before
/// a byte leaves it — using the server's wording, not a second copy of the rule (AC-5.1).
///
/// <para>Since the coffre it also answers <b>where</b> each format's files will be kept, which is the same kind of
/// fact and belongs on the same trip: a picker that had to guess would either offer a study the server will not
/// hold, or refuse one the cabinet can keep perfectly well.</para>
/// </summary>
public class GetUploadPolicyQuery : IRequest<Result<UploadPolicyDto>>
{
    /// <summary>
    /// Which door to describe. Absent means the patient's file drawer, which is what the endpoint served when it
    /// served only one — so an older client keeps working unchanged.
    /// </summary>
    public string? Profile { get; set; }
}

public class GetUploadPolicyQueryHandler : IRequestHandler<GetUploadPolicyQuery, Result<UploadPolicyDto>>
{
    private readonly IFileResidencyPolicy _residencyPolicy;

    public GetUploadPolicyQueryHandler(IFileResidencyPolicy residencyPolicy)
    {
        _residencyPolicy = residencyPolicy;
    }

    public Task<Result<UploadPolicyDto>> Handle(GetUploadPolicyQuery request, CancellationToken cancellationToken)
    {
        var profile = request.Profile is null
            ? FileUploadProfile.PatientFile
            : FileUploadProfile.TryByName(request.Profile);

        // An unknown door is refused rather than defaulting to the patient-file one: silently handing a logo
        // picker the drawer's policy would offer DICOM as a clinic logo and quote the wrong ceiling.
        if (profile is null)
        {
            return Task.FromResult(Result<UploadPolicyDto>.Failure("Ce type d'envoi n'existe pas."));
        }

        var vaultAvailable = _residencyPolicy.VaultAvailable;

        var dto = new UploadPolicyDto
        {
            Profile = profile.Name,
            MaxBytes = profile.MaxBytes,
            // Extensions, not content types: a browser derives the type from the extension through the OS
            // registry and registers none for .stl or .dcm, so a MIME accept list would hide them in the picker.
            Accept = string.Join(",", profile.Entries.SelectMany(entry => entry.Extensions).Select(e => $".{e}")),
            UnsupportedMessage = profile.UnsupportedMessage,
            DeniedMessage = FileUploadValidator.DeniedMessage,
            DeniedExtensions = FileTypeCatalog.DeniedExtensions.OrderBy(e => e, StringComparer.Ordinal).ToList(),
            VaultAvailable = vaultAvailable,
            VaultUnavailableMessage = FileResidencyRefusals.Unavailable(),
            Formats = profile.Entries.Select(entry => ToFormat(profile, entry, vaultAvailable)).ToList()
        };

        return Task.FromResult(Result<UploadPolicyDto>.Success(dto));
    }

    // Where there is no coffre every format reads as always-hosted at the door's own ceiling, so a client written
    // against one deployment kind cannot show a « conservé au cabinet » state on the other (AC-7).
    private static UploadPolicyFormatDto ToFormat(FileUploadProfile profile, FileTypeEntry entry, bool vaultAvailable)
    {
        var routesToVault = vaultAvailable && entry.Residency.Kind == ResidencyKind.HostedUpTo;

        // The DOOR's cap, which is the entry's own unless the profile is tighter — a JPEG cachet is five megabytes
        // where a JPEG radiograph is fifty, and the picker must quote the one it is standing in front of.
        var maxBytes = profile.CapFor(entry);

        return new UploadPolicyFormatDto
        {
            Extensions = entry.Extensions.ToList(),
            ContentType = entry.ContentType,
            Label = entry.Label,
            MaxBytes = maxBytes,
            IsBrowserPreviewable = entry.IsBrowserPreviewable,
            TooLargeMessage = FileUploadValidator.TooLargeMessage(maxBytes),
            Residency = routesToVault ? "hostedUpTo" : "hosted",
            HostedMaxBytes = routesToVault ? entry.Residency.HostedMaxBytes : maxBytes,
            VaultMaxBytes = routesToVault ? entry.VaultMaxBytes : 0,
            VaultTooLargeMessage = routesToVault
                ? FileResidencyRefusals.TooLarge(entry.VaultMaxBytes)
                : string.Empty
        };
    }
}

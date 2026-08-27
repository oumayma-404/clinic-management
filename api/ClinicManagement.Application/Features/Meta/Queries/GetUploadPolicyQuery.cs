using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using MediatR;

namespace ClinicManagement.Application.Features.Meta.Queries;

/// <summary>
/// Publishes the patient-file door's own rules, so the browser can refuse an oversized or unsupported file before
/// a byte leaves it — using the server's wording, not a second copy of the rule (AC-5.1).
/// </summary>
public class GetUploadPolicyQuery : IRequest<Result<UploadPolicyDto>>
{
}

public class GetUploadPolicyQueryHandler : IRequestHandler<GetUploadPolicyQuery, Result<UploadPolicyDto>>
{
    public Task<Result<UploadPolicyDto>> Handle(GetUploadPolicyQuery request, CancellationToken cancellationToken)
    {
        var profile = FileUploadProfile.PatientFile;

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
            Formats = profile.Entries.Select(entry => new UploadPolicyFormatDto
            {
                Extensions = entry.Extensions.ToList(),
                ContentType = entry.ContentType,
                Label = entry.Label,
                MaxBytes = entry.MaxBytes,
                IsBrowserPreviewable = entry.IsBrowserPreviewable,
                TooLargeMessage = FileUploadValidator.TooLargeMessage(entry.MaxBytes)
            }).ToList()
        };

        return Task.FromResult(Result<UploadPolicyDto>.Success(dto));
    }
}

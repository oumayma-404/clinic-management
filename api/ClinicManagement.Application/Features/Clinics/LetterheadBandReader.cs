using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Features.Clinics;

/// <summary>Which band of the letterhead an upload is — the header and footer have different height caps.</summary>
public enum LetterheadBandKind
{
    Header,
    Footer,

    /// <summary>« Page entière »: the strip between the two bands. Stretched to fit, so it has no height cap.</summary>
    Body
}

/// <summary>
/// Validates one uploaded band and returns its bytes. Shared by the save and the preview so a band the preview
/// shows is a band the save accepts — a preview that renders what the save then refuses would be a lie.
/// </summary>
public static class LetterheadBandReader
{
    public static async Task<Result<byte[]>> ReadAsync(
        LetterheadBandKind kind, Stream content, string? fileName, long length, CancellationToken cancellationToken)
    {
        var validation = await FileUploadValidator.ValidateAsync(
            FileUploadProfile.LetterheadBand, fileName, length, content, cancellationToken);
        if (validation.IsFailure)
        {
            return Result<byte[]>.Failure(validation.Error!, validation.Code);
        }

        using var buffer = new MemoryStream();
        await validation.Value!.Content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        if (PngDimensions.Read(bytes) is not { } size)
        {
            return Result<byte[]>.Failure(LetterheadRules.Unreadable, LetterheadRules.UnreadableCode);
        }

        if (size.Width < LetterheadRules.MinWidthPx)
        {
            return Result<byte[]>.Failure(LetterheadRules.TooSmall(size.Width), LetterheadRules.TooSmallCode);
        }

        var heightMm = LetterheadRules.PrintedHeightMm(size.Width, size.Height);
        return kind switch
        {
            LetterheadBandKind.Header when heightMm > LetterheadRules.MaxHeaderHeightMm =>
                Result<byte[]>.Failure(LetterheadRules.HeaderTooTall, LetterheadRules.HeaderTooTallCode),
            LetterheadBandKind.Footer when heightMm > LetterheadRules.MaxFooterHeightMm =>
                Result<byte[]>.Failure(LetterheadRules.FooterTooTall, LetterheadRules.FooterTooTallCode),
            _ => Result<byte[]>.Success(bytes)
        };
    }
}

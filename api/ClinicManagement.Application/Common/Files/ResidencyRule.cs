using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Common.Files;

/// <summary>Where a format's files belong, as the catalog declares it.</summary>
public enum ResidencyKind
{
    /// <summary>Every file of this format is hosted, whatever its size. A PDF, an ordonnance, a photo.</summary>
    AlwaysHosted = 1,

    /// <summary>Hosted up to a threshold, and in the cabinet's coffre above it.</summary>
    HostedUpTo = 2
}

/// <summary>
/// The residency a <see cref="FileTypeEntry"/> declares, and where its threshold sits — <see cref="SignatureRule"/>'s
/// shape, and for the same reason: the fact belongs to the format, and the catalog is the one place that knows it.
///
/// <para>⚠️ <b>The threshold is a size, not a format list.</b> A single DICOM slice is two megabytes and must stay
/// openable on a phone; the same extension at four hundred megabytes is a study that would cost the deployment one
/// live copy, fourteen nightly tarballs and an off-site copy every night. Deciding on the extension alone would
/// send both to the same place and one of them would be wrong.</para>
/// </summary>
public sealed class ResidencyRule
{
    private ResidencyRule(ResidencyKind kind, long hostedMaxBytes)
    {
        Kind = kind;
        HostedMaxBytes = hostedMaxBytes;
    }

    public ResidencyKind Kind { get; }

    /// <summary>The largest file of this format the deployment will hold. <see cref="long.MaxValue"/> when it holds every one.</summary>
    public long HostedMaxBytes { get; }

    public static readonly ResidencyRule AlwaysHosted = new(ResidencyKind.AlwaysHosted, long.MaxValue);

    public static ResidencyRule HostedUpTo(long bytes)
    {
        if (bytes <= 0)
        {
            throw new ArgumentException("Un seuil de résidence doit être positif.", nameof(bytes));
        }

        return new ResidencyRule(ResidencyKind.HostedUpTo, bytes);
    }

    /// <summary>Where a file of this format and this size belongs, on a deployment that has a coffre at all.</summary>
    public FileResidency Decide(long sizeBytes) =>
        sizeBytes > HostedMaxBytes ? FileResidency.Vault : FileResidency.Hosted;
}

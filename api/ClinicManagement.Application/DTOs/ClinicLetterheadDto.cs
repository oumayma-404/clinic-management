using System.Security.Cryptography;
using System.Text;

namespace ClinicManagement.Application.DTOs;

/// <summary>Whether the cabinet has its own letterhead, and the clinic row's version to round-trip on a change.</summary>
public class ClinicLetterheadDto
{
    public bool HasHeader { get; set; }
    public bool HasFooter { get; set; }

    /// <summary>« Page entière »: the strip between the bands is drawn too.</summary>
    public bool HasBody { get; set; }

    /// <summary>Changes with every new upload (keys are never reused), so the browser can bust its image cache on it.</summary>
    public string? Revision { get; set; }

    /// <summary>The clinic row's <c>xmin</c> — the same token the settings form holds.</summary>
    public uint Version { get; set; }

    public static ClinicLetterheadDto From(Domain.Entities.Clinic clinic) => new()
    {
        HasHeader = clinic.LetterheadHeaderStorageKey != null,
        HasFooter = clinic.LetterheadFooterStorageKey != null,
        HasBody = clinic.LetterheadBodyStorageKey != null,
        Revision = clinic.LetterheadHeaderStorageKey is { } header
            ? Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(header + "|" + clinic.LetterheadFooterStorageKey + "|" + clinic.LetterheadBodyStorageKey)))[..16].ToLowerInvariant()
            : null,
        Version = clinic.Version
    };
}

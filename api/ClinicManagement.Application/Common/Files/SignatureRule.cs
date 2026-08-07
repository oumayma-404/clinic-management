using System.Text;

namespace ClinicManagement.Application.Common.Files;

/// <summary>What a format's leading bytes are allowed to tell us.</summary>
public enum SignatureKind
{
    /// <summary>The bytes must match, at the declared offset. A mismatch is a refusal.</summary>
    Required = 1,

    /// <summary>The bytes match when the marker is present, but its absence is legitimate.</summary>
    Advisory = 2,

    /// <summary>The format has no signature at all; the reason says why.</summary>
    None = 3
}

/// <summary>
/// The signature a <see cref="FileTypeEntry"/> expects, and where it sits.
///
/// <para>The offset is what makes DICOM expressible: <c>DICM</c> is at byte <b>128</b>, behind a preamble, so a
/// rule shaped « the file starts with these bytes » could never carry it.</para>
///
/// <para><see cref="None"/> demands a reason and refuses an empty one at construction: the entries are static, so
/// an unexplained "this format has no signature" fails the first time anything touches the catalog rather than
/// waiting for a reviewer to ask.</para>
/// </summary>
public sealed class SignatureRule
{
    private readonly IReadOnlyList<byte[]> _magics;

    private SignatureRule(SignatureKind kind, int offset, IReadOnlyList<byte[]> magics, string reason)
    {
        Kind = kind;
        Offset = offset;
        _magics = magics;
        Reason = reason;
    }

    public SignatureKind Kind { get; }

    /// <summary>Byte offset the marker starts at — 0 for almost everything, 128 for DICOM.</summary>
    public int Offset { get; }

    /// <summary>Why the format carries no marker. Non-empty exactly when <see cref="Kind"/> is <c>None</c>.</summary>
    public string Reason { get; }

    public static SignatureRule Required(int offset, byte[] magic, params byte[][] alternates) =>
        new(SignatureKind.Required, offset, Build(magic, alternates), string.Empty);

    public static SignatureRule Required(int offset, string ascii, params string[] alternates) =>
        Required(offset, Ascii(ascii), alternates.Select(Ascii).ToArray());

    public static SignatureRule Advisory(int offset, byte[] magic) =>
        new(SignatureKind.Advisory, offset, Build(magic, Array.Empty<byte[]>()), string.Empty);

    public static SignatureRule Advisory(int offset, string ascii) => Advisory(offset, Ascii(ascii));

    public static SignatureRule None(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A signature-less format must say why it has no signature.", nameof(reason));
        }

        return new SignatureRule(SignatureKind.None, 0, Array.Empty<byte[]>(), reason);
    }

    /// <summary>True when <paramref name="header"/> carries one of this rule's markers at its offset.</summary>
    public bool Matches(ReadOnlySpan<byte> header)
    {
        foreach (var magic in _magics)
        {
            if (header.Length >= Offset + magic.Length
                && header.Slice(Offset, magic.Length).SequenceEqual(magic))
            {
                return true;
            }
        }

        return false;
    }

    private static byte[][] Build(byte[] magic, byte[][] alternates)
    {
        if (magic.Length == 0)
        {
            throw new ArgumentException("A signature rule needs at least one byte to match.", nameof(magic));
        }

        return new[] { magic }.Concat(alternates).ToArray();
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
}

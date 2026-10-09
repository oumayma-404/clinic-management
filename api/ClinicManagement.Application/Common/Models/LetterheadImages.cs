namespace ClinicManagement.Application.Common.Models;

/// <summary>
/// A letterhead's bands as PNG bytes, ready to draw: the header always, the footer when the paper has one, and — for
/// « Page entière » — the strip between them, drawn behind the text and stretched to the space left.
/// </summary>
public sealed record LetterheadImages(byte[] Header, byte[]? Footer, byte[]? Body = null);

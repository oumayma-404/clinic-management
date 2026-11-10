namespace ClinicManagement.Domain.ValueObjects;

/// <summary>
/// One bullet of a news e-mail: an emoji and one line of text, plus an optional guide — a short description and
/// up to <see cref="MaxSteps"/> numbered steps (« Comment ça marche »). A row with no guide renders as before.
/// </summary>
public sealed class PlatformNewsItem
{
    public const int EmojiMaxLength = 16;
    public const int TextMaxLength = 300;
    public const int DescriptionMaxLength = 400;
    public const int MaxSteps = 5;
    public const int StepMaxLength = 160;

    // Stored as one JSON text column, DentalRecord.Notes' shape: a guide's steps are read and written whole.
    private readonly List<string> _steps = new();

    public string Emoji { get; private set; }
    public string Text { get; private set; }
    public int SortOrder { get; private set; }

    /// <summary>One paragraph under the line, or null.</summary>
    public string? Description { get; private set; }

    /// <summary>The numbered steps, in order; empty when the row has none.</summary>
    public IReadOnlyList<string> Steps => _steps.AsReadOnly();

    private PlatformNewsItem()
    {
        // For EF Core.
        Emoji = string.Empty;
        Text = string.Empty;
    }

    public PlatformNewsItem(
        string emoji, string text, int sortOrder, string? description = null, IEnumerable<string>? steps = null)
    {
        if (string.IsNullOrWhiteSpace(emoji) || emoji.Length > EmojiMaxLength)
        {
            throw new ArgumentException("Chaque nouveauté doit avoir un emoji.", nameof(emoji));
        }

        if (string.IsNullOrWhiteSpace(text) || text.Length > TextMaxLength)
        {
            throw new ArgumentException(
                $"Chaque nouveauté doit avoir un texte de {TextMaxLength} caractères au plus.", nameof(text));
        }

        if (description is { Length: > DescriptionMaxLength })
        {
            throw new ArgumentException(
                $"Le guide d'une nouveauté fait {DescriptionMaxLength} caractères au plus.", nameof(description));
        }

        var stepList = (steps ?? []).ToList();
        if (stepList.Count > MaxSteps || stepList.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > StepMaxLength))
        {
            throw new ArgumentException(
                $"Un guide a {MaxSteps} étapes au plus, de {StepMaxLength} caractères chacune.", nameof(steps));
        }

        Emoji = emoji;
        Text = text;
        SortOrder = sortOrder;
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
        _steps.AddRange(stepList);
    }
}

namespace ClinicManagement.Domain.ValueObjects;

/// <summary>One bullet of a news e-mail: an emoji and one line of text.</summary>
public sealed class PlatformNewsItem
{
    public const int EmojiMaxLength = 16;
    public const int TextMaxLength = 300;

    public string Emoji { get; private set; }
    public string Text { get; private set; }
    public int SortOrder { get; private set; }

    private PlatformNewsItem()
    {
        // For EF Core.
        Emoji = string.Empty;
        Text = string.Empty;
    }

    public PlatformNewsItem(string emoji, string text, int sortOrder)
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

        Emoji = emoji;
        Text = text;
        SortOrder = sortOrder;
    }
}

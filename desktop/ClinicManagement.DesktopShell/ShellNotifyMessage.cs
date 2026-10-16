using System.Text.Json;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// What the page asks Windows to show for a chat message (staff-chat AC-4.3): <c>notify:{json}</c> with a title, a
/// body, the conversation to open on a click, and whether it is urgent. Anything else is refused — the page is web
/// content, and this text lands in the system's notification centre.
/// </summary>
public sealed record ShellNotifyMessage(string Title, string Body, string Conversation, bool Urgent)
{
    public const int MaxTitle = 120;
    public const int MaxBody = 200;
    public const int MaxConversation = 300;

    public static ShellNotifyMessage? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 2_000)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var title = Text(root, "title", MaxTitle);
            var body = Text(root, "body", MaxBody);
            var conversation = Text(root, "tag", MaxConversation);
            if (title is null || body is null || conversation is null)
            {
                return null;
            }

            var urgent = root.TryGetProperty("urgent", out var u) && u.ValueKind == JsonValueKind.True;
            return new ShellNotifyMessage(title, body, conversation, urgent);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name, int max)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > max || text.Any(char.IsControl))
        {
            return null;
        }

        return text;
    }
}

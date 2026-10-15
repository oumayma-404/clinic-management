namespace ClinicManagement.Application.DTOs;

/// <summary>A colleague as the chat shows them; <see cref="Online"/> means the app is open right now, nothing more.</summary>
public class StaffColleagueDto
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDentist { get; set; }
    public bool IsSecretary { get; set; }
    public bool Online { get; set; }
    public bool Active { get; set; }
}

public class StaffMessageSenderDto
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class StaffQuickMessageRefDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Urgent { get; set; }
}

public class StaffMessageDto
{
    public Guid Id { get; set; }
    public string Conversation { get; set; } = string.Empty;
    public StaffMessageSenderDto Sender { get; set; } = new();
    public DateTime SentAt { get; set; }
    public string? Body { get; set; }
    public StaffQuickMessageRefDto? QuickMessage { get; set; }
}

public class StaffConversationDto
{
    public string Key { get; set; } = string.Empty;

    /// <summary><c>direct</c> · <c>desk</c> · <c>clinic</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public List<StaffColleagueDto> Members { get; set; } = new();
    public StaffMessageDto? LastMessage { get; set; }

    /// <summary>Everything unread here, including what reached this person silently (AC-5.3).</summary>
    public int UnreadCount { get; set; }

    public bool HasUrgentUnread { get; set; }

    /// <summary>True when the other side's account is deactivated: history readable, nothing can be sent (FR-9).</summary>
    public bool ReadOnly { get; set; }
}

public class StaffMessagePageDto
{
    /// <summary>Newest first.</summary>
    public List<StaffMessageDto> Items { get; set; } = new();

    public string? NextCursor { get; set; }
}

/// <summary>What the « Messages » badge and the notices are built from.</summary>
public class StaffChatUnreadDto
{
    /// <summary>Unread messages that ring this person — the silent ones of a shared desk are not counted (AC-5.3).</summary>
    public int Total { get; set; }

    public bool HasUrgent { get; set; }

    /// <summary>The newest of those, newest first.</summary>
    public List<StaffMessageDto> Recent { get; set; } = new();
}

/// <summary>One quick message; <see cref="For"/> is <c>dentist</c> · <c>desk</c> · <c>reply</c>.</summary>
public class StaffQuickMessageDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string For { get; set; } = string.Empty;
    public bool AsksForPatient { get; set; }
    public bool Urgent { get; set; }
    public bool BuiltIn { get; set; }
    /// <summary>The cabinet's own order — what the editor shows; <see cref="StaffQuickMessageListDto.Items"/> is in the caller's.</summary>
    public int Order { get; set; }
}

public class StaffQuickMessageListDto
{
    /// <summary>The list's version for the next save; 0 while the cabinet uses the defaults.</summary>
    public uint Version { get; set; }
    /// <summary>Most sent by the caller over 60 days first; then the cabinet's order (AC-2.2).</summary>
    public List<StaffQuickMessageDto> Items { get; set; } = new();
}

/// <summary>One quick message as the editor saves it; a null key is a new message.</summary>
public class StaffQuickMessageInputDto
{
    public string? Key { get; set; }
    public string Label { get; set; } = string.Empty;
    public string For { get; set; } = string.Empty;
    public bool AsksForPatient { get; set; }
    public bool Urgent { get; set; }
}

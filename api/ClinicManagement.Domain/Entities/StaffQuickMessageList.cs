using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Domain.Entities;

/// <summary>One quick message as an editor sends it; a null key is a new message.</summary>
public sealed record StaffQuickMessageInput(
    string? Key, string Label, StaffQuickMessageAudience Audience, bool AsksForPatient, bool IsUrgent);

/// <summary>
/// The cabinet's own quick-message list (staff-chat FR-4, US-6). No row means the defaults. An aggregate so every
/// edit reaches « Journal d'activité » and a stale save is a 409 (AC-6.5).
/// </summary>
public class StaffQuickMessageList : AggregateRoot<Guid>
{
    public const int MaxItems = 30;

    private readonly List<StaffQuickMessage> _items = new();

    public Guid ClinicId { get; private set; }
    public IReadOnlyCollection<StaffQuickMessage> Items => _items.AsReadOnly();

    /// <summary>Moves on every save: replacing only the owned rows leaves the list row untouched, so neither the
    /// version check nor the journal would see the edit.</summary>
    public DateTime UpdatedAt { get; private set; }

    private StaffQuickMessageList() { } // For EF Core

    public static StaffQuickMessageList Create(Guid clinicId, IEnumerable<StaffQuickMessageInput> items, DateTime nowUtc)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("La clinique est obligatoire.", nameof(clinicId));
        }

        var list = new StaffQuickMessageList { Id = Guid.NewGuid(), ClinicId = clinicId };
        list.Replace(items, nowUtc);
        return list;
    }

    /// <summary>Replaces the whole list in the given order; refuses what FR-4 and AC-6.2 forbid, in French.</summary>
    public void Replace(IEnumerable<StaffQuickMessageInput> items, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(items);
        var incoming = items.ToList();

        if (incoming.Count > MaxItems)
        {
            throw new ArgumentException($"Au plus {MaxItems} messages rapides.", nameof(items));
        }

        var built = new List<StaffQuickMessage>(incoming.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in incoming)
        {
            var label = item.Label?.Trim() ?? string.Empty;
            if (label.Length == 0)
            {
                throw new ArgumentException("Chaque message rapide doit avoir un texte.", nameof(items));
            }

            if (label.Length > StaffQuickMessage.LabelMaxLength)
            {
                throw new ArgumentException(
                    $"« {label} » dépasse {StaffQuickMessage.LabelMaxLength} caractères.", nameof(items));
            }

            if (!labels.Add(label))
            {
                throw new ArgumentException($"« {label} » figure deux fois dans la liste.", nameof(items));
            }

            if (!Enum.IsDefined(item.Audience))
            {
                throw new ArgumentException($"Choisissez à qui s'adresse « {label} ».", nameof(items));
            }

            var key = string.IsNullOrWhiteSpace(item.Key) ? $"custom-{Guid.NewGuid():N}" : item.Key.Trim();
            if (key.Length > StaffQuickMessage.KeyMaxLength || !keys.Add(key))
            {
                throw new ArgumentException($"« {label} » n'a pas un identifiant valide.", nameof(items));
            }

            var builtInUrgency = StaffQuickMessageDefaults.BuiltInUrgency(key);
            if (builtInUrgency is bool fixedUrgency && item.IsUrgent != fixedUrgency)
            {
                throw new ArgumentException($"L'urgence de « {label} » ne peut pas être modifiée.", nameof(items));
            }

            built.Add(new StaffQuickMessage(
                key, label, item.Audience, item.AsksForPatient, item.IsUrgent, builtInUrgency is not null, built.Count));
        }

        var missing = StaffQuickMessageDefaults.BuiltInKeys.Where(k => !keys.Contains(k)).ToList();
        if (missing.Count > 0)
        {
            // Named as the cabinet calls them now — a renamed « Patient arrivé » is refused under its new name.
            var names = missing.Select(k =>
                $"« {_items.FirstOrDefault(i => i.Key == k)?.Label ?? StaffQuickMessageDefaults.Items().First(d => d.Key == k).Label} »");
            var verb = missing.Count == 1 ? "ne peut pas être supprimé" : "ne peuvent pas être supprimés";
            throw new ArgumentException($"{string.Join(", ", names)} {verb}.", nameof(items));
        }

        _items.Clear();
        _items.AddRange(built);
        UpdatedAt = nowUtc;
    }
}

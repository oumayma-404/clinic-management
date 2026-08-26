using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Common;

/// <summary>French display labels for <see cref="PaymentMethod"/> (used on receipts / PDFs).</summary>
public static class PaymentMethodLabels
{
    public static string ToFrench(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Espèces",
        PaymentMethod.Cheque => "Chèque",
        // ⚠️ « Carte », not « Carte bancaire ». The two spellings were on screen AT ONCE on la caisse — the
        // « dont » chip took this map while the dépenses table and the expense form took the client's own — and a
        // reader cannot tell whether they are one method or two. The shorter form wins because it is what the two
        // controls a user actually operates already say, and because « Carte bancaire » does not fit the chip.
        PaymentMethod.Card => "Carte",
        PaymentMethod.Transfer => "Virement",
        _ => method.ToString(),
    };

    /// <summary>
    /// The French label for a stored enum NAME (`"Card"`), for the CSV writers whose DTOs carry the name as text.
    /// An unrecognised value is returned unchanged rather than blanked — a column that silently empties is worse
    /// than one carrying a value somebody has to look up.
    /// </summary>
    public static string ToFrench(string? methodName) =>
        Enum.TryParse<PaymentMethod>(methodName, ignoreCase: true, out var parsed)
            ? ToFrench(parsed)
            : methodName ?? string.Empty;
}

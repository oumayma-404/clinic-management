using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Common;

/// <summary>French display labels for <see cref="PaymentMethod"/> (used on receipts / PDFs).</summary>
public static class PaymentMethodLabels
{
    public static string ToFrench(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Espèces",
        PaymentMethod.Cheque => "Chèque",
        PaymentMethod.Card => "Carte bancaire",
        PaymentMethod.Transfer => "Virement",
        _ => method.ToString(),
    };
}

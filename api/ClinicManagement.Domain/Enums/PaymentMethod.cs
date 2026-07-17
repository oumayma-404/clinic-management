namespace ClinicManagement.Domain.Enums;

/// <summary>
/// How an invoice payment was received (Tunisia): cash, cheque, bank card, or wire transfer.
/// </summary>
public enum PaymentMethod
{
    Cash = 0,
    Cheque = 1,
    Card = 2,
    Transfer = 3
}

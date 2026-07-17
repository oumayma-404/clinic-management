namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Lifecycle of an invoice's TTN « El Fatoora » electronic-invoicing state, independent of the fiscal
/// <see cref="InvoiceStatus"/>. An issued invoice starts <see cref="NotSubmitted"/>; the user queues it
/// (<see cref="Queued"/> — the offline outbox), it is signed (<see cref="Signed"/>) and submitted
/// (<see cref="Submitted"/> → <see cref="Validating"/>) to TTN, which validates it (<see cref="Valid"/>)
/// and returns the unique identifier + QR cachet. Terminal error states: <see cref="Rejected"/>
/// (permanent — bad data/schema, needs correction) and <see cref="Failed"/> (retry budget exhausted).
/// </summary>
public enum EInvoiceStatus
{
    NotSubmitted = 0,
    Queued = 1,
    Signed = 2,
    Submitted = 3,
    Validating = 4,
    Valid = 5,
    Rejected = 6,
    Failed = 7
}

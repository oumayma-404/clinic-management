namespace ClinicManagement.Application.DTOs;

/// <summary>
/// « Reste à payer » (À clôturer): one page of patients who owe, plus both lists' totals over every matching
/// patient — never summed from a page, for <see cref="ReceivablesPageDto"/>'s reason.
/// </summary>
public class ResteAPayerPageDto
{
    public List<ResteAPayerRowDto> Items { get; set; } = new();

    /// <summary>Σ due-now amounts over every matching patient (the « À relancer » card).</summary>
    public decimal DueTotal { get; set; }
    public int DueCount { get; set; }

    /// <summary>Σ not-yet-due amounts over every matching patient (the « En cours » card).</summary>
    public decimal RunningTotal { get; set; }
    public int RunningCount { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; } = 1;
}

/// <summary>One patient who owes. <c>DueAmount + RunningAmount</c> equals their « Solde dû ».</summary>
public class ResteAPayerRowDto
{
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }

    /// <summary>The stored dialable form behind « Appeler » and « WhatsApp »; null hides both.</summary>
    public string? PhoneE164 { get; set; }

    public decimal DueAmount { get; set; }
    public decimal RunningAmount { get; set; }

    /// <summary>The oldest due document's reason — « Note du 14/08 non soldée ».</summary>
    public string? DueReason { get; set; }

    /// <summary>« Note n° 2026-0042 · Obturation, Couronne » for the oldest due document.</summary>
    public string? DueDocument { get; set; }
    public int DueDocumentCount { get; set; }
    public DateTime? DueSince { get; set; }

    /// <summary>Whole clinic days since <see cref="DueSince"/>.</summary>
    public int? DueDays { get; set; }

    /// <summary>The leading not-yet-due document's reason — « 2 actes faits sur 3 ».</summary>
    public string? RunningReason { get; set; }
    public string? RunningDocument { get; set; }
    public int RunningDocumentCount { get; set; }
    public int? RunningActsDone { get; set; }
    public int? RunningActsTotal { get; set; }

    /// <summary>The patient's next booked séance, if any.</summary>
    public DateTime? NextVisit { get; set; }

    /// <summary>The earliest agreed échéance not due yet, if any.</summary>
    public DateTime? NextInstallmentDue { get; set; }
}

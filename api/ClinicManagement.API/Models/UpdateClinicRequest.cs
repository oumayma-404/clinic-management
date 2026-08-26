using Microsoft.AspNetCore.Http;

namespace ClinicManagement.API.Models;

public class UpdateClinicRequest
{
    /// <summary>
    /// Optimistic-concurrency token, as a FORM field — this endpoint is multipart (it carries the logo), so
    /// the version cannot ride in a JSON body like it does everywhere else.
    /// </summary>
    public uint Version { get; set; }

    public string Name { get; set; } = string.Empty;

    /*
     * ⚠️ Band A — the five nullable strings below are TRI-STATE, and the `*Specified` flags are the only thing
     * that can express it here.
     *
     * MVC form binding converts an empty form value to `null` for a reference type, so « the field was cleared »
     * and « the field was not sent » arrived at the handler as the same `null` — and the handler read null as
     * « leave unchanged ». A matricule fiscal, once saved, could therefore never be cleared: the blank save
     * reported « Paramètres de facturation enregistrés. » and the old value came back on reload. Ville and the
     * gouvernorate (which travels inside Address) had the same mechanism.
     *
     * The setter fires whenever the binder assigns — including when it assigns null for an empty value — and does
     * not fire when the key is absent from the body. That is exactly the distinction the handler needs.
     */
    public string? Address
    {
        get => _address;
        set { _address = value; AddressSpecified = true; }
    }
    private string? _address;
    public bool AddressSpecified { get; private set; }

    public string? City
    {
        get => _city;
        set { _city = value; CitySpecified = true; }
    }
    private string? _city;
    public bool CitySpecified { get; private set; }

    public string? Phone
    {
        get => _phone;
        set { _phone = value; PhoneSpecified = true; }
    }
    private string? _phone;
    public bool PhoneSpecified { get; private set; }

    public string? Email
    {
        get => _email;
        set { _email = value; EmailSpecified = true; }
    }
    private string? _email;
    public bool EmailSpecified { get; private set; }

    public IFormFile? Logo { get; set; }

    /// <summary>Billing settings. Tri-state like the fields above — omitted leaves it, empty clears it.</summary>
    public string? MatriculeFiscal
    {
        get => _matriculeFiscal;
        set { _matriculeFiscal = value; MatriculeFiscalSpecified = true; }
    }
    private string? _matriculeFiscal;
    public bool MatriculeFiscalSpecified { get; private set; }

    public bool? VatApplicable { get; set; }
    public decimal? VatRate { get; set; }
    public bool? StampDutyEnabled { get; set; }
    public decimal? StampDutyAmount { get; set; }

    // Working hours as a JSON array (reliability-and-polish AC-7). Sent as a form field by the settings UI;
    // null/blank leaves the current value unchanged.
    public string? WorkingHoursJson { get; set; }
}




using System.Globalization;
using System.Xml.Linq;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Builds a TEIF (Tunisian Electronic Invoice Format) XML document from an issued invoice (FR-1).
/// The element structure follows the published TEIF layout (TEIF root + InvoiceHeader + InvoiceBody with
/// Bgm/Dtm/PartnerSection/LinSection/InvoiceMoa/InvoiceTax). The exact XSD version is a spec Open Question
/// (#2) not resolvable in-repo, so this is a best-effort mapping — <c>TeifVersion</c> is centralised here so
/// it can be pinned once the official XSD is available.
/// </summary>
public class TeifXmlGenerator : ITeifXmlGenerator
{
    private const string TeifVersion = "1.8.8";
    private const string ControllingAgency = "TTN";

    public string Generate(TeifInvoiceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("TEIF",
                new XAttribute("version", TeifVersion),
                new XAttribute("controlingAgency", ControllingAgency),
                BuildHeader(input),
                BuildBody(input)));

        return doc.Declaration + System.Environment.NewLine + doc.ToString(SaveOptions.None);
    }

    private static XElement BuildHeader(TeifInvoiceInput input) =>
        new("InvoiceHeader",
            new XElement("MessageSenderIdentifier",
                new XAttribute("type", "I-01"),
                input.SellerMatriculeFiscal ?? string.Empty),
            new XElement("MessageRecieverIdentifier",
                new XAttribute("type", string.IsNullOrWhiteSpace(input.BuyerMatriculeFiscal) ? "I-02" : "I-01"),
                input.BuyerMatriculeFiscal ?? input.BuyerNationalId ?? string.Empty));

    private static XElement BuildBody(TeifInvoiceInput input)
    {
        var body = new XElement("InvoiceBody",
            // Bgm — document number + type code (380 = commercial invoice).
            new XElement("Bgm",
                new XElement("DocumentIdentifier", input.InvoiceNumber),
                new XElement("DocumentType",
                    new XAttribute("code", input.DocumentTypeCode),
                    "Facture")),
            // Dtm — issue date (format I-31 = ddMMyy per TEIF date qualifiers).
            new XElement("Dtm",
                new XElement("DateText",
                    new XAttribute("format", "ddMMyy"),
                    new XAttribute("functionCode", "I-31"),
                    input.IssueDate.ToString("ddMMyy", CultureInfo.InvariantCulture))),
            BuildPartnerSection(input),
            BuildLineSection(input),
            BuildMonetaryAmounts(input),
            BuildTaxSection(input));

        return body;
    }

    private static XElement BuildPartnerSection(TeifInvoiceInput input) =>
        new("PartnerSection",
            // Seller (function code I-62).
            new XElement("PartnerDetails",
                new XAttribute("functionCode", "I-62"),
                new XElement("Nad",
                    new XElement("PartnerIdentifier",
                        new XAttribute("type", "I-01"),
                        input.SellerMatriculeFiscal ?? string.Empty),
                    new XElement("PartnerName", input.SellerName),
                    new XElement("PartnerAdresses", input.SellerAddress ?? string.Empty))),
            // Buyer (function code I-64). B2C consumer carries a national id (or nothing); B2B carries an MF.
            new XElement("PartnerDetails",
                new XAttribute("functionCode", "I-64"),
                new XElement("Nad",
                    new XElement("PartnerIdentifier",
                        new XAttribute("type", string.IsNullOrWhiteSpace(input.BuyerMatriculeFiscal) ? "I-03" : "I-01"),
                        input.BuyerMatriculeFiscal ?? input.BuyerNationalId ?? string.Empty),
                    new XElement("PartnerName", input.BuyerName))));

    private static XElement BuildLineSection(TeifInvoiceInput input)
    {
        var section = new XElement("LinSection");
        var lineNumber = 1;
        foreach (var line in input.Lines)
        {
            section.Add(new XElement("Lin",
                new XElement("ItemIdentifier", lineNumber.ToString(CultureInfo.InvariantCulture)),
                new XElement("LinImd",
                    new XElement("ItemDescription", line.Designation)),
                new XElement("LinQty",
                    new XElement("Quantity",
                        new XAttribute("measurementUnit", "PCE"),
                        line.Quantity.ToString(CultureInfo.InvariantCulture))),
                new XElement("LinMoa",
                    new XElement("MoaDetails",
                        new XElement("Moa",
                            new XAttribute("amountTypeCode", "I-183"),
                            new XAttribute("currencyCodeList", input.CurrencyCode),
                            Amount(line.UnitPriceHt)),
                        new XElement("Moa",
                            new XAttribute("amountTypeCode", "I-171"),
                            new XAttribute("currencyCodeList", input.CurrencyCode),
                            Amount(line.LineTotalHt))))));
            lineNumber++;
        }
        return section;
    }

    private static XElement BuildMonetaryAmounts(TeifInvoiceInput input) =>
        new("InvoiceMoa",
            new XElement("AmountDetails",
                // I-176 total HT, I-180 total VAT, I-161 stamp duty, I-180... TTC I-180? Use I-179 for TTC.
                Moa("I-176", input.TotalHt, input.CurrencyCode),
                Moa("I-180", input.TotalVat, input.CurrencyCode),
                Moa("I-161", input.StampDutyAmount, input.CurrencyCode),
                Moa("I-179", input.TotalTtc, input.CurrencyCode)));

    private static XElement BuildTaxSection(TeifInvoiceInput input) =>
        new("InvoiceTax",
            new XElement("InvoiceTaxDetails",
                new XElement("Tax",
                    new XElement("TaxTypeName",
                        new XAttribute("code", "I-1602"),
                        "TVA"),
                    new XElement("TaxDetails",
                        new XElement("TaxRate", input.VatApplicable
                            ? input.VatRate.ToString("0.##", CultureInfo.InvariantCulture)
                            : "0"))),
                new XElement("AmountDetails",
                    Moa("I-180", input.TotalVat, input.CurrencyCode))));

    private static XElement Moa(string amountTypeCode, decimal amount, string currency) =>
        new("Moa",
            new XAttribute("amountTypeCode", amountTypeCode),
            new XAttribute("currencyCodeList", currency),
            Amount(amount));

    // TEIF amounts are in the invoice currency with millime precision (3 decimals), invariant format.
    private static string Amount(decimal amount) => amount.ToString("0.000", CultureInfo.InvariantCulture);
}

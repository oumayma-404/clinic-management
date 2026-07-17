using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Generates a TEIF (Tunisian Electronic Invoice Format) XML document from an issued invoice (FR-1).
/// Pure transform: the returned XML is the unsigned TEIF, ready to hand to <see cref="IEInvoiceSigner"/>.
/// </summary>
public interface ITeifXmlGenerator
{
    string Generate(TeifInvoiceInput input);
}

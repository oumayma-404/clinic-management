namespace ClinicManagement.API.Models;

/// <summary>The cabinet's letterhead as PNG bands — what both « Enregistrer » and « Aperçu » send.</summary>
public class UpdateClinicLetterheadRequest
{
    public IFormFile? Header { get; set; }

    /// <summary>Absent = the paper has no footer band.</summary>
    public IFormFile? Footer { get; set; }

    /// <summary>« Page entière »: the strip between the bands. Absent = the bands alone.</summary>
    public IFormFile? Body { get; set; }

    /// <summary>The clinic row's version the client read; ignored by the preview.</summary>
    public uint Version { get; set; }
}

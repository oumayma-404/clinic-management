namespace ClinicManagement.Application.DTOs;

/// <summary>
/// A postal address on the wire — « adresse », « ville », « gouvernorat », « code postal ».
///
/// <para>⚠️ Every field is optional and nullable, mirroring the value object: a patient who says only « Sfax » is
/// an ordinary record. These were non-nullable <c>string = string.Empty</c> while <c>Address</c> demanded all
/// four, which is what let a partial address be dropped on create and throw on update.</para>
/// </summary>
public class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Files;
using Xunit;

namespace ClinicManagement.UnitTests.Common.Files;

/// <summary>
/// <b>The judgement every one of the six upload doors runs through, and which nothing asserted.</b>
///
/// <para>This is the class that stops a <c>.exe</c> renamed <c>.pdf</c>, keeps a <c>.txt</c> renamed
/// <c>.pdf</c> out of a patient's radiographs, and admits an ASCII STL that has no magic bytes at all. Every one
/// of those is a decision with a stated reason in <c>features/patient-file-uploads/spec.md</c>, and every one of
/// them was verified by reading the code and by nothing else — the whole test project mentioned
/// <c>FileUploadValidator</c> exactly zero times.</para>
///
/// <para>⚠️ The stream cases matter as much as the refusals. <c>Rewind</c> is what hands the handler a stream
/// positioned at byte 0 after the header was read off it, so a bug there stores a file missing its first four
/// kilobytes — a corrupted radiograph, written successfully, with no error anywhere.</para>
/// </summary>
public class FileUploadValidatorTests
{
    private static readonly byte[] PdfHeader = Encoding.ASCII.GetBytes("%PDF-1.7\n");
    private static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] JpegHeader = { 0xFF, 0xD8, 0xFF, 0xE0 };

    private static MemoryStream Body(byte[] header, int padTo = 64)
    {
        var bytes = new byte[Math.Max(header.Length, padTo)];
        header.CopyTo(bytes, 0);
        return new MemoryStream(bytes);
    }

    private static Task<ClinicManagement.Application.Common.Models.Result<ValidatedUpload>> Validate(
        FileUploadProfile profile, string fileName, byte[] header, long? declaredLength = null, int padTo = 64)
    {
        var body = Body(header, padTo);
        return FileUploadValidator.ValidateAsync(profile, fileName, declaredLength ?? body.Length, body);
    }

    // ── The happy paths ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Real_Pdf_Is_Accepted_And_Resolves_To_The_Catalogs_Own_Content_Type()
    {
        var result = await Validate(FileUploadProfile.PatientFile, "radio.pdf", PdfHeader);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("application/pdf", result.Value!.Entry.ContentType);
        Assert.Equal("radio.pdf", result.Value.FileName);
    }

    // AC-2.3: an ASCII STL has NO signature — it opens with free text — so a validator whose default arm refuses
    // the unmarked is a validator that cannot accept a 3D impression at all. That was the old shape.
    [Fact]
    public async Task An_Stl_With_No_Marker_At_All_Is_Accepted()
    {
        var ascii = Encoding.ASCII.GetBytes("solid empreinte\n  facet normal 0 0 0\n");

        var result = await Validate(FileUploadProfile.PatientFile, "empreinte.stl", ascii);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(SignatureKind.None, result.Value!.Entry.Signature.Kind);
    }

    // AC-2.4: DICM sits at byte 128, behind a preamble — the offset is the whole reason the rule carries one.
    [Fact]
    public async Task A_Dicom_With_Its_Marker_Behind_The_128_Byte_Preamble_Is_Accepted()
    {
        var bytes = new byte[512];
        Encoding.ASCII.GetBytes("DICM").CopyTo(bytes, 128);

        var result = await FileUploadValidator.ValidateAsync(
            FileUploadProfile.PatientFile, "etude.dcm", bytes.Length, new MemoryStream(bytes));

        Assert.True(result.IsSuccess, result.Error);
    }

    // The same rule's other half: preamble-less exports from real scanners exist, so the marker is Advisory and
    // its absence is not a refusal.
    [Fact]
    public async Task A_Dicom_With_No_Preamble_Is_Accepted_Because_Its_Marker_Is_Advisory()
    {
        var result = await Validate(FileUploadProfile.PatientFile, "etude.dcm", new byte[] { 0x08, 0x00, 0x05, 0x00 }, padTo: 512);

        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>
    /// ⚠️ <b>A real DICOM refused as a renamed file, and the fix is that a format's own marker outranks
    /// another format's claim.</b>
    ///
    /// <para>The DICOM standard leaves the 128-byte preamble <i>entirely unspecified</i> — an exporter may put
    /// another format's header there so one file opens in two applications, and some put a TIFF one. So
    /// <c>II*\0</c> at offset 0 followed by <c>DICM</c> at offset 128 is an ordinary, valid DICOM. The advisory
    /// branch asked « do these bytes claim to be some other format? » <i>before</i> noticing the entry's own
    /// marker was sitting right there, so those files were refused with « le fichier a peut-être été renommé »
    /// about a file nobody had renamed — and the practice has no way to act on that.</para>
    ///
    /// <para>Not hypothetical: measured on two of pydicom's own test files, which carry exactly this preamble.
    /// Both were refused by the running API before this change.</para>
    /// </summary>
    [Fact]
    public async Task A_Dicom_Whose_Preamble_Carries_Another_Formats_Magic_Is_Still_A_Dicom()
    {
        var bytes = new byte[512];
        // The TIFF little-endian marker, in the free space the DICOM standard gives the preamble.
        new byte[] { 0x49, 0x49, 0x2A, 0x00 }.CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("DICM").CopyTo(bytes, 128);

        var result = await FileUploadValidator.ValidateAsync(
            FileUploadProfile.PatientFile, "etude.dcm", bytes.Length, new MemoryStream(bytes));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("application/dicom", result.Value!.ContentType);
    }

    /// <summary>
    /// The other side of that fix, so it cannot be read as « advisory means anything goes »: with the entry's
    /// own marker <b>absent</b>, a positive claim to be another format still refuses.
    /// </summary>
    [Fact]
    public async Task A_Tiff_Renamed_To_Dcm_Is_Still_Refused()
    {
        var bytes = new byte[512];
        new byte[] { 0x49, 0x49, 0x2A, 0x00 }.CopyTo(bytes, 0);
        // No DICM at 128 — this really is just a TIFF wearing the wrong extension.

        var result = await FileUploadValidator.ValidateAsync(
            FileUploadProfile.PatientFile, "faux.dcm", bytes.Length, new MemoryStream(bytes));

        Assert.True(result.IsFailure);
        Assert.Equal(FileUploadValidator.SignatureMismatchMessage, result.Error);
    }

    // ── The refusals, each in its own words ───────────────────────────────────────────────────────────────

    // The reported bug that started `patient-file-uploads`, and the refusal was CORRECT — what was broken was
    // that this sentence never reached the browser.
    [Fact]
    public async Task A_Text_File_Renamed_To_Pdf_Is_Refused_On_Its_Content()
    {
        var result = await Validate(
            FileUploadProfile.PatientFile, "faux.pdf", Encoding.ASCII.GetBytes("ceci n'est pas un PDF"));

        Assert.True(result.IsFailure);
        Assert.Equal(FileUploadValidator.SignatureMismatchMessage, result.Error);
    }

    // AC-2.5: the deny-list is asked FIRST and answers with its own reason — « format non pris en charge » on an
    // executable would read as a gap in the catalogue rather than as a refusal.
    [Fact]
    public async Task An_Executable_Is_Refused_By_The_Deny_List_Not_By_The_Allow_List()
    {
        var result = await Validate(
            FileUploadProfile.PatientFile, "outil.exe", new byte[] { 0x4D, 0x5A, 0x90, 0x00 });

        Assert.True(result.IsFailure);
        Assert.Equal(FileUploadValidator.DeniedMessage, result.Error);
    }

    // SVG renders as markup in the app's own origin, so it is deny-listed rather than merely absent — and the
    // deny-list must win even though nothing in the catalogue would have accepted it either.
    [Fact]
    public async Task An_Svg_Is_Deny_Listed_Rather_Than_Merely_Unsupported()
    {
        var result = await Validate(
            FileUploadProfile.PatientFile, "logo.svg", Encoding.ASCII.GetBytes("<svg xmlns="));

        Assert.True(result.IsFailure);
        Assert.Equal(FileUploadValidator.DeniedMessage, result.Error);
    }

    [Fact]
    public async Task A_Format_The_Door_Does_Not_Accept_Is_Refused_With_The_Doors_Own_List()
    {
        var result = await Validate(FileUploadProfile.PatientFile, "film.mp4", new byte[] { 0x00, 0x00, 0x00, 0x18 });

        Assert.True(result.IsFailure);
        Assert.Equal(FileUploadProfile.PatientFile.UnsupportedMessage, result.Error);
        // AC-2.9 — the sentence names what IS accepted, derived from the profile.
        Assert.Contains(".pdf", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Name_With_No_Extension_Is_Refused_Because_The_Format_Cannot_Be_Determined()
    {
        var result = await Validate(FileUploadProfile.PatientFile, "radiographie", PdfHeader);

        Assert.True(result.IsFailure);
        Assert.Equal(FileUploadValidator.MissingExtensionMessage, result.Error);
    }

    [Fact]
    public async Task An_Empty_File_Is_Refused_Before_Anything_Is_Read()
    {
        var result = await FileUploadValidator.ValidateAsync(
            FileUploadProfile.PatientFile, "vide.pdf", 0, new MemoryStream());

        Assert.True(result.IsFailure);
        Assert.Equal(FileUploadValidator.EmptyFileMessage, result.Error);
    }

    // ── The door's own cap, which is not always the format's ─────────────────────────────────────────────

    [Fact]
    public async Task An_Oversized_File_Is_Refused_Against_The_Formats_Cap()
    {
        var entry = FileTypeCatalog.Pdf;

        var result = await Validate(
            FileUploadProfile.PatientFile, "gros.pdf", PdfHeader, declaredLength: entry.MaxBytes + 1);

        Assert.True(result.IsFailure);
        Assert.Equal(FileUploadValidator.TooLargeMessage(entry.MaxBytes), result.Error);
    }

    /// <summary>
    /// ⚠️ <b>The door's cap, not the entry's.</b> PNG and JPEG are shared between the patient's file drawer —
    /// where a fifty-megabyte panoramique is ordinary — and the cachet and clinic logo, which are read fully into
    /// memory on every rendered document. Without a per-door cap those are one number, and raising it for the
    /// radiograph raises it for the letterhead.
    /// </summary>
    [Fact]
    public async Task A_Profile_Image_Is_Capped_By_Its_Door_Even_Though_The_Format_Allows_More()
    {
        var entry = FileTypeCatalog.Png;
        var doorCap = FileUploadProfile.ProfileImage.CapFor(entry);

        Assert.True(doorCap < entry.MaxBytes, "This test is vacuous unless the door is genuinely tighter.");

        var refused = await Validate(
            FileUploadProfile.ProfileImage, "cachet.png", PngHeader, declaredLength: doorCap + 1);
        Assert.True(refused.IsFailure);
        Assert.Equal(FileUploadValidator.TooLargeMessage(doorCap), refused.Error);

        // The same size on the patient drawer's door is perfectly acceptable — that is the point of the split.
        var accepted = await Validate(
            FileUploadProfile.PatientFile, "panoramique.png", PngHeader, declaredLength: doorCap + 1);
        Assert.True(accepted.IsSuccess, accepted.Error);
    }

    [Fact]
    public async Task A_Jpeg_Cachet_Is_Accepted_Because_The_Profile_Takes_Both_Raster_Formats()
    {
        var result = await Validate(FileUploadProfile.ProfileImage, "cachet.jpg", JpegHeader);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("image/jpeg", result.Value!.Entry.ContentType);
    }

    [Fact]
    public async Task The_Csv_Door_Also_Takes_A_Txt_Export()
    {
        var result = await Validate(
            FileUploadProfile.Csv, "patients.txt", Encoding.UTF8.GetBytes("nom;prenom\r\n"));

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task The_Csv_Door_Refuses_A_Radiograph()
    {
        var result = await Validate(FileUploadProfile.Csv, "radio.png", PngHeader);

        Assert.True(result.IsFailure);
        Assert.Equal(FileUploadProfile.Csv.UnsupportedMessage, result.Error);
    }

    // ── The stream the handler is handed back ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ The failure this guards is silent and permanent: a body handed back positioned past the header stores a
    /// file missing its first bytes. It succeeds, it is served back, and the radiograph is simply corrupt.
    /// </summary>
    [Fact]
    public async Task The_Body_Handed_Back_Still_Starts_At_The_First_Byte()
    {
        var payload = new byte[8192];
        PdfHeader.CopyTo(payload, 0);
        for (var i = PdfHeader.Length; i < payload.Length; i++) payload[i] = (byte)(i % 251);

        var result = await FileUploadValidator.ValidateAsync(
            FileUploadProfile.PatientFile, "grand.pdf", payload.Length, new MemoryStream(payload));

        Assert.True(result.IsSuccess, result.Error);

        using var read = new MemoryStream();
        await result.Value!.Content.CopyToAsync(read);

        Assert.Equal(payload, read.ToArray());
        Assert.Equal(payload.Length, result.Value.ByteLength);
    }

    /// <summary>
    /// The same, for a stream that cannot seek — which is what an `IFormFile` becomes once anything upstream has
    /// wrapped it. `Rewind` has to re-prefix the header rather than rely on `Position = 0`.
    /// </summary>
    [Fact]
    public async Task A_Non_Seekable_Body_Is_Also_Handed_Back_Whole()
    {
        var payload = new byte[6000];
        PdfHeader.CopyTo(payload, 0);
        for (var i = PdfHeader.Length; i < payload.Length; i++) payload[i] = (byte)(i % 97);

        var result = await FileUploadValidator.ValidateAsync(
            FileUploadProfile.PatientFile, "flux.pdf", payload.Length, new ForwardOnlyStream(payload));

        Assert.True(result.IsSuccess, result.Error);

        using var read = new MemoryStream();
        await result.Value!.Content.CopyToAsync(read);

        Assert.Equal(payload, read.ToArray());
    }

    // ── The name that gets stored ─────────────────────────────────────────────────────────────────────────

    // AC-2.10 — the stored name is handed to `File(..., fileDownloadName)`, so a path segment in it is not a
    // cosmetic problem.
    [Fact]
    public async Task A_Name_Carrying_Path_Segments_Is_Sanitised_Before_It_Is_Stored()
    {
        var result = await Validate(FileUploadProfile.PatientFile, @"..\..\etc\passwd.pdf", PdfHeader);

        Assert.True(result.IsSuccess, result.Error);
        Assert.DoesNotContain("/", result.Value!.FileName, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\", result.Value.FileName, StringComparison.Ordinal);
        Assert.DoesNotContain("..", result.Value.FileName, StringComparison.Ordinal);
    }

    /// <summary>A stream that refuses to seek, the shape `Rewind` exists for.</summary>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly MemoryStream _inner;

        public ForwardOnlyStream(byte[] bytes) => _inner = new MemoryStream(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

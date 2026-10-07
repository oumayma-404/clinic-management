using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Features.Clinics;
using ClinicManagement.UnitTests.Common;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// What a letterhead band must be to print well: a PNG, at least 150 dpi across 210 mm, and no taller than a third
/// (header) or a sixth (footer) of the page once mapped at true scale. Each refusal carries its own code.
/// </summary>
public class LetterheadBandTests
{
    [Fact]
    public void PngDimensions_Reads_Width_And_Height_From_The_Ihdr()
    {
        Assert.Equal((2480, 400), PngDimensions.Read(TestPng.White(2480, 400)));
    }

    [Fact]
    public void PngDimensions_Refuses_Bytes_That_Are_Not_A_Png()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0 };

        Assert.Null(PngDimensions.Read(jpeg));
    }

    [Fact]
    public void PngDimensions_Refuses_A_Truncated_Header()
    {
        Assert.Null(PngDimensions.Read(TestPng.White(2480, 400)[..20]));
    }

    [Fact]
    public void PngDimensions_Refuses_A_Zero_Width()
    {
        var png = TestPng.White(2480, 400);
        png[16] = png[17] = png[18] = png[19] = 0;

        Assert.Null(PngDimensions.Read(png));
    }

    [Fact]
    public async Task A_Sharp_Header_Within_A_Third_Of_The_Page_Is_Accepted()
    {
        var png = TestPng.White(2480, 1169); // 99 mm, the cap itself

        var result = await Read(LetterheadBandKind.Header, png);

        Assert.True(result.IsSuccess);
        Assert.Equal(png, result.Value);
    }

    [Fact]
    public async Task A_Band_Narrower_Than_150_Dpi_Is_Refused_As_Too_Small()
    {
        var result = await Read(LetterheadBandKind.Header, TestPng.White(900, 150));

        Assert.True(result.IsFailure);
        Assert.Equal(LetterheadRules.TooSmallCode, result.Code);
        Assert.Contains("900 px", result.Error);
    }

    [Fact]
    public async Task A_Header_Taller_Than_A_Third_Of_The_Page_Is_Refused()
    {
        var result = await Read(LetterheadBandKind.Header, TestPng.White(2480, 1300)); // 110 mm

        Assert.True(result.IsFailure);
        Assert.Equal(LetterheadRules.HeaderTooTallCode, result.Code);
    }

    [Fact]
    public async Task The_Same_Band_Is_Refused_As_A_Footer_Above_A_Sixth_Of_The_Page()
    {
        var png = TestPng.White(2480, 700); // 59 mm: a valid header, too tall for a footer

        Assert.True((await Read(LetterheadBandKind.Header, png)).IsSuccess);
        var footer = await Read(LetterheadBandKind.Footer, png);

        Assert.True(footer.IsFailure);
        Assert.Equal(LetterheadRules.FooterTooTallCode, footer.Code);
    }

    [Fact]
    public async Task A_Jpeg_Is_Refused_At_The_Door_Before_Its_Size_Is_Read()
    {
        var jpeg = new byte[64];
        jpeg[0] = 0xFF; jpeg[1] = 0xD8; jpeg[2] = 0xFF;

        var result = await LetterheadBandReader.ReadAsync(
            LetterheadBandKind.Header, new MemoryStream(jpeg), "entete.jpg", jpeg.Length, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.NotEqual(LetterheadRules.TooSmallCode, result.Code);
    }

    [Fact]
    public async Task The_Page_Body_Has_No_Height_Cap_Since_It_Is_Stretched_To_Fit()
    {
        var result = await Read(LetterheadBandKind.Body, TestPng.White(2480, 2400)); // 203 mm

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_Blurred_Page_Body_Is_Refused_Like_A_Band()
    {
        var result = await Read(LetterheadBandKind.Body, TestPng.White(900, 800));

        Assert.True(result.IsFailure);
        Assert.Equal(LetterheadRules.TooSmallCode, result.Code);
    }

    [Fact]
    public void Printed_Height_Maps_The_Band_Width_To_210_Mm()
    {
        Assert.Equal(40, LetterheadRules.PrintedHeightMm(2480, 2480 * 40 / 210), precision: 0);
    }

    private static Task<ClinicManagement.Application.Common.Models.Result<byte[]>> Read(LetterheadBandKind kind, byte[] png) =>
        LetterheadBandReader.ReadAsync(kind, new MemoryStream(png), "entete.png", png.Length, CancellationToken.None);
}

using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Clinics;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.UnitTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// Saving and withdrawing the cabinet's letterhead. The property that matters most is what is NEVER deleted: a
/// document issued on a letterhead keeps pointing at its bands, so a new save writes under new keys and a removal
/// touches no blob — only a save that failed cleans up the blobs it had just written.
/// </summary>
public class ClinicLetterheadCommandTests
{
    private const string UserId = "local|admin";
    private static readonly Guid ClinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<string> _uploadedPaths = new();

    public ClinicLetterheadCommandTests()
    {
        _storage
            .Setup(s => s.UploadAsync(It.IsAny<Stream>(), "image/png", ClinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, Guid, string, CancellationToken>((_, _, _, path, _) => _uploadedPaths.Add(path))
            .ReturnsAsync((Stream _, string _, Guid clinic, string path, CancellationToken _) => $"clinics/{clinic}/{path}");
    }

    private UpdateClinicLetterheadCommandHandler UpdateHandler() =>
        new(_clinics.Object, _users.Object, _context.Object, _storage.Object, _uow.Object,
            NullLogger<UpdateClinicLetterheadCommandHandler>.Instance);

    private RemoveClinicLetterheadCommandHandler RemoveHandler() =>
        new(_clinics.Object, _users.Object, _context.Object, _uow.Object,
            NullLogger<RemoveClinicLetterheadCommandHandler>.Instance);

    private Clinic SignedInAs(string role)
    {
        var user = new User(UserId, ClinicId, role, "salma@clinic.tn", "Salma");
        var clinic = new Clinic(ClinicId, "Cabinet Dentaire");
        _context.Setup(c => c.GetUserId()).Returns(UserId);
        _users.Setup(r => r.GetByAuth0SubAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        return clinic;
    }

    private static UpdateClinicLetterheadCommand Save(byte[] header, byte[]? footer = null, uint version = 0, byte[]? body = null)
    {
        var command = new UpdateClinicLetterheadCommand
        {
            Header = new MemoryStream(header),
            HeaderFileName = "entete.png",
            HeaderLength = header.Length,
            Version = version
        };
        if (footer != null)
        {
            command.Footer = new MemoryStream(footer);
            command.FooterFileName = "pied-de-page.png";
            command.FooterLength = footer.Length;
        }
        if (body != null)
        {
            command.Body = new MemoryStream(body);
            command.BodyFileName = "page.png";
            command.BodyLength = body.Length;
        }

        return command;
    }

    private static readonly byte[] Header = TestPng.White(2480, 400);
    private static readonly byte[] Footer = TestPng.White(2480, 200);
    private static readonly byte[] Body = TestPng.White(2480, 2900);

    [Fact]
    public async Task A_Page_Entiere_Save_Stores_The_Body_Beside_The_Bands_Of_The_Same_Upload()
    {
        var clinic = SignedInAs("admin");

        var result = await UpdateHandler().Handle(Save(Header, Footer, body: Body), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, _uploadedPaths.Count);
        var batch = _uploadedPaths[0].Split('/')[1];
        Assert.Equal($"letterhead/{batch}/body", _uploadedPaths[2]);
        Assert.Equal($"clinics/{ClinicId}/letterhead/{batch}/body", clinic.LetterheadBodyStorageKey);
        Assert.True(result.Value!.HasBody);
    }

    [Fact]
    public async Task A_Bands_Only_Save_Drops_A_Previous_Page_Body()
    {
        var clinic = SignedInAs("admin");
        clinic.SetLetterhead("clinics/old/letterhead/a/header", null, "clinics/old/letterhead/a/body");

        var result = await UpdateHandler().Handle(Save(Header), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(clinic.LetterheadBodyStorageKey);
        Assert.False(result.Value!.HasBody);
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Refused_Body_Leaves_No_Band_Blob_Behind()
    {
        var clinic = SignedInAs("admin");

        var result = await UpdateHandler().Handle(Save(Header, Footer, body: TestPng.White(900, 800)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LetterheadRules.TooSmallCode, result.Code);
        Assert.Empty(_uploadedPaths);
        Assert.Null(clinic.LetterheadHeaderStorageKey);
    }

    [Fact]
    public void The_Revision_Changes_When_Only_The_Body_Is_Added()
    {
        var clinic = new Clinic(ClinicId, "Cabinet Dentaire");
        clinic.SetLetterhead("clinics/x/letterhead/a/header", null);
        var before = ClinicLetterheadDto.From(clinic).Revision;

        clinic.SetLetterhead("clinics/x/letterhead/a/header", null, "clinics/x/letterhead/a/body");

        Assert.NotEqual(before, ClinicLetterheadDto.From(clinic).Revision);
    }

    [Fact]
    public async Task A_Save_Stores_Both_Bands_Under_One_New_Upload_And_Checks_The_Version()
    {
        var clinic = SignedInAs("admin");

        var result = await UpdateHandler().Handle(Save(Header, Footer, version: 41), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _uploadedPaths.Count);
        var batch = _uploadedPaths[0].Split('/')[1];
        Assert.Equal($"letterhead/{batch}/header", _uploadedPaths[0]);
        Assert.Equal($"letterhead/{batch}/footer", _uploadedPaths[1]);
        Assert.Equal($"clinics/{ClinicId}/letterhead/{batch}/header", clinic.LetterheadHeaderStorageKey);
        Assert.Equal($"clinics/{ClinicId}/letterhead/{batch}/footer", clinic.LetterheadFooterStorageKey);
        Assert.True(result.Value!.HasHeader);
        Assert.True(result.Value.HasFooter);
        _uow.Verify(u => u.SetExpectedVersion(clinic, 41u), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_New_Save_Never_Deletes_The_Bands_It_Replaces()
    {
        var clinic = SignedInAs("admin");
        clinic.SetLetterhead("clinics/old/letterhead/a/header", "clinics/old/letterhead/a/footer");

        var result = await UpdateHandler().Handle(Save(Header, Footer), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual("clinics/old/letterhead/a/header", clinic.LetterheadHeaderStorageKey);
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Save_Without_A_Footer_Drops_The_Previous_Footer()
    {
        var clinic = SignedInAs("admin");
        clinic.SetLetterhead("clinics/old/letterhead/a/header", "clinics/old/letterhead/a/footer");

        var result = await UpdateHandler().Handle(Save(Header), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(_uploadedPaths);
        Assert.Null(clinic.LetterheadFooterStorageKey);
        Assert.False(result.Value!.HasFooter);
    }

    [Fact]
    public async Task Each_Save_Gets_A_Different_Revision()
    {
        SignedInAs("admin");

        var first = await UpdateHandler().Handle(Save(Header), CancellationToken.None);
        var second = await UpdateHandler().Handle(Save(Header), CancellationToken.None);

        Assert.NotNull(first.Value!.Revision);
        Assert.NotEqual(first.Value.Revision, second.Value!.Revision);
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("secretary")]
    public async Task Only_An_Admin_May_Save_And_Nothing_Is_Stored_Otherwise(string role)
    {
        var clinic = SignedInAs(role);

        var result = await UpdateHandler().Handle(Save(Header, Footer), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LetterheadRules.AdminOnly, result.Error);
        Assert.Empty(_uploadedPaths);
        Assert.Null(clinic.LetterheadHeaderStorageKey);
    }

    [Fact]
    public async Task A_Refused_Footer_Leaves_No_Header_Blob_Behind()
    {
        var clinic = SignedInAs("admin");

        var result = await UpdateHandler().Handle(Save(Header, footer: TestPng.White(2480, 700)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LetterheadRules.FooterTooTallCode, result.Code);
        Assert.Empty(_uploadedPaths);
        Assert.Null(clinic.LetterheadHeaderStorageKey);
    }

    [Fact]
    public async Task A_Blurred_Header_Is_Refused_With_Its_Code()
    {
        SignedInAs("admin");

        var result = await UpdateHandler().Handle(Save(TestPng.White(900, 150)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LetterheadRules.TooSmallCode, result.Code);
        Assert.Empty(_uploadedPaths);
    }

    [Fact]
    public async Task A_Failed_Save_Deletes_The_Blobs_It_Had_Just_Written()
    {
        SignedInAs("admin");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db save failed"));

        var result = await UpdateHandler().Handle(Save(Header, Footer), CancellationToken.None);

        Assert.True(result.IsFailure);
        foreach (var path in _uploadedPaths)
        {
            _storage.Verify(s => s.DeleteAsync($"clinics/{ClinicId}/{path}", It.IsAny<CancellationToken>()), Times.Once);
        }
        Assert.Equal(2, _uploadedPaths.Count);
    }

    [Fact]
    public async Task A_Version_Conflict_Escapes_After_Cleaning_Up_The_New_Blobs()
    {
        SignedInAs("admin");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("stale"));

        await Assert.ThrowsAsync<ConflictException>(() =>
            UpdateHandler().Handle(Save(Header, Footer, version: 7), CancellationToken.None));

        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Removing_Returns_To_The_Text_Header_And_Deletes_No_Blob()
    {
        var clinic = SignedInAs("admin");
        clinic.SetLetterhead("clinics/x/letterhead/a/header", "clinics/x/letterhead/a/footer", "clinics/x/letterhead/a/body");

        var result = await RemoveHandler().Handle(new RemoveClinicLetterheadCommand { Version = 12 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(clinic.LetterheadHeaderStorageKey);
        Assert.Null(clinic.LetterheadFooterStorageKey);
        Assert.Null(clinic.LetterheadBodyStorageKey);
        Assert.False(result.Value!.HasHeader);
        Assert.Null(result.Value.Revision);
        _uow.Verify(u => u.SetExpectedVersion(clinic, 12u), Times.Once);
        _storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("secretary")]
    public async Task Only_An_Admin_May_Remove(string role)
    {
        var clinic = SignedInAs(role);
        clinic.SetLetterhead("clinics/x/letterhead/a/header", null);

        var result = await RemoveHandler().Handle(new RemoveClinicLetterheadCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LetterheadRules.AdminOnly, result.Error);
        Assert.Equal("clinics/x/letterhead/a/header", clinic.LetterheadHeaderStorageKey);
    }

    [Fact]
    public void A_Letterhead_Cannot_Be_Set_Without_Its_Header()
    {
        var clinic = new Clinic(ClinicId, "Cabinet Dentaire");

        Assert.Throws<ArgumentException>(() => clinic.SetLetterhead(" ", "clinics/x/footer"));
    }
}

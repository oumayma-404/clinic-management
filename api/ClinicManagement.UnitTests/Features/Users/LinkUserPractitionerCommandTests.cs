using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Users;
using ClinicManagement.Application.Features.Users.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Users;

/// <summary>
/// [cabinet-de-groupe US-3] One practitioner record per dentist: « Lier à un praticien » links an existing record or
/// creates one, never re-points an account that has one, and a record linked to somebody else cannot be taken.
/// </summary>
public class LinkUserPractitionerCommandTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<Doctor> _added = new();
    private readonly User _admin = User.CreateLocalUser(ClinicId, User.RoleAdmin, "owner@cabinet.tn", "hash", "Le titulaire");

    public LinkUserPractitionerCommandTests()
    {
        _context.Setup(c => c.GetUserId()).Returns(_admin.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(_admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_admin);
        _doctors.Setup(r => r.AddAsync(It.IsAny<Doctor>(), It.IsAny<CancellationToken>()))
            .Callback<Doctor, CancellationToken>((d, _) => _added.Add(d))
            .Returns(Task.CompletedTask);
    }

    private User Account(string role)
    {
        var user = User.CreateLocalUser(ClinicId, role, $"{role}@cabinet.tn", "hash", "Dr Nadia Trabelsi");
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    private Doctor Record(string? linkedTo = null)
    {
        var doctor = new Doctor(Guid.NewGuid(), ClinicId, "Nadia", "Trabelsi", "Dentiste");
        if (linkedTo != null) doctor.LinkToUser(linkedTo);
        _doctors.Setup(r => r.GetByIdAsync(doctor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doctor);
        return doctor;
    }

    private LinkUserPractitionerCommandHandler Handler() =>
        new(_users.Object, _doctors.Object, _context.Object, _uow.Object, NullLogger<LinkUserPractitionerCommandHandler>.Instance);

    [Fact]
    public async Task An_Admin_Dentist_Is_Linked_To_The_Record_Already_On_The_Roster()
    {
        var account = Account(User.RoleAdmin);
        var record = Record();

        var result = await Handler().Handle(
            new LinkUserPractitionerCommand { TargetUserId = account.Id, ExistingDoctorId = record.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(account.Id, record.UserId);
        Assert.Empty(_added);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Account_With_No_Record_Can_Be_Given_A_New_One()
    {
        var account = Account(User.RoleDoctor);

        var result = await Handler().Handle(new LinkUserPractitionerCommand
        {
            TargetUserId = account.Id,
            DoctorInfo = new DoctorPersonalInfoDto { FirstName = "Nadia", LastName = "Trabelsi", Specialty = "Dentiste" },
        }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(account.Id, Assert.Single(_added).UserId);
    }

    [Fact]
    public async Task An_Account_That_Already_Has_A_Record_Is_Never_Re_Pointed()
    {
        var account = Account(User.RoleDoctor);
        _doctors.Setup(r => r.GetByUserIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Record(account.Id));

        var result = await Handler().Handle(
            new LinkUserPractitionerCommand { TargetUserId = account.Id, ExistingDoctorId = Record().Id }, default);

        Assert.Equal(PractitionerLink.AlreadyLinkedCode, result.Code);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Colleagues_Record_Cannot_Be_Taken()
    {
        var account = Account(User.RoleDoctor);
        var colleagues = Record(linkedTo: "local|colleague");

        var result = await Handler().Handle(
            new LinkUserPractitionerCommand { TargetUserId = account.Id, ExistingDoctorId = colleagues.Id }, default);

        Assert.Equal(PractitionerLink.UnavailableCode, result.Code);
        Assert.Equal("local|colleague", colleagues.UserId);
    }

    [Fact]
    public async Task A_Secretary_Is_Not_A_Practitioner()
    {
        var account = Account(User.RoleSecretary);

        var result = await Handler().Handle(
            new LinkUserPractitionerCommand { TargetUserId = account.Id, ExistingDoctorId = Record().Id }, default);

        Assert.Equal(PractitionerLink.NotAPractitionerMessage, result.Error);
    }

    [Fact]
    public void A_Record_Linked_To_One_Account_Refuses_Another()
    {
        var doctor = new Doctor(Guid.NewGuid(), ClinicId, "Nadia", "Trabelsi", "Dentiste");
        doctor.LinkToUser("local|first");

        doctor.LinkToUser("local|first"); // the same account again is a no-op
        Assert.Throws<InvalidOperationException>(() => doctor.LinkToUser("local|second"));
        Assert.Equal("local|first", doctor.UserId);
    }

    [Fact]
    public void A_Promotion_Links_Only_The_One_Unlinked_Record_With_The_Same_E_Mail()
    {
        Doctor With(string? email, string? userId = null)
        {
            var d = new Doctor(Guid.NewGuid(), ClinicId, "Nadia", "Trabelsi", "Dentiste", email: email);
            if (userId != null) d.LinkToUser(userId);
            return d;
        }

        var match = With("Nadia@Cabinet.tn");
        Assert.Same(match, PractitionerLink.SoleUnlinkedWithEmail(
            new[] { match, With("other@cabinet.tn"), With("nadia@cabinet.tn", userId: "local|taken") }, "nadia@cabinet.tn"));

        // Two candidates is a guess, and a guess never ties a record.
        Assert.Null(PractitionerLink.SoleUnlinkedWithEmail(new[] { With("nadia@cabinet.tn"), With("nadia@cabinet.tn") }, "nadia@cabinet.tn"));
        Assert.Null(PractitionerLink.SoleUnlinkedWithEmail(new[] { With(null) }, "nadia@cabinet.tn"));
    }
}

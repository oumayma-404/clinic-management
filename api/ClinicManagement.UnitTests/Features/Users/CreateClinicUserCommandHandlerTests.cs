using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Users.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Users;

/// <summary>
/// Admin-created staff accounts (multi-tenant-cloud US-3, Part C).
///
/// <para><b>Why the command had to exist.</b> Until now the only way a second person got an account was
/// <c>POST /api/auth/register</c> — self-registration behind the clinic's six-character join code, which the
/// hosted profile closes (<c>DeploymentProfile.AllowsSelfRegistration</c>). <c>UsersController</c> exposed
/// exactly <c>GET</c>, <c>{id}/reset-password</c>, <c>{id}/status</c> and <c>{id}/role</c>, every one of which
/// operates on an account somebody else had already created — so without this, a hosted clinic could not add
/// staff at all.</para>
/// </summary>
public class CreateClinicUserCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private sealed class Harness
    {
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IDoctorRepository> Doctors { get; } = new();
        public Mock<IClinicContext> ClinicContext { get; } = new();
        public Mock<ILocalAuthService> LocalAuth { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public List<User> Added { get; } = new();
        public List<Doctor> AddedDoctors { get; } = new();

        public Harness(User? caller)
        {
            Doctors.Setup(r => r.AddAsync(It.IsAny<Doctor>(), It.IsAny<CancellationToken>()))
                .Callback<Doctor, CancellationToken>((d, _) => AddedDoctors.Add(d))
                .Returns(Task.CompletedTask);

            ClinicContext.Setup(c => c.GetUserId()).Returns(caller?.Id ?? "local|caller");
            Users.Setup(r => r.GetByAuth0SubAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(caller);
            Users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((User?)null);
            Users.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .Callback<User, CancellationToken>((u, _) => Added.Add(u))
                .Returns(Task.CompletedTask);

            LocalAuth.Setup(a => a.GenerateTemporaryPassword()).Returns("Temp-Pass-42");
            LocalAuth.Setup(a => a.HashPassword(It.IsAny<string>())).Returns<string>(p => $"hash({p})");
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public CreateClinicUserCommandHandler Handler() => new(
            Users.Object,
            Doctors.Object,
            ClinicContext.Object,
            LocalAuth.Object,
            UnitOfWork.Object,
            NullLogger<CreateClinicUserCommandHandler>.Instance);
    }

    private static User Admin(Guid clinicId) =>
        User.CreateLocalUser(clinicId, User.RoleAdmin, "admin@cabinet.tn", "hash", "L'administrateur");

    private static CreateClinicUserCommand Command(string role = "secretary") => new()
    {
        Email = "assistante@cabinet.tn",
        FullName = "Amira Trabelsi",
        Role = role
    };

    // [US-3] The happy path. The password is returned once, and the account is forced to replace it.
    [Fact]
    public async Task It_creates_an_active_account_with_a_one_time_password()
    {
        var harness = new Harness(Admin(ClinicId));

        var result = await harness.Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Temp-Pass-42", result.Value!.TemporaryPassword);

        var created = Assert.Single(harness.Added);
        Assert.Equal(ClinicId, created.ClinicId);
        Assert.Equal(User.RoleSecretary, created.Role);
        Assert.Equal("assistante@cabinet.tn", created.Email);
        Assert.True(created.MustChangePassword);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// [US-3] ⚠️ Active, unlike <see cref="User.CreateSelfRegistered"/>. The two differ in who vouched for whom:
    /// a self-registration is a stranger asking to be let in and waits for approval (I5); this one <i>is</i> the
    /// approval. A pending account here would ask the same admin to approve their own action.
    /// </summary>
    [Fact]
    public async Task The_account_is_active_immediately_unlike_a_self_registration()
    {
        var harness = new Harness(Admin(ClinicId));

        await harness.Handler().Handle(Command(), CancellationToken.None);

        var created = Assert.Single(harness.Added);
        Assert.True(created.IsActive);
        Assert.False(created.IsPendingActivation);
    }

    /// <summary>
    /// [US-3] The clinic comes from the caller's own DB record and is never taken from the request — an admin
    /// creates staff for their own practice and nowhere else. There is no clinic id on the command at all, which
    /// is what makes that true by construction rather than by a check.
    /// </summary>
    [Fact]
    public async Task The_new_account_lands_in_the_callers_own_clinic()
    {
        var harness = new Harness(Admin(OtherClinicId));

        await harness.Handler().Handle(Command(), CancellationToken.None);

        Assert.Equal(OtherClinicId, Assert.Single(harness.Added).ClinicId);
    }

    /// <summary>
    /// [US-3] The controller policy is <c>AdminOnly</c>, but the DB role is authoritative everywhere in this
    /// codebase: a JWT minted before a demotion still carries the old role until it expires.
    /// </summary>
    [Fact]
    public async Task A_non_admin_is_refused_even_though_the_policy_let_them_through()
    {
        var doctor = User.CreateLocalUser(ClinicId, User.RoleDoctor, "doc@cabinet.tn", "hash", "Le docteur");
        var harness = new Harness(doctor);

        var result = await harness.Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("administrateurs", result.Error);
        Assert.Empty(harness.Added);
    }

    [Fact]
    public async Task An_unresolvable_caller_is_refused()
    {
        var harness = new Harness(caller: null);

        var result = await harness.Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(harness.Added);
    }

    // [US-3] Validated against User.AssignableRoles — the closed set. An unrecognised value used to be accepted
    // silently by the self-registration paths, producing an account that matched no policy at all (A-11).
    [Theory]
    [InlineData("")]
    [InlineData("owner")]
    [InlineData("Admin ")]
    public async Task A_role_outside_the_closed_set_is_refused(string role)
    {
        var harness = new Harness(Admin(ClinicId));

        var result = await harness.Handler().Handle(Command(role), CancellationToken.None);

        // « Admin » with a trailing space is the one that must SUCCEED — NormalizeRole trims and case-folds, so
        // this theory doubles as the canonicalisation case.
        if (role.Trim().Equals(User.RoleAdmin, StringComparison.OrdinalIgnoreCase))
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(User.RoleAdmin, Assert.Single(harness.Added).Role);
            return;
        }

        Assert.True(result.IsFailure);
        Assert.Contains("Rôle invalide", result.Error);
        Assert.Empty(harness.Added);
    }

    /// <summary>
    /// [US-3] The partial unique index on the lowercased email would otherwise surface as a 500. Checked across
    /// every clinic deliberately: a local account is identified by its email alone at login, so a second clinic
    /// reusing one would make the two indistinguishable to the query that resolves them.
    /// </summary>
    [Fact]
    public async Task An_email_that_already_has_an_account_is_refused()
    {
        var harness = new Harness(Admin(ClinicId));
        harness.Users.Setup(r => r.GetByEmailAsync("assistante@cabinet.tn", It.IsAny<CancellationToken>()))
            .ReturnsAsync(User.CreateLocalUser(OtherClinicId, User.RoleSecretary, "assistante@cabinet.tn", "h", "X"));

        var result = await harness.Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        // The fixture's collision is in ANOTHER clinic, so the refusal must not confirm the address is taken —
        // that would be a cross-tenant oracle (review finding 25). The two wordings have their own cases below.
        Assert.Contains("ne peut pas être utilisé", result.Error);
        Assert.Empty(harness.Added);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("", "Amira Trabelsi")]
    [InlineData("not-an-email", "Amira Trabelsi")]
    [InlineData("assistante@cabinet.tn", "   ")]
    public async Task A_missing_or_malformed_identity_is_refused_in_French(string email, string fullName)
    {
        var harness = new Harness(Admin(ClinicId));
        var command = new CreateClinicUserCommand { Email = email, FullName = fullName, Role = "secretary" };

        var result = await harness.Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(harness.Added);
    }

    /// <summary>
    /// [US-3] The A-8 defect class: a failure must never echo server internals back to a caller. Here the raw
    /// exception would carry the email that was being created.
    /// </summary>
    [Fact]
    public async Task An_unexpected_failure_returns_a_French_message_and_not_the_exception()
    {
        var harness = new Harness(Admin(ClinicId));
        harness.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("23505: duplicate key value violates unique constraint"));

        var result = await harness.Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("23505", result.Error);
        Assert.Contains("Erreur lors de la création du compte", result.Error);
    }

    // ---- The doctor role creates and links a practitioner (review finding 4) ----

    /// <summary>
    /// The defect this closes was silent and its symptom was three screens away: a doctor account with no
    /// <see cref="Doctor"/> row is absent from the practitioner roster, has nothing for « Mon profil » to edit,
    /// leaves <c>PractitionerAttribution</c>'s caller fall-back resolving to null so the money it collects is
    /// unattributed, and makes <c>PractitionerRenderSnapshot</c> stamp certificats and ordonnances with **no**
    /// practitioner identity — on the one profile where this command is the only way to add a dentist.
    /// </summary>
    [Fact]
    public async Task A_doctor_account_gets_a_linked_practitioner_record()
    {
        var harness = new Harness(Admin(ClinicId));
        var command = Command("doctor");
        command.DoctorInfo = new DoctorPersonalInfoDto
        {
            FirstName = "Bechir",
            LastName = "Ben Salah",
            Specialty = "Dentiste",
            Phone = "71234567"
        };

        var result = await harness.Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var created = Assert.Single(harness.Added);
        var doctor = Assert.Single(harness.AddedDoctors);
        Assert.Equal(ClinicId, doctor.ClinicId);
        Assert.Equal("Bechir", doctor.FirstName);
        Assert.Equal("Ben Salah", doctor.LastName);
        // The link is the load-bearing part: without it « Mon profil » and every attribution read find nothing.
        Assert.Equal(created.Id, doctor.UserId);
        // Both rows in ONE save — an account whose practitioner failed to commit is the half-created state this fixes.
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null, "Ben Salah", "Dentiste")]
    [InlineData("Bechir", null, "Dentiste")]
    [InlineData("Bechir", "Ben Salah", null)]
    public async Task A_doctor_role_without_a_practitioner_is_refused_in_French(
        string? firstName, string? lastName, string? specialty)
    {
        var harness = new Harness(Admin(ClinicId));
        var command = Command("doctor");
        command.DoctorInfo = new DoctorPersonalInfoDto
        {
            FirstName = firstName!,
            LastName = lastName!,
            Specialty = specialty!
        };

        var result = await harness.Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("praticien", result.Error);
        // Refused BEFORE anything is written: a user row with no doctor is exactly what must not survive.
        Assert.Empty(harness.Added);
        Assert.Empty(harness.AddedDoctors);
    }

    [Fact]
    public async Task A_doctor_role_with_no_practitioner_object_at_all_is_refused()
    {
        var harness = new Harness(Admin(ClinicId));

        var result = await harness.Handler().Handle(Command("doctor"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(harness.Added);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("secretary")]
    public async Task A_non_practitioner_role_creates_no_doctor_record(string role)
    {
        // An admin or a secretary is not a practitioner, so DoctorInfo is ignored rather than required.
        var harness = new Harness(Admin(ClinicId));

        var result = await harness.Handler().Handle(Command(role), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(harness.AddedDoctors);
    }

    // ---- The email refusal must not be a cross-tenant oracle (review finding 25) ----

    /// <summary>
    /// The uniqueness check is global on purpose (login resolves an account by email alone), but on a hosted backend
    /// serving competing practices a refusal that says « taken » for an address belonging to *another* clinic turns
    /// this endpoint into an oracle any clinic admin can walk against a list of addresses.
    /// </summary>
    [Fact]
    public async Task An_email_taken_by_another_clinic_is_refused_without_disclosing_that()
    {
        var harness = new Harness(Admin(ClinicId));
        harness.Users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(User.CreateLocalUser(
                OtherClinicId, User.RoleDoctor, "assistante@cabinet.tn", "hash", "Quelqu'un d'autre"));

        var result = await harness.Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("existe déjà", result.Error);
        Assert.Contains("ne peut pas être utilisé", result.Error);
        Assert.Empty(harness.Added);
    }

    [Fact]
    public async Task An_email_taken_inside_the_callers_own_clinic_says_so()
    {
        // Here the admin can see the colleague on their own « Utilisateurs » screen, so naming the collision
        // discloses nothing and is the more useful message.
        var harness = new Harness(Admin(ClinicId));
        harness.Users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(User.CreateLocalUser(
                ClinicId, User.RoleSecretary, "assistante@cabinet.tn", "hash", "Amira Trabelsi"));

        var result = await harness.Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("existe déjà", result.Error);
        Assert.Contains("votre cabinet", result.Error);
    }
}

using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Clinics;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// The shared clinic + first-admin construction (multi-tenant-cloud US-3, Part C).
///
/// <para><b>Why this type exists at all, which is what the tests are really holding.</b> Two callers need to
/// create a clinic with a password-backed admin: first-run <c>setup</c> (an HTTP request) and the
/// <c>provision-clinic</c> console verb (no HTTP context, no mediator). The plan said the verb would « wrap
/// <c>CreateClinicCommand</c> », and it cannot — that command's Local branch refuses outright once any user
/// exists (AC-1.2a), so it can create an install's first clinic and never its second, which is the only thing
/// the verb is for. The body was therefore <b>moved</b> here rather than copied, so « what it means to create a
/// clinic » keeps one answer.</para>
///
/// <para>⚠️ The load-bearing test is <see cref="It_provisions_a_clinic_even_though_the_install_already_has_users"/>:
/// it pins that the bootstrap gate is deliberately <i>not</i> in here. If someone later « tidies » the
/// <c>AnyUserExistsAsync</c> check down into this helper, the verb stops working on every install that has ever
/// been used — and it would fail with setup's own « la configuration initiale a déjà été effectuée », which
/// reads as a correct refusal rather than a regression.</para>
/// </summary>
public class LocalClinicProvisioningTests
{
    private sealed class Harness
    {
        public Mock<IClinicRepository> Clinics { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IDoctorRepository> Doctors { get; } = new();
        public Mock<IProcedureTypeRepository> ProcedureTypes { get; } = new();
        public Mock<IClinicSubscriptionRepository> Subscriptions { get; } = new();
        public Mock<ISubscriptionPolicy> SubscriptionPolicy { get; } = new();
        public Mock<IMessagingAllowanceRepository> MessagingAllowances { get; } = new();
        public Mock<IMessagingAllowancePolicy> MessagingPolicy { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IClinicCatalogSeeder> CatalogSeeder { get; } = new();

        public List<Clinic> AddedClinics { get; } = new();
        public List<User> AddedUsers { get; } = new();
        public List<Doctor> AddedDoctors { get; } = new();
        public List<ProcedureType> AddedProcedureTypes { get; } = new();
        public List<ClinicSubscription> AddedSubscriptions { get; } = new();
        public List<SubscriptionPeriod> AddedEntries { get; } = new();
        public List<MessagingAllowanceEntry> AddedAllowanceEntries { get; } = new();
        public List<ClinicMessagingMonth> AddedMessagingMonths { get; } = new();

        /// <summary>Saves observed, so a test can pin that the entitlement rode the clinic's own save (FR-4).</summary>
        public int SaveCount { get; private set; }

        public Harness(bool requiresSubscription = true, int trialDays = 30, int messagesPerMonth = 200)
        {
            SubscriptionPolicy.SetupGet(p => p.RequiresSubscription).Returns(requiresSubscription);
            SubscriptionPolicy.SetupGet(p => p.TrialDays).Returns(trialDays);

            MessagingPolicy.SetupGet(p => p.DefaultMessagesPerMonth).Returns(messagesPerMonth);

            MessagingAllowances
                .Setup(r => r.AddEntryAsync(It.IsAny<MessagingAllowanceEntry>(), It.IsAny<CancellationToken>()))
                .Callback<MessagingAllowanceEntry, CancellationToken>((e, _) => AddedAllowanceEntries.Add(e))
                .Returns(Task.CompletedTask);
            MessagingAllowances
                .Setup(r => r.AddMonthAsync(It.IsAny<ClinicMessagingMonth>(), It.IsAny<CancellationToken>()))
                .Callback<ClinicMessagingMonth, CancellationToken>((m, _) => AddedMessagingMonths.Add(m))
                .Returns(Task.CompletedTask);

            Subscriptions.Setup(r => r.AddAsync(It.IsAny<ClinicSubscription>(), It.IsAny<CancellationToken>()))
                .Callback<ClinicSubscription, CancellationToken>((s, _) => AddedSubscriptions.Add(s))
                .Returns(Task.CompletedTask);
            Subscriptions.Setup(r => r.AddEntryAsync(It.IsAny<SubscriptionPeriod>(), It.IsAny<CancellationToken>()))
                .Callback<SubscriptionPeriod, CancellationToken>((e, _) => AddedEntries.Add(e))
                .Returns(Task.CompletedTask);

            Clinics.Setup(r => r.CodeExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            Clinics.Setup(r => r.AddAsync(It.IsAny<Clinic>(), It.IsAny<CancellationToken>()))
                .Callback<Clinic, CancellationToken>((c, _) => AddedClinics.Add(c))
                .ReturnsAsync((Clinic c, CancellationToken _) => c);

            Users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((User?)null);
            Users.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .Callback<User, CancellationToken>((u, _) => AddedUsers.Add(u))
                .Returns(Task.CompletedTask);

            Doctors.Setup(r => r.AddAsync(It.IsAny<Doctor>(), It.IsAny<CancellationToken>()))
                .Callback<Doctor, CancellationToken>((d, _) => AddedDoctors.Add(d))
                .Returns(Task.CompletedTask);

            ProcedureTypes.Setup(r => r.AddAsync(It.IsAny<ProcedureType>(), It.IsAny<CancellationToken>()))
                .Callback<ProcedureType, CancellationToken>((p, _) => AddedProcedureTypes.Add(p))
                .ReturnsAsync((ProcedureType p, CancellationToken _) => p);

            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Callback(() => SaveCount++)
                .ReturnsAsync(1);
        }

        public Task<Application.Common.Models.Result<ProvisionedClinic>> Run(LocalClinicRequest request) =>
            LocalClinicProvisioning.ProvisionAsync(
                request,
                Clinics.Object,
                Users.Object,
                Doctors.Object,
                ProcedureTypes.Object,
                Subscriptions.Object,
                SubscriptionPolicy.Object,
                MessagingAllowances.Object,
                MessagingPolicy.Object,
                UnitOfWork.Object,
                CatalogSeeder.Object,
                NullLogger.Instance);
    }

    private static LocalClinicRequest Request(
        bool mustChangePassword = false,
        DoctorPersonalInfoDto? doctorInfo = null,
        string? name = "Cabinet Ben Salah",
        string? email = "owner@cabinet.tn",
        string? fullName = "Dr Ahmed Ben Salah") =>
        new(
            Guid.NewGuid(),
            name,
            email,
            "hashed-password",
            fullName,
            mustChangePassword,
            DoctorInfo: doctorInfo);

    // [US-3] The happy path, and the one that says the two callers get the same clinic: code, admin, procedure
    // menu and reference catalogs, committed in one save.
    [Fact]
    public async Task It_creates_the_clinic_its_admin_and_the_default_catalogs()
    {
        var harness = new Harness();
        var request = Request();

        var result = await harness.Run(request);

        Assert.True(result.IsSuccess);

        var clinic = Assert.Single(harness.AddedClinics);
        Assert.Equal(request.ClinicId, clinic.Id);
        Assert.Equal("Cabinet Ben Salah", clinic.Name);
        Assert.False(string.IsNullOrWhiteSpace(clinic.Code));

        var admin = Assert.Single(harness.AddedUsers);
        Assert.Equal(User.RoleAdmin, admin.Role);
        Assert.Equal("owner@cabinet.tn", admin.Email);
        Assert.True(admin.IsActive);
        Assert.Equal(clinic.Id, admin.ClinicId);

        Assert.NotEmpty(harness.AddedProcedureTypes);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        harness.CatalogSeeder.Verify(s => s.SeedForClinicAsync(clinic.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// [US-3] The whole reason the helper is separate from <c>CreateClinicCommandHandler</c>'s Local branch: that
    /// branch refuses once any user exists, and provisioning clinic #2 of a hosted install happens precisely then.
    /// Nothing here may ask <c>AnyUserExistsAsync</c>.
    /// </summary>
    [Fact]
    public async Task It_provisions_a_clinic_even_though_the_install_already_has_users()
    {
        var harness = new Harness();
        harness.Users.Setup(r => r.AnyUserExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await harness.Run(Request());

        Assert.True(result.IsSuccess);
        harness.Users.Verify(r => r.AnyUserExistsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [US-3] The caller mints the id so `provision-clinic` can declare UseClinic(id) BEFORE the writes it covers
    // — a tenant scope set afterwards would cover nothing.
    [Fact]
    public async Task The_clinic_uses_the_id_the_caller_supplied()
    {
        var harness = new Harness();
        var clinicId = Guid.NewGuid();

        var result = await harness.Run(Request() with { ClinicId = clinicId });

        Assert.True(result.IsSuccess);
        Assert.Equal(clinicId, result.Value!.Clinic.Id);
        Assert.Equal(clinicId, result.Value.Admin.ClinicId);
    }

    // [US-3] A generated password must not survive its first use; a chosen one must not be challenged.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_forced_password_change_follows_who_chose_the_password(bool mustChange)
    {
        var harness = new Harness();

        var result = await harness.Run(Request(mustChangePassword: mustChange));

        Assert.True(result.IsSuccess);
        Assert.Equal(mustChange, result.Value!.Admin.MustChangePassword);
    }

    /// <summary>
    /// [US-3] The partial unique index on the lowercased email would otherwise surface as a
    /// <c>DbUpdateException</c> — a 500 on setup, a stack trace on the operator's console. First-run reaches this
    /// with no users at all, so the check costs it nothing; the verb is the caller that can genuinely collide.
    /// </summary>
    [Fact]
    public async Task An_email_that_already_has_an_account_is_refused_before_anything_is_written()
    {
        var harness = new Harness();
        harness.Users.Setup(r => r.GetByEmailAsync("owner@cabinet.tn", It.IsAny<CancellationToken>()))
            .ReturnsAsync(User.CreateLocalUser(Guid.NewGuid(), User.RoleDoctor, "owner@cabinet.tn", "h", "Someone"));

        var result = await harness.Run(Request());

        Assert.True(result.IsFailure);
        Assert.Contains("existe déjà", result.Error);
        Assert.Empty(harness.AddedClinics);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [US-3] A single-dentist cabinet: the admin is also the practitioner, so a linked Doctor carries their
    // document identity (cachet, n° d'ordre CNOMDT).
    [Fact]
    public async Task A_practitioner_is_created_and_linked_to_the_admin_account()
    {
        var harness = new Harness();

        var result = await harness.Run(Request(doctorInfo: new DoctorPersonalInfoDto
        {
            FirstName = "Ahmed",
            LastName = "Ben Salah",
            Specialty = "Dentiste"
        }));

        Assert.True(result.IsSuccess);
        var doctor = Assert.Single(harness.AddedDoctors);
        Assert.Equal(result.Value!.Admin.Id, doctor.UserId);
        Assert.Equal(result.Value.Clinic.Id, doctor.ClinicId);
    }

    // [US-3] Absent practitioner details is the admin-only account (a non-clinical office manager), not an error.
    [Fact]
    public async Task No_practitioner_details_creates_no_doctor()
    {
        var harness = new Harness();

        var result = await harness.Run(Request());

        Assert.True(result.IsSuccess);
        Assert.Empty(harness.AddedDoctors);
    }

    [Theory]
    [InlineData(null, "owner@cabinet.tn", "Dr Ahmed", "nom du cabinet")]
    [InlineData("   ", "owner@cabinet.tn", "Dr Ahmed", "nom du cabinet")]
    [InlineData("Cabinet", null, "Dr Ahmed", "email")]
    [InlineData("Cabinet", "owner@cabinet.tn", "  ", "nom complet")]
    public async Task A_missing_required_field_is_refused_in_French(
        string? name, string? email, string? fullName, string expectedFragment)
    {
        var harness = new Harness();

        var result = await harness.Run(Request(name: name, email: email, fullName: fullName));

        Assert.True(result.IsFailure);
        Assert.Contains(expectedFragment, result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.AddedClinics);
    }

    // [US-3] A nameless practitioner is never persisted — the rule the Cloud CreateClinic and JoinClinic doctor
    // paths already enforce, kept here so both callers of this helper inherit it.
    [Fact]
    public async Task A_practitioner_with_a_specialty_but_no_name_is_refused()
    {
        var harness = new Harness();

        var result = await harness.Run(Request(doctorInfo: new DoctorPersonalInfoDto
        {
            FirstName = "",
            LastName = "",
            Specialty = "Dentiste"
        }));

        Assert.True(result.IsFailure);
        Assert.Contains("praticien", result.Error);
        Assert.Empty(harness.AddedClinics);
    }

    /// <summary>
    /// [US-3] Catalog seeding is best-effort and committed separately: a failure there must not undo a clinic
    /// that already exists, because the startup backfill re-seeds it on the next boot.
    /// </summary>
    [Fact]
    public async Task A_catalog_seeding_failure_does_not_undo_the_clinic()
    {
        var harness = new Harness();
        harness.CatalogSeeder
            .Setup(s => s.SeedForClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("catalog unavailable"));

        var result = await harness.Run(Request());

        Assert.True(result.IsSuccess);
        Assert.Single(harness.AddedClinics);
    }

    // [US-3] A code collision retries rather than failing — the sequence is short and the alphabet small.
    [Fact]
    public async Task A_colliding_clinic_code_is_regenerated()
    {
        var harness = new Harness();
        var attempts = 0;
        harness.Clinics.Setup(r => r.CodeExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++attempts <= 2);

        var result = await harness.Run(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(3, attempts);
        Assert.False(string.IsNullOrWhiteSpace(result.Value!.Clinic.Code));
    }
}

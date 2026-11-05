using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Maintenance;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Clinics;
using ClinicManagement.Application.Features.Demo;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Security;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Builds the public demo's cabinet (<c>demo-cabinet</c> US-1/US-4/US-6) beside the one visitors are using: the
/// new dentist-admin is switched off right after she is created and switched on last, so
/// <see cref="DemoCabinetLocator"/> never hands a visitor a half-built cabinet. <see cref="DemoCabinetRebuild"/> owns the order.
/// </summary>
/// <remarks>
/// It runs inside the API host, never as a console verb: a verb's container has no <see cref="IRealtimeNotifier"/>
/// and no <see cref="IHttpContextAccessor"/>, so the MediatR pipeline the scenario sends through cannot resolve there.
/// </remarks>
public sealed class DemoCabinetRebuilder
{
    public const string ActorName = "demo-rebuild";

    /// <summary>Hangfire's id for the nightly rebuild, 03:00 Tunis (02:00 UTC).</summary>
    public const string RecurringJobId = "rebuild-demo-cabinet";

    public const string NightlyCron = "0 2 * * *";

    private readonly IServiceScopeFactory _scopes;
    private readonly IDemoPolicy _demo;
    private readonly DemoCabinetBuilds _builds;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DemoCabinetRebuilder> _logger;

    public DemoCabinetRebuilder(
        IServiceScopeFactory scopes,
        IDemoPolicy demo,
        DemoCabinetBuilds builds,
        IConfiguration configuration,
        ILogger<DemoCabinetRebuilder> logger)
    {
        _scopes = scopes;
        _demo = demo;
        _builds = builds;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Boot of a demo deployment: build a cabinet only when none can be entered.</summary>
    [DisableConcurrentExecution(1800)]
    [AutomaticRetry(Attempts = 0)]
    public async Task EnsureCabinet()
    {
        if (!_demo.IssuesDemoSessions)
        {
            return;
        }

        using (var scope = _scopes.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantScope>()
                .UseSystemWide("demo-rebuild: is there a cabinet a visitor can enter?");
            if (await scope.ServiceProvider.GetRequiredService<DemoCabinetLocator>().CurrentAsync(CancellationToken.None)
                is { } current)
            {
                _builds.Opened(DateOnly.FromDateTime(ClinicClock.ToClinicLocal(current.Clinic.CreatedAt)));
                return;
            }
        }

        await Rebuild();
    }

    /// <summary>Builds a fresh cabinet, checks it, makes it the one visitors enter and removes the ones nobody is in.</summary>
    [DisableConcurrentExecution(1800)]
    [AutomaticRetry(Attempts = 0)]
    public async Task Rebuild()
    {
        if (!_demo.IssuesDemoSessions)
        {
            return;
        }

        await _builds.RebuildGate.WaitAsync();
        try
        {
            using var run = new Run(this);
            await DemoCabinetRebuild.RunAsync(run, _logger, CancellationToken.None);
        }
        finally
        {
            _builds.RebuildGate.Release();
        }
    }

    /// <summary>The real steps of one rebuild, sharing the scope the new admin was provisioned in.</summary>
    private sealed class Run : IDemoRebuildSteps, IDisposable
    {
        private readonly DemoCabinetRebuilder _owner;
        public Guid ClinicId { get; } = Guid.NewGuid();
        private readonly DateTime _today = ClinicClock.ClinicToday();
        private readonly IServiceScope _scope;

        // Nobody signs in with these addresses; the stamp keeps them unique beside yesterday's cabinet.
        private readonly string _stamp = ClinicClock.ToClinicLocal(DateTime.UtcNow).ToString("yyyyMMddHHmmss");
        private User? _admin;

        public Run(DemoCabinetRebuilder owner)
        {
            _owner = owner;
            _scope = owner._scopes.CreateScope();
            _scope.ServiceProvider.GetRequiredService<IAuditActorProvider>().RunAs(ActorName);
            _scope.ServiceProvider.GetRequiredService<ITenantScope>().UseClinic(ClinicId);
        }

        public async Task<IReadOnlySet<string>> SchemaBaselineAsync(CancellationToken cancellationToken) =>
            DemoCabinetRebuild.SchemaDrift(await VerifySchemaAsync(cancellationToken));

        public async Task ProvisionAsync(CancellationToken cancellationToken) =>
            _admin = await ProvisionAdminAsync(_scope.ServiceProvider, ClinicId, _stamp, cancellationToken);

        public async Task BuildAsync(CancellationToken cancellationToken)
        {
            var admin = _admin!;
            var builder = new DemoCabinetBuilder(new DemoSeedContext(_owner._scopes, ClinicId), _today, _stamp);
            using (_owner._builds.Begin(ClinicId))
            {
                await builder.BuildAsync(new DemoMember(admin.Id, admin.Email ?? string.Empty, admin.Role), cancellationToken);
            }
        }

        public async Task<IReadOnlyList<string>> CheckAsync(IReadOnlySet<string> schemaBaseline, CancellationToken cancellationToken)
        {
            MoneyReconciliationReport money;
            using (var scope = _owner._scopes.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<ITenantScope>()
                    .UseSystemWide("demo-rebuild: the money check before a new cabinet opens");
                money = await new MoneyReconciliationService(
                    new MoneyReconciliationReader(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()))
                    .RunAsync(cancellationToken: cancellationToken);
            }

            return DemoCabinetRebuild.Failures(money, schemaBaseline, await VerifySchemaAsync(cancellationToken));
        }

        public async Task OpenAsync(CancellationToken cancellationToken)
        {
            _admin!.Activate();
            await _scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
            _owner._builds.Opened(DateOnly.FromDateTime(_today));
            _owner._logger.LogInformation("Demo: cabinet {ClinicId} built and opened to visitors.", ClinicId);
        }

        public async Task RetireOthersAsync(Guid currentClinicId, CancellationToken cancellationToken)
        {
            IReadOnlyList<Guid> retirable;
            using (var scope = _owner._scopes.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<ITenantScope>()
                    .UseSystemWide("demo-rebuild: which old cabinets is nobody inside?");
                retirable = await scope.ServiceProvider.GetRequiredService<DemoCabinetRetirement>()
                    .RetirableAsync(currentClinicId, _owner._builds.IsBuilding, DateTime.UtcNow, cancellationToken);
            }

            foreach (var clinicId in retirable)
            {
                await PurgeAsync(clinicId, cancellationToken);
                _owner._logger.LogInformation("Demo: old cabinet {ClinicId} removed.", clinicId);
            }
        }

        public async Task PurgeAsync(Guid clinicId, CancellationToken cancellationToken)
        {
            using var scope = _owner._scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<IAuditActorProvider>().RunAs(ActorName);
            scope.ServiceProvider.GetRequiredService<ITenantScope>().UseClinic(clinicId);
            await scope.ServiceProvider.GetRequiredService<DemoCabinetPurge>().PurgeAsync(clinicId, cancellationToken);
        }

        public void Dispose() => _scope.Dispose();

        private async Task<SchemaVerificationReport> VerifySchemaAsync(CancellationToken cancellationToken)
        {
            using var scope = _owner._scopes.CreateScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<ITenantScope>().UseSystemWide("demo-rebuild: the schema check around a rebuild");
            var reader = new SchemaVerificationReader(
                services.GetRequiredService<ApplicationDbContext>(),
                services.GetRequiredService<IVendorMessagingAvailability>(),
                _owner._configuration,
                services.GetRequiredService<IDataProtectionProvider>(),
                services.GetRequiredService<IAuditChainKeyProvider>());
            return await new SchemaVerificationService(reader).RunAsync(cancellationToken);
        }
    }

    private static async Task<User> ProvisionAdminAsync(
        IServiceProvider services, Guid clinicId, string stamp, CancellationToken cancellationToken)
    {
        var localAuth = services.GetRequiredService<ILocalAuthService>();

        var result = await LocalClinicProvisioning.ProvisionAsync(
            new LocalClinicRequest(
                clinicId,
                DemoScenario.CabinetName,
                DemoScenario.EmailFor("monica.geller", stamp),
                localAuth.HashPassword(localAuth.GenerateTemporaryPassword()),
                DemoScenario.DentistFullName,
                MustChangePassword: false,
                DemoScenario.Address,
                DemoScenario.Phone,
                DemoScenario.City,
                new DoctorPersonalInfoDto
                {
                    FirstName = DemoScenario.DentistFirstName,
                    LastName = DemoScenario.DentistLastName,
                    Specialty = DemoScenario.DentistSpecialty,
                },
                // Off while the scenario is written, so the locator keeps choosing yesterday's cabinet.
                AdminStartsInactive: true),
            services.GetRequiredService<IClinicRepository>(),
            services.GetRequiredService<IUserRepository>(),
            services.GetRequiredService<IDoctorRepository>(),
            services.GetRequiredService<IProcedureTypeRepository>(),
            services.GetRequiredService<IClinicSubscriptionRepository>(),
            services.GetRequiredService<ISubscriptionPolicy>(),
            services.GetRequiredService<IMessagingAllowanceRepository>(),
            services.GetRequiredService<IMessagingAllowancePolicy>(),
            services.GetRequiredService<IUnitOfWork>(),
            services.GetRequiredService<IClinicCatalogSeeder>(),
            services.GetRequiredService<ILoggerFactory>().CreateLogger(ActorName),
            cancellationToken);

        return result.IsSuccess
            ? result.Value!.Admin
            : throw new InvalidOperationException($"Demo: the cabinet could not be provisioned — {result.Error}");
    }
}

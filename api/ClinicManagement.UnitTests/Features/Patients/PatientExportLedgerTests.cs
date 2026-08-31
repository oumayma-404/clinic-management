using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Csv;
using ClinicManagement.Application.Features.Patients.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// « Exporter » on the patients page is recorded, and refuses when it cannot be.
///
/// <para><b>What this is about.</b> <c>GET /api/patients/export</c> returns twenty columns per patient —
/// <i>Date de naissance</i>, <i>Adresse</i>, <i>Identifiant CNAM</i>, <i>Antécédents médicaux</i>,
/// <i>Allergies</i> — for the whole filtered set, i.e. the cabinet's entire identified medical dataset in one
/// file. It carried <b>none</b> of the four controls the whole-clinic ZIP archive carries: no step-up, no rate
/// limit, no audit row, open to every clinic role. Nothing in the product could answer « who took the patient
/// list, and when? ».</para>
/// </summary>
public class PatientExportLedgerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<ISender> _sender = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IAuditEntryRepository> _auditEntries = new();
    private readonly Mock<IAuditActorProvider> _auditActor = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private readonly List<AuditEntry> _written = new();

    public PatientExportLedgerTests()
    {
        _clinicResolver
            .Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

        _auditActor
            .SetupGet(a => a.Current)
            .Returns(new AuditActor("user-1", "sonia@cabinet.tn"));

        _auditEntries
            .Setup(r => r.AddRangeAsync(It.IsAny<IReadOnlyCollection<AuditEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<AuditEntry>, CancellationToken>((rows, _) => _written.AddRange(rows))
            .Returns(Task.CompletedTask);

        _sender
            .Setup(s => s.Send(It.IsAny<GetPatientsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<PatientDto>>.Success(
                new PagedResult<PatientDto>(new List<PatientDto> { new(), new(), new() }, 1, 3, 3)));
    }

    private ExportPatientsQueryHandler Handler() => new(
        _sender.Object,
        _clinicResolver.Object,
        _auditEntries.Object,
        _auditActor.Object,
        _uow.Object,
        NullLogger<ExportPatientsQueryHandler>.Instance);

    [Fact]
    public async Task An_export_writes_one_audit_row_naming_the_actor_and_the_row_count()
    {
        var result = await Handler().Handle(new ExportPatientsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var row = Assert.Single(_written);
        Assert.Equal(ListExportLedger.PatientEntityType, row.EntityType);
        Assert.Equal(ClinicId, row.ClinicId);
        Assert.Equal("user-1", row.UserId);
        Assert.Contains("3 ligne(s)", row.ChangedFields);
        Assert.Contains("Liste des patients", row.ChangedFields);
    }

    // THE case this exists for. The archive's rule verbatim: the operation *is* what is being recorded, so an
    // unrecorded export that succeeds makes the guarantee false. Unlike a notification, this may not swallow.
    [Fact]
    public async Task An_export_that_cannot_be_recorded_does_not_happen()
    {
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("la base est indisponible"));

        var result = await Handler().Handle(new ExportPatientsQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ListExportLedger.UnrecordableCode, result.Code);
        Assert.Null(result.Value);
    }

    // ⚠️ A search term on this product IS a patient's name. Recording it would put PHI into the one table
    // designed never to be deleted — the same rule LogTemplateCoverageTests holds the log file to.
    [Fact]
    public async Task The_recorded_summary_never_contains_what_was_searched_for()
    {
        await Handler().Handle(
            new ExportPatientsQuery { SearchTerm = "Ben Salah" }, CancellationToken.None);

        var row = Assert.Single(_written);
        Assert.DoesNotContain("Ben Salah", row.ChangedFields);
        Assert.Contains("recherche appliquée", row.ChangedFields);
    }

    [Fact]
    public async Task An_unfiltered_export_says_so_because_that_is_the_one_that_matters()
    {
        await Handler().Handle(new ExportPatientsQuery(), CancellationToken.None);

        Assert.Contains("sans filtre (tout le cabinet)", Assert.Single(_written).ChangedFields);
    }

    [Theory]
    [InlineData(true, false, "patients signalés uniquement")]
    [InlineData(false, true, "filtré par date d'inscription")]
    public void The_filter_summary_names_each_narrowing_without_its_value(
        bool flaggedOnly, bool byDate, string expected)
    {
        var summary = ListExportLedger.DescribeFilters(
            searchTerm: null,
            createdFrom: byDate ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null,
            createdTo: null,
            flaggedOnly: flaggedOnly);

        Assert.Contains(expected, summary);
    }
}

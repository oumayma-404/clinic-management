using ClinicManagement.UnitTests.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.ProcedureTypes.Commands;
using ClinicManagement.Application.Features.ProcedureTypes.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.ProcedureTypes;

/// <summary>
/// Hardening pass — <see cref="ProcedureType"/> is now tenant-scoped (§1a). Verifies cross-clinic
/// Update/Delete read as "not found" (AC-1) and Create stamps the caller's clinic.
/// </summary>
public class ProcedureTypeTenantIsolationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static ProcedureType ProcedureType(Guid clinicId) =>
        new(Guid.NewGuid(), clinicId, "Cleaning", 30, new ColorHex("#4F83CC"));

    private readonly Mock<IProcedureTypeRepository> _procedures = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private void Authenticated() =>
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

    // [AC-1] Updating a procedure type owned by another clinic reads as "not found".
    [Fact]
    public async Task Update_Should_Return_NotFound_For_Other_Clinic()
    {
        Authenticated();
        var foreign = ProcedureType(OtherClinicId);
        _procedures.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var handler = new UpdateProcedureTypeCommandHandler(
            _procedures.Object, _appointments.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<UpdateProcedureTypeCommandHandler>.Instance);

        var result = await handler.Handle(new UpdateProcedureTypeCommand { Id = foreign.Id, Name = "Hacked" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _procedures.Verify(r => r.UpdateAsync(It.IsAny<ProcedureType>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-1] Deleting a procedure type owned by another clinic reads as "not found".
    [Fact]
    public async Task Delete_Should_Return_NotFound_For_Other_Clinic()
    {
        Authenticated();
        var foreign = ProcedureType(OtherClinicId);
        _procedures.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var handler = new DeleteProcedureTypeCommandHandler(
            _procedures.Object, _appointments.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<DeleteProcedureTypeCommandHandler>.Instance);

        var result = await handler.Handle(new DeleteProcedureTypeCommand { Id = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _procedures.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _procedures.Verify(r => r.UpdateAsync(It.IsAny<ProcedureType>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-1] Create stamps the caller's clinic on the new procedure type.
    [Fact]
    public async Task Create_Should_Assign_Caller_Clinic()
    {
        Authenticated();
        _procedures.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        ProcedureType? captured = null;
        _procedures.Setup(r => r.AddAsync(It.IsAny<ProcedureType>(), It.IsAny<CancellationToken>()))
            .Callback<ProcedureType, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync((ProcedureType p, CancellationToken _) => p);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateProcedureTypeCommandHandler(
            _procedures.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<CreateProcedureTypeCommandHandler>.Instance);

        var result = await handler.Handle(
            new CreateProcedureTypeCommand { Name = "Whitening", DefaultDurationMinutes = 45, ColorHex = "#2A9D8F" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal(ClinicId, captured!.ClinicId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-1 / Finding 2] The single-Get read explicitly scopes to the caller's clinic (not just the
    // fail-open global filter): another clinic's procedure type reads as "not found".
    [Fact]
    public async Task Get_Should_Return_NotFound_For_Other_Clinic()
    {
        Authenticated();
        var foreign = ProcedureType(OtherClinicId);
        _procedures.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var handler = new GetProcedureTypeQueryHandler(
            _procedures.Object, _clinicResolver.Object, NullLogger<GetProcedureTypeQueryHandler>.Instance);

        var result = await handler.Handle(new GetProcedureTypeQuery { Id = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    // [AC-1 / Finding 2] The list read is issued **with the caller's own clinic id** — which is what scopes it.
    //
    // ⚠️ This used to hand the mock a foreign row and assert the handler dropped it, on the premise that the
    // repository (behind a fail-open filter) might return one. Two things retired that premise: `list-pagination`
    // moved the clinic predicate into `GetFilteredAsync`'s SQL, so re-filtering the returned page in C# would be a
    // second copy of the rule — and filtering an already-cut page shrinks it unpredictably, which is why it was
    // removed; and `multi-tenant-cloud` US-2 made the global query filter refuse an unset scope instead of
    // switching off, so "the repository hands back another clinic's row" is no longer a state the product has.
    // What is still worth holding, and is held here, is that the handler cannot ask for anyone else's rows.
    [Fact]
    public async Task List_Is_Read_With_The_Callers_Own_Clinic_Id()
    {
        Authenticated();
        var own = ProcedureType(ClinicId);
        _procedures.Setup(r => r.GetFilteredAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<PageRequest?>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(new[] { own }.AsPage());

        var handler = new GetProcedureTypesQueryHandler(
            _procedures.Object, _clinicResolver.Object, NullLogger<GetProcedureTypesQueryHandler>.Instance);

        var result = await handler.Handle(new GetProcedureTypesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(own.Id, Assert.Single(result.Value!.Items).Id);
        _procedures.Verify(r => r.GetFilteredAsync(ClinicId, It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()), Times.Once);
        _procedures.Verify(r => r.GetFilteredAsync(OtherClinicId, It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

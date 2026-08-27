using ClinicManagement.UnitTests.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.ProcedureTypes.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.ProcedureTypes;

/// <summary>
/// The material-list editor's command (AC-P4.14) — the missing caller for <c>ProcedureType.SetMaterials</c>.
/// Covers the three things that make it safe: replace semantics (an empty list is the opt-out, not a no-op),
/// per-clinic item validation (a list must never point at another clinic's stock), and the two input rules.
/// </summary>
public class ProcedureTypeMaterialsTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IProcedureTypeRepository> _procedures = new();
    private readonly Mock<IStockItemRepository> _stock = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static ProcedureType Act(Guid clinicId) =>
        new(Guid.NewGuid(), clinicId, "Composite", 30, new ColorHex("#4F83CC"));

    private static StockItem Item(Guid clinicId, string name) =>
        new(Guid.NewGuid(), clinicId, name, "Consommable", "unité", minimumStockLevel: 2, maximumStockLevel: 100);

    private SetProcedureTypeMaterialsCommandHandler Handler() =>
        new(_procedures.Object, _stock.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<SetProcedureTypeMaterialsCommandHandler>.Instance);

    private void Authenticated() =>
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

    private void StockIs(params StockItem[] items) =>
        _stock.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((items).AsPage());

    // [AC-P4.14] The happy path: the list is stored against the act and echoed back on the DTO, which is what
    // lets the editor reopen showing what was saved.
    [Fact]
    public async Task Sets_The_Material_List()
    {
        Authenticated();
        var act = Act(ClinicId);
        var gloves = Item(ClinicId, "Gants");
        var anesthetic = Item(ClinicId, "Anesthésique");
        _procedures.Setup(r => r.GetByIdAsync(act.Id, It.IsAny<CancellationToken>())).ReturnsAsync(act);
        StockIs(gloves, anesthetic);

        var result = await Handler().Handle(
            new SetProcedureTypeMaterialsCommand
            {
                Id = act.Id,
                Materials =
                {
                    new() { StockItemId = gloves.Id, QuantityPerAct = 2 },
                    new() { StockItemId = anesthetic.Id, QuantityPerAct = 1 },
                },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Materials.Count);
        Assert.Equal(2, result.Value!.Materials.Single(m => m.StockItemId == gloves.Id).QuantityPerAct);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-P4.11] An empty list is a REAL value — it is how an act opts out of consuming stock. It must clear an
    // existing list rather than being read as "nothing to change", which is what a patch-semantics command
    // would have done and why this is its own command.
    [Fact]
    public async Task Empty_List_Clears_An_Existing_One()
    {
        Authenticated();
        var act = Act(ClinicId);
        var gloves = Item(ClinicId, "Gants");
        act.SetMaterials(new[] { (gloves.Id, 2) });
        Assert.Single(act.Materials); // precondition

        _procedures.Setup(r => r.GetByIdAsync(act.Id, It.IsAny<CancellationToken>())).ReturnsAsync(act);

        var result = await Handler().Handle(
            new SetProcedureTypeMaterialsCommand { Id = act.Id, Materials = new() }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Materials);
        Assert.Empty(act.Materials);
    }

    // [AC-P4.14] Material lists are per-clinic. An item from another clinic reads as "not found" — without this
    // check, saving the fiche would draw down the OTHER clinic's stock via StockConsumptionService.
    [Fact]
    public async Task Rejects_A_Stock_Item_From_Another_Clinic()
    {
        Authenticated();
        var act = Act(ClinicId);
        var foreignItem = Item(OtherClinicId, "Gants");
        _procedures.Setup(r => r.GetByIdAsync(act.Id, It.IsAny<CancellationToken>())).ReturnsAsync(act);
        StockIs(Item(ClinicId, "Autre chose")); // the clinic's own catalogue does not contain it

        var result = await Handler().Handle(
            new SetProcedureTypeMaterialsCommand
            {
                Id = act.Id,
                Materials = { new() { StockItemId = foreignItem.Id, QuantityPerAct = 1 } },
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(act.Materials);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Tenant check on the act itself, matching its sibling commands.
    [Fact]
    public async Task Returns_NotFound_For_An_Act_Of_Another_Clinic()
    {
        Authenticated();
        var foreign = Act(OtherClinicId);
        _procedures.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await Handler().Handle(
            new SetProcedureTypeMaterialsCommand { Id = foreign.Id, Materials = new() }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // A duplicate is refused with a French message rather than surfacing the aggregate's ArgumentException.
    [Fact]
    public async Task Rejects_A_Duplicated_Stock_Item()
    {
        Authenticated();
        var act = Act(ClinicId);
        var gloves = Item(ClinicId, "Gants");
        _procedures.Setup(r => r.GetByIdAsync(act.Id, It.IsAny<CancellationToken>())).ReturnsAsync(act);
        StockIs(gloves);

        var result = await Handler().Handle(
            new SetProcedureTypeMaterialsCommand
            {
                Id = act.Id,
                Materials =
                {
                    new() { StockItemId = gloves.Id, QuantityPerAct = 1 },
                    new() { StockItemId = gloves.Id, QuantityPerAct = 3 },
                },
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("qu'une fois", result.Error);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Rejects_A_Non_Positive_Quantity(int quantity)
    {
        Authenticated();
        var act = Act(ClinicId);
        var gloves = Item(ClinicId, "Gants");
        _procedures.Setup(r => r.GetByIdAsync(act.Id, It.IsAny<CancellationToken>())).ReturnsAsync(act);
        StockIs(gloves);

        var result = await Handler().Handle(
            new SetProcedureTypeMaterialsCommand
            {
                Id = act.Id,
                Materials = { new() { StockItemId = gloves.Id, QuantityPerAct = quantity } },
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

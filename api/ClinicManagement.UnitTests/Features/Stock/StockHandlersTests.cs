using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Stock.Commands;
using ClinicManagement.Application.Features.Stock.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Stock;

/// <summary>Shared test fixtures for the stock handlers.</summary>
public static class StockTestData
{
    public static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static StockItem Item(Guid clinicId, int currentStock = 10, int min = 5)
    {
        var item = new StockItem(Guid.NewGuid(), clinicId, "Gloves", "Medical Supplies", "Box", min, 100);
        item.SetCurrentStock(currentStock);
        return item;
    }
}

public class GetStockItemsQueryHandlerTests
{
    private readonly Mock<IStockItemRepository> _stock = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    private GetStockItemsQueryHandler Handler() => new(_stock.Object, _clinicResolver.Object);

    private void Authenticated() =>
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(StockTestData.ClinicId));

    // [AC-1][AC-7] Returns the current clinic's items mapped to DTOs.
    [Fact]
    public async Task Handle_Should_Return_Clinic_Scoped_Items()
    {
        Authenticated();
        _stock.Setup(r => r.GetByClinicIdAsync(StockTestData.ClinicId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { StockTestData.Item(StockTestData.ClinicId, currentStock: 3, min: 5) });

        var result = await Handler().Handle(new GetStockItemsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = Assert.Single(result.Value!);
        Assert.True(dto.IsLowStock); // current 3 <= min 5
    }

    // [AC-6] Passes the low-stock filter through to the repository.
    [Fact]
    public async Task Handle_Should_Pass_LowStockOnly_To_Repository()
    {
        Authenticated();
        _stock.Setup(r => r.GetByClinicIdAsync(StockTestData.ClinicId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StockItem>());

        var result = await Handler().Handle(new GetStockItemsQuery { LowStockOnly = true }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _stock.Verify(r => r.GetByClinicIdAsync(StockTestData.ClinicId, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-7] Fails when the clinic cannot be resolved (unauthenticated).
    [Fact]
    public async Task Handle_Should_Fail_When_Clinic_Not_Resolved()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("User ID not found in token"));

        var result = await Handler().Handle(new GetStockItemsQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _stock.Verify(r => r.GetByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class CreateStockItemCommandHandlerTests
{
    private readonly Mock<IStockItemRepository> _stock = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private CreateStockItemCommandHandler Handler() => new(_stock.Object, _clinicResolver.Object, _uow.Object);

    private StockItem? _captured;

    private void Authenticated()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(StockTestData.ClinicId));
        _stock.Setup(r => r.AddAsync(It.IsAny<StockItem>(), It.IsAny<CancellationToken>()))
            .Callback<StockItem, CancellationToken>((s, _) => _captured = s)
            .ReturnsAsync((StockItem s, CancellationToken _) => s);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static CreateStockItemCommand ValidCommand() => new()
    {
        Name = "Syringes",
        Category = "Medical Supplies",
        Unit = "Box",
        CurrentStock = 20,
        MinimumStockLevel = 10
    };

    // [AC-2][AC-7] Creates a clinic-scoped item and persists it.
    [Fact]
    public async Task Handle_Should_Create_Item_Scoped_To_Clinic()
    {
        Authenticated();

        var result = await Handler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Value!.CurrentStock);
        Assert.NotNull(_captured);
        Assert.Equal(StockTestData.ClinicId, _captured!.ClinicId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Edge case: MaximumStockLevel defaults to MinimumStockLevel when omitted.
    [Fact]
    public async Task Handle_Should_Default_Maximum_To_Minimum_When_Omitted()
    {
        Authenticated();
        var command = ValidCommand();
        command.MaximumStockLevel = null;

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(command.MinimumStockLevel, result.Value!.MaximumStockLevel);
    }

    // [AC-5] Validation: blank name is rejected and nothing is persisted.
    [Fact]
    public async Task Handle_Should_Fail_When_Name_Blank()
    {
        Authenticated();
        var command = ValidCommand();
        command.Name = "   ";

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-5] Validation: negative quantity is rejected.
    [Fact]
    public async Task Handle_Should_Fail_When_Quantity_Negative()
    {
        Authenticated();
        var command = ValidCommand();
        command.CurrentStock = -1;

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-5] Validation: negative unit price is rejected (parity with the UI).
    [Fact]
    public async Task Handle_Should_Fail_When_UnitPrice_Negative()
    {
        Authenticated();
        var command = ValidCommand();
        command.UnitPrice = -5m;

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class UpdateStockItemCommandHandlerTests
{
    private readonly Mock<IStockItemRepository> _stock = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private readonly Mock<INotificationGenerator> _notificationGenerator = new();

    private UpdateStockItemCommandHandler Handler() => new(_stock.Object, _clinicResolver.Object, _uow.Object, _notificationGenerator.Object);

    private void Authenticated()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(StockTestData.ClinicId));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static UpdateStockItemCommand Command(Guid id) => new()
    {
        Id = id,
        Name = "Updated Name",
        Category = "PPE",
        Unit = "Pack",
        CurrentStock = 7,
        MinimumStockLevel = 3
    };

    // [AC-3] Updates an item belonging to the user's clinic.
    [Fact]
    public async Task Handle_Should_Update_Own_Clinic_Item()
    {
        Authenticated();
        var item = StockTestData.Item(StockTestData.ClinicId);
        _stock.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        var result = await Handler().Handle(Command(item.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Name", result.Value!.Name);
        Assert.Equal(7, result.Value!.CurrentStock);
        _stock.Verify(r => r.UpdateAsync(item, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-7] Cross-clinic isolation: cannot update another clinic's item.
    [Fact]
    public async Task Handle_Should_Fail_For_Other_Clinic_Item()
    {
        Authenticated();
        var foreignItem = StockTestData.Item(StockTestData.OtherClinicId);
        _stock.Setup(r => r.GetByIdAsync(foreignItem.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreignItem);

        var result = await Handler().Handle(Command(foreignItem.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        _stock.Verify(r => r.UpdateAsync(It.IsAny<StockItem>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Missing item → failure.
    [Fact]
    public async Task Handle_Should_Fail_When_Item_Not_Found()
    {
        Authenticated();
        var id = Guid.NewGuid();
        _stock.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((StockItem?)null);

        var result = await Handler().Handle(Command(id), CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class DeleteStockItemCommandHandlerTests
{
    private readonly Mock<IStockItemRepository> _stock = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private DeleteStockItemCommandHandler Handler() => new(_stock.Object, _clinicResolver.Object, _uow.Object);

    private void Authenticated()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(StockTestData.ClinicId));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    // [AC-4] Deletes an item belonging to the user's clinic.
    [Fact]
    public async Task Handle_Should_Delete_Own_Clinic_Item()
    {
        Authenticated();
        var item = StockTestData.Item(StockTestData.ClinicId);
        _stock.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        var result = await Handler().Handle(new DeleteStockItemCommand { Id = item.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _stock.Verify(r => r.DeleteAsync(item.Id, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-7] Cross-clinic isolation: cannot delete another clinic's item.
    [Fact]
    public async Task Handle_Should_Fail_For_Other_Clinic_Item()
    {
        Authenticated();
        var foreignItem = StockTestData.Item(StockTestData.OtherClinicId);
        _stock.Setup(r => r.GetByIdAsync(foreignItem.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreignItem);

        var result = await Handler().Handle(new DeleteStockItemCommand { Id = foreignItem.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _stock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

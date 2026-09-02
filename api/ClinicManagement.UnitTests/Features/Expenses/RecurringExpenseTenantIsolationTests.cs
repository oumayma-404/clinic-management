using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Expenses.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Expenses;

/// <summary>
/// Tenant isolation for the monthly-dépense series (`caisse-monthly-expenses`), on the terms every clinic-scoped
/// feature in this suite is held to.
///
/// <para><b>Why a mocked repository is the right harness for this.</b> It applies no filter at all — which is
/// exactly what « the EF global query filter is inactive » looks like from a handler's point of view. So these
/// cases fail unless each handler performs its own DB-resolved <c>ClinicId</c> check, and pass whether or not the
/// filter happens to be in scope.</para>
///
/// <para>Each asserts the same three things: the operation <b>fails</b>, it reads as « introuvable » rather than
/// « interdit » (no existence disclosure, the convention everywhere else here), and <b>nothing is saved</b>.</para>
/// </summary>
public class RecurringExpenseTenantIsolationTests
{
    private static readonly Guid CallerClinic = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinic = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IRecurringExpenseRepository> _series = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    public RecurringExpenseTenantIsolationTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(CallerClinic));
    }

    private RecurringExpense ForeignSeries()
    {
        var series = new RecurringExpense(
            Guid.NewGuid(), OtherClinic, "Loyer", 800m, PaymentMethod.Cash, 5, "2026-09", "Local");
        _series.Setup(r => r.GetByIdAsync(series.Id, It.IsAny<CancellationToken>())).ReturnsAsync(series);
        return series;
    }

    private void AssertRefusedAndNothingSaved(Result result)
    {
        Assert.True(result.IsFailure);
        Assert.Contains("introuvable", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Refuses_Another_Clinics_Series()
    {
        var series = ForeignSeries();
        var handler = new UpdateRecurringExpenseCommandHandler(
            _series.Object, _clinicResolver.Object, _uow.Object);

        var result = await handler.Handle(
            new UpdateRecurringExpenseCommand
            {
                Id = series.Id, Category = "Piraté", Amount = 5m,
                Method = nameof(PaymentMethod.Cash), DayOfMonth = 1,
            },
            CancellationToken.None);

        AssertRefusedAndNothingSaved(result);
        Assert.Equal("Loyer", series.Category);
        Assert.Equal(800m, series.Amount);
    }

    [Fact]
    public async Task Stop_Refuses_Another_Clinics_Series()
    {
        var series = ForeignSeries();
        var handler = new StopRecurringExpenseCommandHandler(
            _series.Object, _clinicResolver.Object, _uow.Object);

        var result = await handler.Handle(
            new StopRecurringExpenseCommand { Id = series.Id }, CancellationToken.None);

        AssertRefusedAndNothingSaved(result);
        Assert.True(series.IsActive);
    }

    // A series that does not exist and one belonging to somebody else must be indistinguishable to the caller.
    [Fact]
    public async Task A_Missing_Series_Reads_The_Same_As_A_Foreign_One()
    {
        var missingId = Guid.NewGuid();
        _series.Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecurringExpense?)null);
        var handler = new StopRecurringExpenseCommandHandler(
            _series.Object, _clinicResolver.Object, _uow.Object);

        var missing = await handler.Handle(
            new StopRecurringExpenseCommand { Id = missingId }, CancellationToken.None);

        var foreign = await handler.Handle(
            new StopRecurringExpenseCommand { Id = ForeignSeries().Id }, CancellationToken.None);

        Assert.Equal(foreign.Error, missing.Error);
        Assert.Equal(foreign.Code, missing.Code);
    }

    // The list read is scoped by the caller's own clinic id, never by a parameter the caller could supply.
    [Fact]
    public async Task The_List_Read_Asks_Only_For_The_Callers_Clinic()
    {
        _series.Setup(r => r.GetActiveByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RecurringExpense>());
        var handler = new Application.Features.Expenses.Queries.GetRecurringExpensesQueryHandler(
            _series.Object, _clinicResolver.Object);

        await handler.Handle(new Application.Features.Expenses.Queries.GetRecurringExpensesQuery(), CancellationToken.None);

        _series.Verify(r => r.GetActiveByClinicIdAsync(CallerClinic, It.IsAny<CancellationToken>()), Times.Once);
        _series.Verify(r => r.GetActiveByClinicIdAsync(OtherClinic, It.IsAny<CancellationToken>()), Times.Never);
    }
}

using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.ProcedureTypes.Commands;
using ClinicManagement.Application.Features.ProcedureTypes.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.ProcedureTypes;

/// <summary>
/// The handler side of the act catalogue's category: the suggestion list served to the form and the filter, and the
/// update command's tri-state.
/// </summary>
public class ProcedureTypeCategoryHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IProcedureTypeRepository> _procedures = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public ProcedureTypeCategoryHandlerTests() =>
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

    private static ProcedureType Act(string? category = null) =>
        new(
            id: Guid.NewGuid(),
            clinicId: ClinicId,
            name: "Détartrage",
            defaultDurationMinutes: 30,
            color: new ColorHex("#4F83CC"),
            category: category);

    private async Task<List<string>> Categories(params string[] inUse)
    {
        _procedures
            .Setup(r => r.GetCategoriesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inUse);

        var handler = new GetProcedureTypeCategoriesQueryHandler(
            _procedures.Object, _clinicResolver.Object,
            NullLogger<GetProcedureTypeCategoriesQueryHandler>.Instance);

        var result = await handler.Handle(new GetProcedureTypeCategoriesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    // The suggestions are the twelve disciplines even for a clinic that has filed nothing yet — otherwise the very
    // first act typed into a fresh clinic gets a blank list and a hand-typed category, which is where drift starts.
    [Fact]
    public async Task Categories_Offer_The_Suggested_Disciplines_When_The_Clinic_Has_Used_None()
    {
        var categories = await Categories();

        Assert.Equal(ProcedureTypeCategories.Canonical, categories);
    }

    // Clinical order, not alphabetical: « Consultation » must lead, and the alphabet would put
    // « Chirurgie/Extraction » there instead.
    [Fact]
    public async Task Categories_Keep_The_Suggested_Disciplines_In_Clinical_Order()
    {
        var categories = await Categories();

        Assert.Equal("Consultation", categories[0]);
        Assert.Equal("Radiologie", categories[1]);
        Assert.Equal("Pédodontie", categories[^1]);
    }

    /// <summary>
    /// The point of the query: a category the clinic invented comes back, so the next admin picks it instead of
    /// retyping it. Without this the open field drifts on its second use.
    /// </summary>
    [Fact]
    public async Task Categories_Append_The_Clinics_Own_Alphabetically_After_The_Suggested_Ones()
    {
        var categories = await Categories("Occlusodontie", "Implantologie", "Chirurgie maxillo-faciale");

        Assert.Equal(ProcedureTypeCategories.Canonical.Count + 2, categories.Count);
        Assert.Equal("Chirurgie maxillo-faciale", categories[^2]);
        Assert.Equal("Occlusodontie", categories[^1]);
    }

    // A canonical category the clinic is already using must not be listed twice — the union is on the discipline,
    // not on the string, so a stored « Implantologie » is the same entry as the suggestion.
    [Fact]
    public async Task Categories_Do_Not_Repeat_A_Suggested_Discipline_The_Clinic_Already_Uses()
    {
        var categories = await Categories("Implantologie", "implantologie");

        Assert.Equal(ProcedureTypeCategories.Canonical, categories);
    }

    private UpdateProcedureTypeCommandHandler UpdateHandler() =>
        new(_procedures.Object, _appointments.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<UpdateProcedureTypeCommandHandler>.Instance);

    private void Existing(ProcedureType act)
    {
        _procedures.Setup(r => r.GetByIdAsync(act.Id, It.IsAny<CancellationToken>())).ReturnsAsync(act);
        _appointments
            .Setup(r => r.GetByProcedureTypeIdAsync(act.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());
    }

    // Tri-state, part 1 — omitted means unchanged. This is what lets the *other* fields be edited without the form
    // having to resend a category it was not asked about.
    [Fact]
    public async Task Update_Without_Category_Leaves_The_Discipline_Alone()
    {
        var act = Act("Endodontie");
        Existing(act);

        var result = await UpdateHandler().Handle(
            new UpdateProcedureTypeCommand { Id = act.Id, Name = "Dévitalisation" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Endodontie", act.Category);
    }

    // Tri-state, part 2 — `""` clears. Conflating the two with `null` is what makes an "unfile this act" gesture
    // report success and change nothing, the same defect the appointment procedures tri-state fixed.
    [Fact]
    public async Task Update_With_An_Empty_Category_Unfiles_The_Act()
    {
        var act = Act("Endodontie");
        Existing(act);

        var result = await UpdateHandler().Handle(
            new UpdateProcedureTypeCommand { Id = act.Id, Category = "" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(act.Category);
    }

    // A typed variant reaching the handler is stored canonically, so the act groups with the ones picked from the
    // list rather than forming a second « endodontie » group of one.
    [Fact]
    public async Task Update_Canonicalises_A_Typed_Category()
    {
        var act = Act();
        Existing(act);

        var result = await UpdateHandler().Handle(
            new UpdateProcedureTypeCommand { Id = act.Id, Category = "  PARODONTOLOGIE " }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Parodontologie", act.Category);
    }
}

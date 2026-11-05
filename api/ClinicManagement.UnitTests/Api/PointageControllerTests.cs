using System.Text.Json;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Application.Features.Team.Commands;
using ClinicManagement.Application.Features.Team.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The borne's two anonymous routes: the secret comes from the cookie and never from the body, each refusal reaches the
/// borne with the status its code names, and only a punch that landed re-issues the cookie.
/// </summary>
public class PointageControllerTests
{
    private const string Secret = "borne-secret";
    private readonly Mock<IMediator> _mediator = new();
    private readonly DefaultHttpContext _http = new();

    private PointageController Controller(bool withCookie = true)
    {
        if (withCookie) _http.Request.Headers.Cookie = $"{BorneCookie.Name}={Secret}";
        return new PointageController(_mediator.Object) { ControllerContext = new ControllerContext { HttpContext = _http } };
    }

    private void PunchAnswers(Result<PunchOutcome> outcome) =>
        _mediator.Setup(m => m.Send(It.IsAny<PunchCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(outcome);

    private static PointageController.PunchRequest Tap => new("user-1", new List<int> { 0, 1, 2, 5 });

    private string SetCookie => _http.Response.Headers.SetCookie.ToString();

    private static JsonElement Body(ObjectResult result) => JsonSerializer.SerializeToElement(result.Value);

    [Fact]
    public async Task A_Landed_Punch_Answers_200_And_Re_Issues_The_Cookie_For_Another_400_Days()
    {
        PunchAnswers(Result<PunchOutcome>.Success(new PunchOutcome(
            new PunchResultDto { Action = AttendanceRules.ArrivalAction, At = "08:02", Name = "Ines Gharbi", TerminalName = "Accueil" }, null)));

        var result = (await Controller().Punch(Tap, default)).Result;

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("08:02", Assert.IsType<PunchResultDto>(ok.Value).At);
        var cookie = SetCookie.ToLowerInvariant();
        Assert.Contains($"{BorneCookie.Name}={Secret}".ToLowerInvariant(), cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/api/pointage", cookie);
        Assert.Contains($"max-age={(int)TimeSpan.FromDays(400).TotalSeconds}", cookie);
    }

    [Fact]
    public async Task The_Secret_Comes_From_The_Cookie_And_The_Person_From_The_Body()
    {
        PunchAnswers(Result<PunchOutcome>.Failure(AttendanceRules.TerminalUnknown, AttendanceRules.TerminalUnknownCode));

        await Controller().Punch(new PointageController.PunchRequest(null, null, "arrival"), default);

        _mediator.Verify(m => m.Send(It.Is<PunchCommand>(c => c.Secret == Secret && c.UserId == string.Empty && c.Pattern == null
            && c.Expected == "arrival"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Wrong_Schema_Answers_400_With_The_Tries_Left_And_No_Cookie()
    {
        PunchAnswers(Result<PunchOutcome>.Success(new PunchOutcome(null,
            new PunchRefusal(AttendanceRules.SchemaWrongCode, AttendanceRules.SchemaWrong(4), 4, null))));

        var result = Assert.IsType<ObjectResult>((await Controller().Punch(Tap, default)).Result);

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        var body = Body(result);
        Assert.Equal(AttendanceRules.SchemaWrongCode, body.GetProperty("code").GetString());
        Assert.Equal(4, body.GetProperty("attemptsLeft").GetInt32());
        Assert.Equal(AttendanceRules.SchemaWrong(4), body.GetProperty("error").GetString());
        Assert.Empty(SetCookie);
    }

    [Fact]
    public async Task A_Locked_Tile_Answers_423_With_The_Lock_End()
    {
        var until = new DateTime(2026, 11, 5, 9, 15, 0, DateTimeKind.Utc);
        PunchAnswers(Result<PunchOutcome>.Success(new PunchOutcome(null,
            new PunchRefusal(AttendanceRules.SchemaLockedCode, "Trop d'essais.", null, until))));

        var result = Assert.IsType<ObjectResult>((await Controller().Punch(Tap, default)).Result);

        Assert.Equal(StatusCodes.Status423Locked, result.StatusCode);
        Assert.Equal(until, Body(result).GetProperty("lockedUntil").GetDateTime());
        Assert.Empty(SetCookie);
    }

    [Theory]
    [InlineData(AttendanceRules.TerminalUnknownCode, StatusCodes.Status401Unauthorized)]
    [InlineData(AttendanceRules.ModuleOffCode, StatusCodes.Status403Forbidden)]
    [InlineData(AttendanceRules.PersonNotTrackedCode, StatusCodes.Status403Forbidden)]
    [InlineData(AttendanceRules.SchemaMissingCode, StatusCodes.Status409Conflict)]
    [InlineData(AttendanceRules.OnLeaveCode, StatusCodes.Status409Conflict)]
    [InlineData(AttendanceRules.PunchStateChangedCode, StatusCodes.Status409Conflict)]
    [InlineData(AttendanceRules.SchemaInvalidCode, StatusCodes.Status400BadRequest)]
    [InlineData(SubscriptionRefusals.RequiredCode, StatusCodes.Status402PaymentRequired)]
    [InlineData(SubscriptionRefusals.SuspendedCode, StatusCodes.Status402PaymentRequired)]
    [InlineData(SubscriptionRefusals.MissingCode, StatusCodes.Status402PaymentRequired)]
    public async Task Each_Refusal_Reaches_The_Borne_With_The_Status_Its_Code_Names(string code, int status)
    {
        PunchAnswers(Result<PunchOutcome>.Failure("Refusé.", code));

        var result = Assert.IsType<ObjectResult>((await Controller().Punch(Tap, default)).Result);

        Assert.Equal(status, result.StatusCode);
        Assert.Equal(code, Body(result).GetProperty("code").GetString());
        Assert.Empty(SetCookie);
    }

    [Fact]
    public async Task A_Failure_With_No_Code_Is_A_Plain_400()
    {
        PunchAnswers(Result<PunchOutcome>.Failure("Erreur lors du pointage."));

        var result = Assert.IsType<ObjectResult>((await Controller().Punch(Tap, default)).Result);

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task The_Status_Read_Is_Asked_With_The_Devices_Cookie()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetPointageStatusQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PointageStatusDto>.Success(new PointageStatusDto { IsBorne = false }));

        var result = await Controller(withCookie: false).Status(default);

        Assert.IsType<OkObjectResult>(result.Result);
        _mediator.Verify(m => m.Send(It.Is<GetPointageStatusQuery>(q => q.Secret == null), It.IsAny<CancellationToken>()), Times.Once);
    }
}

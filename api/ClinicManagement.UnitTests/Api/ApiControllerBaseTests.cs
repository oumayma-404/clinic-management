using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// Pins the single canonical API error contract (graceful-error-handling AC-1 / AC-8): every failure
/// renders as <c>{ "error": "&lt;message&gt;" }</c> while the action keeps its chosen status code, and a
/// null/blank message falls back to a generic message rather than leaking an empty body.
/// </summary>
public class ApiControllerBaseTests
{
    // Minimal concrete subclass so the protected helpers can be exercised directly (the base is abstract).
    private sealed class TestController : ApiControllerBase
    {
        public ActionResult InvokeHandleFailure(Result result, int statusCode) => HandleFailure(result, statusCode);
        public ActionResult InvokeHandleFailureDefault(Result result) => HandleFailure(result);
        public ActionResult InvokeFailure(string? message, int statusCode) => Failure(message, statusCode);
        public ActionResult InvokeFailureDefault(string? message) => Failure(message);
    }

    /// <summary>Asserts the body is exactly the canonical <c>{ error }</c> shape and returns the message.</summary>
    private static string? ErrorOf(ActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.NotNull(objectResult.Value);
        var property = objectResult.Value!.GetType().GetProperty("error");
        Assert.NotNull(property); // canonical shape: the key is exactly "error" — no title/details/envelope
        return (string?)property!.GetValue(objectResult.Value);
    }

    [Fact]
    public void HandleFailure_Renders_Canonical_Error_Shape_With_Chosen_Status() // [AC-1]
    {
        var result = new TestController().InvokeHandleFailure(
            Result.Failure("Patient not found."), StatusCodes.Status404NotFound);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal("Patient not found.", ErrorOf(result));
    }

    [Theory] // [AC-1] status-code assignment is preserved per action; only the body shape is unified.
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status404NotFound)]
    public void Failure_Preserves_The_Actions_Status_Code(int statusCode) // [AC-1]
    {
        var result = new TestController().InvokeFailure("boom", statusCode);

        Assert.Equal(statusCode, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal("boom", ErrorOf(result));
    }

    [Fact]
    public void Helpers_Default_To_BadRequest() // [AC-1]
    {
        Assert.Equal(StatusCodes.Status400BadRequest,
            Assert.IsType<ObjectResult>(new TestController().InvokeFailureDefault("boom")).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest,
            Assert.IsType<ObjectResult>(new TestController().InvokeHandleFailureDefault(Result.Failure("boom"))).StatusCode);
    }

    [Theory] // [AC-8] a null/blank message never leaks an empty body — it becomes the generic message.
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Failure_Falls_Back_To_Generic_Message_For_Blank_Input(string? message) // [AC-8]
    {
        var error = ErrorOf(new TestController().InvokeFailure(message, StatusCodes.Status400BadRequest));

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal("An error occurred while processing your request.", error);
    }
}

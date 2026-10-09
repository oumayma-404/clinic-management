using System.Text;
using ClinicManagement.API.Middleware;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// <c>clinic-pc-copy</c> D17, FR-6: a cabinet's save carrying an <c>Idempotency-Key</c> is answered again — never
/// recorded twice — when it is pressed again after its answer was lost. The store's SQL is the rig's to check; what
/// is decided around it is here: what is kept, what frees the key, and what the middleware never touches.
/// </summary>
public class IdempotencyMiddlewareTests
{
    private static readonly Guid ClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private const string Key = "3f1c2d4e-5a6b-4c7d-8e9f-0a1b2c3d4e5f";

    private sealed class FakeStore : IIdempotencyStore
    {
        public IdempotencyClaim Next { get; set; } = new(IdempotencyClaimOutcome.Claimed);
        public List<string> Calls { get; } = new();
        public (int Status, string? ContentType, string Body)? Completed { get; private set; }
        public string? ClaimedFingerprint { get; private set; }

        public Task<IdempotencyClaim> ClaimAsync(Guid clinicId, string key, string fingerprint, DateTime nowUtc, CancellationToken ct)
        {
            Calls.Add("claim");
            ClaimedFingerprint = fingerprint;
            return Task.FromResult(Next);
        }

        public Task CompleteAsync(Guid clinicId, string key, int statusCode, string? contentType, string body, DateTime nowUtc, CancellationToken ct)
        {
            Calls.Add("complete");
            Completed = (statusCode, contentType, body);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid clinicId, string key, CancellationToken ct)
        {
            Calls.Add("release");
            return Task.CompletedTask;
        }
    }

    private static (DefaultHttpContext Context, MemoryStream Body) Request(
        string method = "POST", string path = "/api/patients", string? key = Key)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (key is not null)
        {
            context.Request.Headers[IdempotencyMiddleware.HeaderName] = key;
        }

        var body = new MemoryStream();
        context.Response.Body = body;
        return (context, body);
    }

    private static TenantScope Scoped(bool clinic = true)
    {
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);
        if (clinic)
        {
            scope.UseClinic(ClinicId);
        }

        return scope;
    }

    private static async Task<int> RunAsync(
        FakeStore store, DefaultHttpContext context, RequestDelegate next, bool clinic = true)
    {
        var runs = 0;
        var middleware = new IdempotencyMiddleware(async ctx =>
        {
            runs++;
            await next(ctx);
        }, NullLogger<IdempotencyMiddleware>.Instance);
        await middleware.InvokeAsync(context, Scoped(clinic), store);
        return runs;
    }

    private static RequestDelegate Answer(int status, string? json)
    {
        return async ctx =>
        {
            ctx.Response.StatusCode = status;
            if (json is not null)
            {
                ctx.Response.ContentType = "application/json; charset=utf-8";
                await ctx.Response.WriteAsync(json);
            }
        };
    }

    private static string Text(MemoryStream body) => Encoding.UTF8.GetString(body.ToArray());

    [Fact]
    public async Task A_First_Save_Runs_Keeps_Its_Answer_And_Stamps_Its_Key_For_The_Change_Log()
    {
        var store = new FakeStore();
        var (context, body) = Request();
        string? stamped = null;

        var runs = await RunAsync(store, context, async ctx =>
        {
            stamped = ctx.Items[HttpIdempotencyKeyAccessor.ItemsKey] as string;
            await Answer(201, """{"id":"p1"}""")(ctx);
        });

        Assert.Equal(1, runs);
        Assert.Equal(Key, stamped);
        Assert.Equal(new[] { "claim", "complete" }, store.Calls);
        Assert.Equal((201, "application/json; charset=utf-8", """{"id":"p1"}"""), store.Completed);
        // What the client got is exactly what the save answered: the capture writes through.
        Assert.Equal("""{"id":"p1"}""", Text(body));
        Assert.Equal("POST /api/patients", store.ClaimedFingerprint);
    }

    // FR-6 / EC-6: the save's answer was lost, the same save is pressed again — answered, not recorded twice.
    [Fact]
    public async Task A_Second_Press_Is_Answered_With_The_First_Answer_And_Records_Nothing()
    {
        var store = new FakeStore
        {
            Next = new(IdempotencyClaimOutcome.Replay, 201, "application/json; charset=utf-8", """{"id":"p1"}"""),
        };
        var (context, body) = Request();

        var runs = await RunAsync(store, context, Answer(201, """{"id":"p2"}"""));

        Assert.Equal(0, runs);
        Assert.Equal(201, context.Response.StatusCode);
        Assert.Equal("true", context.Response.Headers[IdempotencyMiddleware.ReplayedHeader].ToString());
        Assert.Equal("""{"id":"p1"}""", Text(body));
        Assert.Equal(new[] { "claim" }, store.Calls);
    }

    [Fact]
    public async Task A_Press_While_The_First_Is_Still_Running_Is_Refused_In_French_And_Runs_Nothing()
    {
        var store = new FakeStore { Next = new(IdempotencyClaimOutcome.InProgress) };
        var (context, body) = Request();

        var runs = await RunAsync(store, context, Answer(201, "{}"));

        Assert.Equal(0, runs);
        Assert.Equal(409, context.Response.StatusCode);
        Assert.Contains(IdempotencyMiddleware.InProgressCode, Text(body));
        Assert.Contains("déjà en cours", Text(body));
    }

    [Fact]
    public async Task A_Key_Reused_On_Another_Request_Is_Refused_And_Runs_Nothing()
    {
        var store = new FakeStore { Next = new(IdempotencyClaimOutcome.Mismatch) };
        var (context, body) = Request();

        var runs = await RunAsync(store, context, Answer(201, "{}"));

        Assert.Equal(0, runs);
        Assert.Equal(422, context.Response.StatusCode);
        Assert.Contains(IdempotencyMiddleware.KeyReusedCode, Text(body));
    }

    // A refusal is not kept: corrected and pressed again, the save must run.
    [Theory]
    [InlineData(400)]
    [InlineData(409)]
    [InlineData(423)]
    [InlineData(500)]
    public async Task A_Refused_Save_Frees_Its_Key(int status)
    {
        var store = new FakeStore();
        var (context, _) = Request();

        await RunAsync(store, context, Answer(status, """{"error":"non"}"""));

        Assert.Equal(new[] { "claim", "release" }, store.Calls);
    }

    [Fact]
    public async Task A_Save_That_Throws_Frees_Its_Key_And_The_Error_Travels_On()
    {
        var store = new FakeStore();
        var (context, body) = Request();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunAsync(store, context, _ => throw new InvalidOperationException("boom")));

        Assert.Equal(new[] { "claim", "release" }, store.Calls);
        // The error middleware upstream writes to the real body again, not to the capture.
        Assert.Same(body, context.Response.Body);
    }

    // A file is rebuilt, never replayed: a PDF answered to a POST frees its key, and the client still gets every byte.
    [Fact]
    public async Task A_File_Answer_Is_Not_Kept()
    {
        var store = new FakeStore();
        var (context, body) = Request(path: "/api/medical-documents/generate-pdf-download");

        await RunAsync(store, context, async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/pdf";
            await ctx.Response.Body.WriteAsync(Encoding.ASCII.GetBytes("%PDF-1.7 ..."));
        });

        Assert.Equal(new[] { "claim", "release" }, store.Calls);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(body.ToArray()));
    }

    [Fact]
    public async Task An_Answer_Too_Large_To_Keep_Frees_Its_Key_And_Still_Reaches_The_Client_Whole()
    {
        var store = new FakeStore();
        var (context, body) = Request();
        var big = "\"" + new string('x', IdempotencyMiddleware.MaxStoredBodyBytes + 10) + "\"";

        await RunAsync(store, context, Answer(200, big));

        Assert.Equal(new[] { "claim", "release" }, store.Calls);
        Assert.Equal(big.Length, body.Length);
    }

    [Fact]
    public async Task An_Empty_Answer_Is_Kept_So_A_Repeated_Delete_Is_Answered_Too()
    {
        var store = new FakeStore();
        var (context, _) = Request(method: "DELETE", path: "/api/expenses/1");

        await RunAsync(store, context, Answer(204, null));

        Assert.Equal(new[] { "claim", "complete" }, store.Calls);
        Assert.Equal(204, store.Completed!.Value.Status);
    }

    // Only a cabinet's own writes: reads, other paths, no clinic in scope (sign-in, the console, a PC's channel), and a
    // missing or malformed key pass through untouched — the store is never asked.
    [Theory]
    [InlineData("GET", "/api/patients", Key, true)]
    [InlineData("HEAD", "/api/patients", Key, true)]
    [InlineData("OPTIONS", "/api/patients", Key, true)]
    [InlineData("POST", "/bff/auth/token", Key, true)]
    [InlineData("POST", "/api/patients", null, true)]
    [InlineData("POST", "/api/patients", "short", true)]
    [InlineData("POST", "/api/patients", "has spaces in it ok", true)]
    [InlineData("POST", "/api/auth/login", Key, false)]
    public async Task What_Is_Not_A_Cabinet_Write_With_A_Key_Is_Left_Alone(string method, string path, string? key, bool clinic)
    {
        var store = new FakeStore();
        var (context, _) = Request(method, path, key);

        var runs = await RunAsync(store, context, Answer(201, "{}"), clinic);

        Assert.Equal(1, runs);
        Assert.Empty(store.Calls);
        Assert.Null(context.Items[HttpIdempotencyKeyAccessor.ItemsKey]);
    }

    [Fact]
    public void The_Fingerprint_Is_The_Method_And_Path_Hashed_Past_The_Column()
    {
        var (context, _) = Request(path: "/api/invoices/1/payments");
        context.Request.QueryString = new QueryString("?force=true");
        Assert.Equal("POST /api/invoices/1/payments?force=true", IdempotencyMiddleware.Fingerprint(context.Request));

        context.Request.QueryString = new QueryString("?q=" + new string('a', 500));
        var hashed = IdempotencyMiddleware.Fingerprint(context.Request);
        Assert.StartsWith("sha256:", hashed);
        Assert.True(hashed.Length <= ClinicManagement.Domain.Entities.IdempotencyRecord.MaxFingerprintLength);
    }

    [Fact]
    public void The_Key_Accessor_Reads_What_The_Middleware_Stamped()
    {
        var context = new DefaultHttpContext();
        var accessor = new HttpIdempotencyKeyAccessor(new HttpContextAccessor { HttpContext = context });
        Assert.Null(accessor.Current);

        context.Items[HttpIdempotencyKeyAccessor.ItemsKey] = Key;
        Assert.Equal(Key, accessor.Current);
        Assert.Null(new HttpIdempotencyKeyAccessor(null).Current);
    }

    /// <summary>
    /// The order that makes the replay safe, against <c>Program.cs</c> itself: after the token checks (a revoked token
    /// replays nothing) and before the lease and subscription gates (a save done before the cloud locked is still
    /// answered « fait » when pressed again during the lock).
    /// </summary>
    [Fact]
    public void It_Runs_After_The_Token_Checks_And_Before_The_Gates()
    {
        var program = File.ReadAllText(Path.Combine(
            ClinicManagement.UnitTests.Common.SolutionSources.Root().FullName, "ClinicManagement.API", "Program.cs"));
        int At(string name) => program.IndexOf($"UseMiddleware<ClinicManagement.API.Middleware.{name}>", StringComparison.Ordinal);

        var idempotency = At(nameof(IdempotencyMiddleware));
        Assert.True(idempotency > 0, "IdempotencyMiddleware is no longer registered in Program.cs.");
        Assert.True(At("TenantScopeMiddleware") is > 0 and var scope && scope < idempotency, "It must run after the tenant scope is set.");
        Assert.True(At("LocalAuthEnforcementMiddleware") is > 0 and var token && token < idempotency, "It must run after token revocation.");
        Assert.True(idempotency < At("RelayLeaseGateMiddleware"), "It must run before the lease gate.");
        Assert.True(idempotency < At("SubscriptionGateMiddleware"), "It must run before the subscription gate.");
    }
}

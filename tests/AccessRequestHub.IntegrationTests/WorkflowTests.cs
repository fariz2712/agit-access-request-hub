using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AccessRequestHub.Domain;
using AccessRequestHub.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AccessRequestHub.IntegrationTests;

public sealed class WorkflowTests : IAsyncLifetime
{
    private readonly HubFactory factory = new();
    private HttpClient client = null!;

    public async ValueTask InitializeAsync()
    {
        client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HubDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }

    [Theory]
    [InlineData(1, "NonProduction", "Read", "Approved", null)]
    [InlineData(1, "Production", "Read", "PendingSystemOwner", "carol")]
    [InlineData(2, "NonProduction", "Admin", "PendingSystemOwner", "dana")]
    [InlineData(2, "Production", "Admin", "PendingSystemOwner", "dana")]
    public async Task Approval_routes_by_risk_and_assignment(int app, string environment, string level, string afterManager, string? owner)
    {
        var id = await Create(app, environment, level);
        Assert.Equal(id, (await Inbox("bob"))[0].GetProperty("id").GetGuid());
        Assert.Empty(await Inbox(owner ?? "carol"));

        var manager = await Read(await Decide("bob", id, 1), HttpStatusCode.OK);
        Assert.Equal(afterManager, Text(manager, "status"));
        Assert.Equal("v1", Text(manager, "policyVersion"));
        Assert.Empty(await Inbox("bob"));
        if (owner is not null)
        {
            Assert.Equal(id, (await Inbox(owner))[0].GetProperty("id").GetGuid());
            Assert.Equal(HttpStatusCode.Forbidden, (await Decide(owner == "carol" ? "dana" : "carol", id, 2)).StatusCode);
            var final = await Read(await Decide(owner, id, 2), HttpStatusCode.OK);
            Assert.Equal("Approved", Text(final, "status"));
        }

        var events = await Audit(id);
        Assert.Equal(owner is null ? 2 : 3, events.Length);
        Assert.Equal("Created", Text(events[0], "action"));
        Assert.Equal("alice@example.local", Text(events[0], "actor"));
        Assert.Equal("PendingManager", Text(events[1], "previousStatus"));
        Assert.Equal("bob@example.local", Text(events[1], "actor"));
        Assert.Equal("Approved", Text(events[^1], "resultingStatus"));
        Assert.Equal(Enumerable.Range(1, events.Length), events.Select(e => e.GetProperty("version").GetInt32()));
    }

    [Fact]
    public async Task Server_checks_identity_relationship_stage_and_read_scope()
    {
        var id = await Create(environment: "Production");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(null, $"/api/requests/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send("unknown", $"/api/requests/{id}")).StatusCode);
        foreach (var actor in new[] { "alice", "carol", "dana", "erin" })
            Assert.Equal(HttpStatusCode.Forbidden, (await Decide(actor, id, 1)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await Send("dana", $"/api/requests/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send("dana", $"/api/requests/{id}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send("alice", $"/api/requests/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send("bob", "/api/requests?all=true")).StatusCode);
        var all = await Read(await Send("erin", "/api/requests?all=true"), HttpStatusCode.OK);
        Assert.Equal(id, all[0].GetProperty("id").GetGuid());
        Assert.Single(await Audit(id));
    }

    [Fact]
    public async Task Validates_create_and_uses_current_identity()
    {
        var key = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await PostCreate(key, justification: " ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostCreate(key, app: 999)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send("erin", "/api/requests", Input(key))).StatusCode);
        var created = await Read(await PostCreate(key), HttpStatusCode.Created);
        Assert.Equal("alice@example.local", Text(created, "requester"));
    }

    [Fact]
    public async Task Invalid_json_and_enum_values_return_bad_request()
    {
        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/requests");
        malformed.Headers.Add("X-Demo-User", "alice@example.local");
        malformed.Content = new StringContent("{", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(malformed, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostCreate(Guid.NewGuid(), environment: "InvalidEnvironment")).StatusCode);
    }

    [Fact]
    public async Task Demo_routes_are_unavailable_in_production()
    {
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var productionClient = production.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await productionClient.GetAsync("/api/demo/users", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await productionClient.GetAsync("/api/requests", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Retry_reuses_request_and_changed_payload_conflicts()
    {
        var key = Guid.NewGuid();
        var first = await Read(await PostCreate(key), HttpStatusCode.Created);
        var replay = await Read(await PostCreate(key, justification: "  Demo access  "), HttpStatusCode.OK);
        Assert.Equal(Text(first, "id"), Text(replay, "id"));
        Assert.Equal(HttpStatusCode.Conflict, (await PostCreate(key, app: 2)).StatusCode);
        Assert.Single(await Audit(first.GetProperty("id").GetGuid()));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<HubDbContext>().Requests.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Manager_assignment_and_idempotency_are_scoped_to_each_requester()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        db.Users.AddRange(
            new DemoUser { Id = 10, Email = "other-manager@example.local", Role = DemoRole.Manager },
            new DemoUser { Id = 11, Email = "other-requester@example.local", Role = DemoRole.Requester, ManagerId = 10 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var key = Guid.NewGuid();
        var first = await Read(await PostCreate(key), HttpStatusCode.Created);
        var second = await Read(await Send("other-requester", "/api/requests", Input(key)), HttpStatusCode.Created);
        var id = second.GetProperty("id").GetGuid();

        Assert.NotEqual(first.GetProperty("id").GetGuid(), id);
        Assert.Equal(HttpStatusCode.Forbidden, (await Decide("bob", id, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Decide("other-manager", first.GetProperty("id").GetGuid(), 1)).StatusCode);
        await Read(await Decide("other-manager", id, 1), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Application_owner_cannot_decide_own_request()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        await db.Database.ExecuteSqlRawAsync("UPDATE Users SET ManagerId = 2 WHERE Id = 3", TestContext.Current.CancellationToken);
        var created = await Read(await Send("carol", "/api/requests", Input(Guid.NewGuid(), environment: "Production")),
            HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        await Read(await Decide("bob", id, 1), HttpStatusCode.OK);
        Assert.Empty(await Inbox("carol"));
        Assert.Equal(HttpStatusCode.Forbidden, (await Decide("carol", id, 2)).StatusCode);
        Assert.Equal(2, (await Audit(id)).Length);
    }

    [Fact]
    public async Task Overlapping_duplicate_writes_commit_one_request_and_audit_event()
    {
        factory.Writes.WaitForTwo();
        var key = Guid.NewGuid();
        var responses = await Task.WhenAll(PostCreate(key), PostCreate(key));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        var requests = await Task.WhenAll(responses.Select(r => Read(r, r.StatusCode)));
        Assert.Equal(Text(requests[0], "id"), Text(requests[1], "id"));
        Assert.Single(await Audit(requests[0].GetProperty("id").GetGuid()));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<HubDbContext>().Requests.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overlapping_decisions_allow_exactly_one_transition(bool ownerStage)
    {
        var id = await Create(environment: ownerStage ? "Production" : "NonProduction");
        var actor = ownerStage ? "carol" : "bob";
        var version = ownerStage ? 2 : 1;
        if (ownerStage)
            await Read(await Decide("bob", id, 1), HttpStatusCode.OK);
        factory.Writes.WaitForTwo();

        var responses = await Task.WhenAll(Decide(actor, id, version), Decide(actor, id, version, "No business need"));
        var winner = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("Reload", Text(await Read(loser, HttpStatusCode.Conflict), "detail"));
        var winningStatus = Text(await Read(winner, HttpStatusCode.OK), "status");
        var events = await Audit(id);
        Assert.Equal(version + 1, events.Length);
        Assert.Equal(winningStatus, Text(events[^1], "resultingStatus"));
        Assert.Equal(winningStatus, Text(await Read(await Send("alice", $"/api/requests/{id}"), HttpStatusCode.OK), "status"));
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(actor, id, version)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejection_requires_reason_and_ends_workflow(bool ownerStage)
    {
        var id = await Create(environment: "Production");
        var actor = ownerStage ? "carol" : "bob";
        var version = ownerStage ? 2 : 1;
        if (ownerStage)
            await Read(await Decide("bob", id, 1), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(actor, id, version, "   ")).StatusCode);
        var decision = await Read(await Decide(actor, id, version, "  No business need  "), HttpStatusCode.OK);
        Assert.Equal("Rejected", Text(decision, "status"));
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(actor, id, version + 1)).StatusCode);
        var events = await Audit(id);
        Assert.Equal(version + 1, events.Length);
        Assert.Equal("No business need", Text(events[^1], "reason"));
    }

    [Fact]
    public async Task Approved_is_terminal()
    {
        var id = await Create();
        await Read(await Decide("bob", id, 1), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, (await Decide("bob", id, 2, "Changed mind")).StatusCode);
        Assert.Equal(2, (await Audit(id)).Length);
    }

    [Fact]
    public async Task Audit_write_failure_rolls_back_creation_and_transition()
    {
        var id = await Create();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER FailAudit BEFORE INSERT ON AuditEvents
            BEGIN SELECT RAISE(ABORT, 'Simulated audit write failure'); END;
            """, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, (await PostCreate(Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Decide("bob", id, 1)).StatusCode);
        var row = await db.Requests.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(id, row.Id);
        Assert.Equal(RequestStatus.PendingManager, row.Status);
        Assert.Equal(1, row.Version);
        Assert.Single(await Audit(id));
    }

    private async Task<Guid> Create(int app = 1, string environment = "NonProduction", string level = "Read") =>
        (await Read(await PostCreate(Guid.NewGuid(), app, environment, level), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private Task<HttpResponseMessage> PostCreate(Guid key, int app = 1, string environment = "NonProduction",
        string level = "Read", string justification = "Demo access") =>
        Send("alice", "/api/requests", Input(key, app, environment, level, justification));

    private static object Input(Guid key, int app = 1, string environment = "NonProduction",
        string level = "Read", string justification = "Demo access") =>
        new { clientRequestId = key, applicationId = app, environment, accessLevel = level, justification, requester = "erin@example.local" };

    private Task<HttpResponseMessage> Decide(string actor, Guid id, int expectedVersion, string? reason = null) =>
        Send(actor, $"/api/requests/{id}/{(reason is null ? "approve" : "reject")}", new { expectedVersion, reason });

    private async Task<JsonElement[]> Audit(Guid id) =>
        (await Read(await Send("erin", $"/api/requests/{id}/audit"), HttpStatusCode.OK)).EnumerateArray().ToArray();

    private async Task<JsonElement[]> Inbox(string actor) =>
        (await Read(await Send(actor, "/api/approval-inbox"), HttpStatusCode.OK)).EnumerateArray().ToArray();

    private async Task<HttpResponseMessage> Send(string? actor, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path);
        if (actor is not null)
            request.Headers.Add("X-Demo-User", $"{actor}@example.local");
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage result, HttpStatusCode expected)
    {
        using (result)
        {
            var body = await result.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(result.StatusCode == expected, $"Expected {expected}, got {result.StatusCode}: {body}");
            using var json = JsonDocument.Parse(body);
            return json.RootElement.Clone();
        }
    }

    private static string? Text(JsonElement json, string name) => json.GetProperty(name).GetString();
}

internal sealed class HubFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"access-hub-{Guid.NewGuid():N}.db");
    public WriteGate Writes { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Hub"] = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath, ForeignKeys = true, Pooling = false, DefaultTimeout = 5
            }.ToString(),
            ["DemoIdentity:Enabled"] = "true"
        }));
        builder.ConfigureServices(services => services.AddDbContext<HubDbContext>(options => options.AddInterceptors(Writes)));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(databasePath);
    }
}

internal sealed class WriteGate : SaveChangesInterceptor
{
    private TaskCompletionSource? gate;
    private int arrivals;

    public void WaitForTwo()
    {
        arrivals = 0;
        gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (gate is { } pending)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
                pending.TrySetResult();
            await pending.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        return result;
    }
}

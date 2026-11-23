using AccessRequestHub.Application;
using AccessRequestHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.API;

public static class Endpoints
{
    public static void MapDemo(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/demo/users", async (HubDbContext db, CancellationToken ct) =>
            await db.Users.OrderBy(u => u.Id).Select(u => new { u.Email, u.Role }).ToArrayAsync(ct));

        var secured = api.MapGroup("").AddEndpointFilter(async (invocation, next) =>
        {
            var identity = invocation.HttpContext.RequestServices.GetRequiredService<DemoIdentity>();
            return await identity.Resolve(invocation.HttpContext)
                ? await next(invocation)
                : Results.Problem(statusCode: 401, detail: "Select a seeded demo identity.");
        });

        secured.MapGet("/applications", async (HubDbContext db, CancellationToken ct) =>
            await db.Applications.OrderBy(a => a.Id).Select(a => new { a.Id, a.Name }).ToArrayAsync(ct));

        secured.MapGet("/requests", async (bool? all, DemoIdentity identity, RequestService service, CancellationToken ct) =>
            await service.List(identity.User, all is true, ct));
        secured.MapPost("/requests", async (CreateRequest input, DemoIdentity identity, RequestService service, CancellationToken ct) =>
        {
            var (request, created) = await service.Create(input, identity.User, ct);
            return created ? Results.Created($"/api/requests/{request.Id}", request) : Results.Ok(request);
        });
        secured.MapGet("/requests/{id:guid}", async (Guid id, DemoIdentity identity, RequestService service, CancellationToken ct) =>
            await service.Detail(id, identity.User, ct));
        secured.MapGet("/requests/{id:guid}/audit", async (Guid id, DemoIdentity identity, RequestService service, CancellationToken ct) =>
            await service.Audit(id, identity.User, ct));
        secured.MapGet("/approval-inbox", async (DemoIdentity identity, RequestService service, CancellationToken ct) =>
            await service.Inbox(identity.User, ct));
        secured.MapPost("/requests/{id:guid}/approve", async (Guid id, Decision input, DemoIdentity identity, RequestService service, CancellationToken ct) =>
            await service.Decide(id, input, false, identity.User, ct));
        secured.MapPost("/requests/{id:guid}/reject", async (Guid id, Decision input, DemoIdentity identity, RequestService service, CancellationToken ct) =>
            await service.Decide(id, input, true, identity.User, ct));
    }
}

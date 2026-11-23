using AccessRequestHub.Domain;
using AccessRequestHub.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.Application;

public sealed record CreateRequest(Guid ClientRequestId, int ApplicationId, AccessEnvironment? Environment,
    AccessLevel? AccessLevel, string? Justification);
public sealed record Decision(int ExpectedVersion, string? Reason);
public sealed record RequestView(Guid Id, string Requester, string Application, AccessEnvironment Environment,
    AccessLevel AccessLevel, string Justification, RequestStatus Status, string PolicyVersion,
    int Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, bool CanDecide);
public sealed record AuditView(int Version, string Actor, AuditAction Action, RequestStatus? PreviousStatus,
    RequestStatus ResultingStatus, DateTime OccurredAtUtc, string? Reason);

public sealed class RequestError(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class RequestService(HubDbContext db, TimeProvider time)
{
    private IQueryable<AccessRequest> WithDetails => db.Requests.Include(r => r.Requester).Include(r => r.Application);

    public async Task<RequestView[]> List(DemoUser actor, bool all, CancellationToken ct)
    {
        if (all && actor.Role != DemoRole.Auditor)
            throw new RequestError(403, "Only the auditor can list all requests.");
        var requests = WithDetails.AsNoTracking();
        if (!all)
            requests = requests.Where(r => r.RequesterId == actor.Id);
        return await ToViews(requests, actor, ct);
    }

    public Task<RequestView[]> Inbox(DemoUser actor, CancellationToken ct)
    {
        IQueryable<AccessRequest> requests = WithDetails.AsNoTracking().Where(r => r.RequesterId != actor.Id);
        requests = actor.Role switch
        {
            DemoRole.Manager => requests.Where(r => r.Status == RequestStatus.PendingManager && r.Requester.ManagerId == actor.Id),
            DemoRole.SystemOwner => requests.Where(r => r.Status == RequestStatus.PendingSystemOwner && r.Application.OwnerId == actor.Id),
            _ => requests.Where(r => false)
        };
        return ToViews(requests, actor, ct);
    }

    private static async Task<RequestView[]> ToViews(IQueryable<AccessRequest> query, DemoUser actor, CancellationToken ct)
    {
        var requests = await query.OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id).ToArrayAsync(ct);
        return requests.Select(r => View(r, actor)).ToArray();
    }

    public async Task<RequestView> Detail(Guid id, DemoUser actor, CancellationToken ct) =>
        View(await ReadableRequest(id, actor, ct), actor);

    public async Task<AuditView[]> Audit(Guid id, DemoUser actor, CancellationToken ct)
    {
        await ReadableRequest(id, actor, ct);
        var events = await db.AuditEvents.AsNoTracking().Where(e => e.RequestId == id)
            .OrderBy(e => e.RequestVersion)
            .Select(e => new AuditView(e.RequestVersion, e.Actor.Email, e.Action, e.PreviousStatus,
                e.ResultingStatus, e.OccurredAtUtc, e.Reason)).ToArrayAsync(ct);
        return events.Select(e => e with { OccurredAtUtc = Utc(e.OccurredAtUtc) }).ToArray();
    }

    private async Task<AccessRequest> ReadableRequest(Guid id, DemoUser actor, CancellationToken ct)
    {
        var request = await WithDetails.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new RequestError(404, "Request not found.");
        if (!ApprovalPolicy.CanRead(actor, request))
            throw new RequestError(403, "You cannot view this request.");
        return request;
    }

    public async Task<(RequestView Request, bool Created)> Create(CreateRequest input, DemoUser actor, CancellationToken ct)
    {
        var justification = input.Justification?.Trim();
        if (input.ClientRequestId == Guid.Empty || input.ApplicationId <= 0
            || input.Environment is not { } environment || !Enum.IsDefined(environment)
            || input.AccessLevel is not { } accessLevel || !Enum.IsDefined(accessLevel)
            || string.IsNullOrEmpty(justification) || justification.Length > 2000)
            throw new RequestError(400, "Provide a client request ID, application, environment, access level, and justification (1–2000 characters).");

        Task<AccessRequest?> FindReplay() => WithDetails.AsNoTracking().SingleOrDefaultAsync(
            r => r.RequesterId == actor.Id && r.ClientRequestId == input.ClientRequestId, ct);

        (RequestView, bool) Replay(AccessRequest existing)
        {
            if (existing.ApplicationId != input.ApplicationId || existing.Environment != environment
                || existing.AccessLevel != accessLevel || existing.Justification != justification)
                throw new RequestError(409, "This client request ID already belongs to different request details.");
            return (View(existing, actor), false);
        }

        var existing = await FindReplay();
        if (existing is not null)
            return Replay(existing);
        if (actor.ManagerId is not int managerId
            || !await db.Users.AnyAsync(u => u.Id == managerId && u.Role == DemoRole.Manager && u.Id != actor.Id, ct))
            throw new RequestError(403, "Your identity has no eligible assigned manager.");

        var application = await db.Applications.FindAsync([input.ApplicationId], ct)
            ?? throw new RequestError(400, "Select an existing application.");
        var now = time.GetUtcNow().UtcDateTime;
        var request = new AccessRequest
        {
            ClientRequestId = input.ClientRequestId,
            Requester = actor,
            RequesterId = actor.Id,
            Application = application,
            ApplicationId = application.Id,
            Environment = environment,
            AccessLevel = accessLevel,
            Justification = justification,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Requests.Add(request);
        AddAudit(request, actor, AuditAction.Created, null, null);
        try
        {
            await db.SaveChangesAsync(ct);
            return (View(request, actor), true);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 } sql
            && sql.Message.Contains("Requests.RequesterId, Requests.ClientRequestId", StringComparison.Ordinal))
        {
            db.ChangeTracker.Clear();
            var winner = await FindReplay();
            if (winner is null)
                throw;
            return Replay(winner);
        }
    }

    public async Task<RequestView> Decide(Guid id, Decision input, bool reject, DemoUser actor, CancellationToken ct)
    {
        var reason = input.Reason?.Trim();
        if (input.ExpectedVersion < 1)
            throw new RequestError(400, "Provide a positive expectedVersion.");
        if (reject && (string.IsNullOrEmpty(reason) || reason.Length > 1000))
            throw new RequestError(400, "Provide a rejection reason (1–1000 characters).");

        var request = await WithDetails.SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new RequestError(404, "Request not found.");
        if (actor.Id == request.RequesterId
            || !(ApprovalPolicy.IsManager(actor, request) || ApprovalPolicy.IsOwner(actor, request)))
            throw new RequestError(403, "You are not an assigned approver for this request.");
        if (request.Version != input.ExpectedVersion || request.Status is RequestStatus.Approved or RequestStatus.Rejected)
            throw Conflict();
        if (!ApprovalPolicy.CanDecide(actor, request))
            throw new RequestError(403, "You cannot decide this approval stage.");

        var previous = request.Status;
        request.Status = reject ? RequestStatus.Rejected : ApprovalPolicy.Approve(request);
        request.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        request.Version++;
        AddAudit(request, actor, reject ? AuditAction.Rejected : AuditAction.Approved, previous, reject ? reason : null);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict();
        }
        return View(request, actor);
    }

    private void AddAudit(AccessRequest request, DemoUser actor, AuditAction action, RequestStatus? previous, string? reason)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            RequestId = request.Id,
            ActorId = actor.Id,
            Action = action,
            RequestVersion = request.Version,
            PreviousStatus = previous,
            ResultingStatus = request.Status,
            OccurredAtUtc = request.UpdatedAtUtc,
            Reason = reason
        });
    }

    private static RequestError Conflict() => new(409, "This request has changed or already ended. Reload it before taking another action.");

    private static RequestView View(AccessRequest request, DemoUser actor) => new(request.Id, request.Requester.Email,
        request.Application.Name, request.Environment, request.AccessLevel, request.Justification, request.Status,
        request.PolicyVersion, request.Version, Utc(request.CreatedAtUtc), Utc(request.UpdatedAtUtc),
        ApprovalPolicy.CanDecide(actor, request));

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

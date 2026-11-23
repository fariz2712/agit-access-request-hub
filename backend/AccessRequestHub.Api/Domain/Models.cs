namespace AccessRequestHub.Domain;

public enum DemoRole { Requester, Manager, SystemOwner, Auditor }
public enum AccessEnvironment { NonProduction, Production }
public enum AccessLevel { Read, Admin }
public enum RequestStatus { PendingManager, PendingSystemOwner, Approved, Rejected }
public enum AuditAction { Created, Approved, Rejected }

public sealed class DemoUser
{
    public int Id { get; init; }
    public required string Email { get; init; }
    public DemoRole Role { get; init; }
    public int? ManagerId { get; init; }
}

public sealed class ManagedApplication
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public int OwnerId { get; init; }
}

public sealed class AccessRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ClientRequestId { get; init; }
    public int RequesterId { get; init; }
    public DemoUser Requester { get; init; } = null!;
    public int ApplicationId { get; init; }
    public ManagedApplication Application { get; init; } = null!;
    public AccessEnvironment Environment { get; init; }
    public AccessLevel AccessLevel { get; init; }
    public required string Justification { get; init; }
    public string PolicyVersion { get; init; } = "v1";
    public RequestStatus Status { get; set; } = RequestStatus.PendingManager;
    public int Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class AuditEvent
{
    public long Id { get; init; }
    public Guid RequestId { get; init; }
    public int ActorId { get; init; }
    public DemoUser Actor { get; init; } = null!;
    public int RequestVersion { get; init; }
    public AuditAction Action { get; init; }
    public RequestStatus? PreviousStatus { get; init; }
    public RequestStatus ResultingStatus { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public string? Reason { get; init; }
}

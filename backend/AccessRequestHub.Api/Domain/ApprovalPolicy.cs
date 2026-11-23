namespace AccessRequestHub.Domain;

public static class ApprovalPolicy
{
    public static bool IsManager(DemoUser actor, AccessRequest request) =>
        actor.Role == DemoRole.Manager && request.Requester.ManagerId == actor.Id;

    public static bool IsOwner(DemoUser actor, AccessRequest request) =>
        actor.Role == DemoRole.SystemOwner && request.Application.OwnerId == actor.Id;

    public static bool CanRead(DemoUser actor, AccessRequest request) =>
        actor.Id == request.RequesterId || actor.Role == DemoRole.Auditor
        || IsManager(actor, request) || IsOwner(actor, request);

    public static bool CanDecide(DemoUser actor, AccessRequest request) =>
        actor.Id != request.RequesterId && request.Status switch
        {
            RequestStatus.PendingManager => IsManager(actor, request),
            RequestStatus.PendingSystemOwner => IsOwner(actor, request),
            _ => false
        };

    public static RequestStatus Approve(AccessRequest request) =>
        request.PolicyVersion switch
        {
            "v1" when request.Status == RequestStatus.PendingManager
                && (request.Environment == AccessEnvironment.Production || request.AccessLevel == AccessLevel.Admin)
                => RequestStatus.PendingSystemOwner,
            "v1" => RequestStatus.Approved,
            _ => throw new InvalidOperationException("Unsupported approval policy version.")
        };
}

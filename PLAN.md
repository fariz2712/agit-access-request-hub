# Access Request Hub | Phase 1 plan

## Problem and acceptance criteria

Replace email and chat approvals with a local system of record for access requests, approval decisions, and their audit history. The assessor must be able to create a request as Alice, switch identities, complete or reject it in the correct order, and inspect the resulting timeline. The system must preserve one request per idempotency key and one successful transition per request version, even when operations overlap.

This milestone records the design and baseline. Implementation starts after candidate approval. The assessment allocates 4–8 effective hours across both phases and documentation. Phase 2 requires the assessor's change request.

## Architecture

- One ASP.NET Core Web API project in `backend/AccessRequestHub.Api`, organized into API, application, domain, and infrastructure folders. Thin endpoints resolve identity and map HTTP responses. An application service coordinates validation, authorization, persistence, and audit. A domain policy defines allowed transitions. A scoped EF Core DbContext manages SQLite persistence through migrations.
- One React and TypeScript Vite app in `frontend/`. Use a seeded identity switcher, create form, My Requests, Approval Inbox, and request detail with an audit timeline. Erin gets an all-requests view. Include validation, loading, empty, and error states. Clear identity-scoped state on user switch and discard responses from the previous session. Use the Vite proxy during local development.
- One xUnit project in `tests/AccessRequestHub.IntegrationTests` using `WebApplicationFactory` and a temporary file-backed SQLite database per test or isolated fixture. Use production migrations and seed data. Concurrent test requests must use separate HTTP requests and database contexts.
- The workspace has .NET SDK 8.0.421, Node 24.21.0, and npm 11.19.0. Start from .NET 8 and matching EF Core and ASP.NET Core testing packages. Verify supported stable React, Vite, and TypeScript versions against Node before scaffolding and commit the npm lockfile.

## Relational model

| Table | Fields and constraints |
| --- | --- |
| Users | Stable primary key, unique email, demo role, nullable ManagerId referencing Users. |
| Applications | Stable primary key, unique name, required OwnerId referencing Users. |
| AccessRequests | Primary key, required RequesterId and ApplicationId foreign keys, ClientRequestId UUID, Environment, AccessLevel, Justification, Status, PolicyVersion, Version, CreatedAtUtc, UpdatedAtUtc. Unique (RequesterId, ClientRequestId). Version starts at 1 and is an EF Core concurrency token. Required fields and check constraints cover allowed values, positive versions, and nonblank justification. |
| AuditEvents | Primary key, required RequestId and ActorId foreign keys, resulting RequestVersion, event type, previous status (null on creation), resulting status, UTC timestamp, and decision reason where applicable. Unique (RequestId, RequestVersion). Require a nonblank reason for rejection. Order the timeline by resulting version. |

Enforce SQLite foreign keys on application connections. Seed stable identifiers through the initial migration for reproducible databases.

| Demo identity | Responsibility |
| --- | --- |
| alice@example.local | Requester reporting to Bob |
| bob@example.local | Manager for Alice |
| carol@example.local | System Owner for CRM |
| dana@example.local | System Owner for Finance Portal |
| erin@example.local | Admin/Auditor with global request and audit visibility and no approval assignment |

Environment values are `NonProduction` and `Production`. Access levels are `Read` and `Admin`. Persist `v1` on creation and use the stored policy version when evaluating decisions.

## Workflow and authorization

| Current state | Authorized actor | Decision | Result |
| --- | --- | --- | --- |
| New | Current requester with a manager assignment | Create | PendingManager |
| PendingManager | Requester's direct manager | Approve NonProduction/Read | Approved |
| PendingManager | Requester's direct manager | Approve Production or Admin | PendingSystemOwner |
| PendingManager | Requester's direct manager | Reject with reason | Rejected |
| PendingSystemOwner | Application's assigned owner | Approve | Approved |
| PendingSystemOwner | Application's assigned owner | Reject with reason | Rejected |

Approved and Rejected are terminal. A requester cannot decide their own request. Check the actor's role, relationship, and workflow stage on the server for every decision. The demo header supplies a seeded identity identifier. The server looks up role and assignments in SQLite and derives the requester from that identity. Enable simulated identity only for the local demo configuration.

Read access covers the requester, their assigned manager, the application's assigned owner, and Erin. Apply the same visibility predicate to detail and audit. The inbox narrows visibility to requests the current identity can act on at that stage. An otherwise authorized approver submitting an outdated version receives a conflict even if the winning decision already advanced the stage. Unrelated identities receive forbidden.

## HTTP contract

| Method and route | Behavior |
| --- | --- |
| GET /api/demo/users | Seeded identities for the local switcher. |
| GET /api/applications | Master data for the create form. |
| POST /api/requests | Accept clientRequestId, applicationId, environment, accessLevel, and justification. Return 201 with Location on first creation, 200 on an equivalent replay, or 409 for a replay with different normalized input. |
| GET /api/requests | List own requests by default. A global view is available to Erin. |
| GET /api/requests/{id} | Return request detail, version, and allowed actions for the caller. |
| GET /api/approval-inbox | Return only requests awaiting the caller's decision. |
| POST /api/requests/{id}/approve | Require expectedVersion and return the updated request. |
| POST /api/requests/{id}/reject | Require expectedVersion and a trimmed, nonblank reason. Return the updated request. |
| GET /api/requests/{id}/audit | Return the authorized timeline ordered by request version. |

Use Problem Details with 400 for validation errors, 401 for missing or unknown demo identity on protected endpoints, 403 for forbidden access, 404 for missing resources, and 409 for stale versions, terminal decisions by otherwise eligible actors, or conflicting idempotency replays. Log HTTP correlation, failed operations, and unexpected storage errors without logging justification or rejection text.

## Concurrency and audit

- Keep a client-generated UUID for one logical submit, including retry after an ambiguous network failure. Generate a fresh UUID for a new submission. Normalize payload values consistently before replay comparison. On a unique-key collision, roll back the attempted insert, read the winner with a clean context, and compare the persisted input. Handle the targeted duplicate constraint explicitly and preserve unrelated database failures.
- Require expectedVersion from the client and increment Version for each successful decision. EF must use the original version in the update predicate. On a lost race, roll back and return a clear 409. The UI reloads the detail and inbox after a conflict and requires the user to review the current state before making another decision.
- Save the change and corresponding audit event in one relational transaction through a single SaveChanges call. Creation writes the initial event. Every successful decision writes exactly one event carrying actor, timestamp, state change and, for a rejection, the reason. Failed actions and equivalent replays leave the business timeline unchanged. Expose audit reads only through normal application routes.
- Keep write transactions short. Exercise SQLite lock contention and constraint behavior with overlapping requests. Any retry for transient busy errors must be bounded and repeat the whole operation with a clean context and the original client key or expected version. Surface persistent database unavailability as a retriable service error.

## Milestones and verification

| Milestone | Work and verification |
| --- | --- |
| 0. Planning | Review all five assessment pages, record this plan, add repository exclusions, and tag the initial commit `assessment-start`. |
| 1. Persistence and API | Scaffold the API, add migration and seed data, implement validation, authorization, transitions, idempotency, and audit. Create a fresh database via migrations and inspect seed rows. |
| 2. Integration tests | Test the HTTP contract through the real SQLite provider. Run the suite repeatedly to check overlap behavior. |
| 3. Frontend | Connect every workflow, including role-specific views and conflict recovery. Build with `npm ci` and `npm run build` in `frontend/` and smoke-test the demo scenarios in the browser. |
| 4. Reproducibility and review | Run the backend build and xUnit suite, repeat setup from an empty database, check tracked files, write evidence, and tag the verified Phase 1 commit `phase-1-complete`. Prepare local commits and tags for candidate review before pushing. |

Planned commands from repository root include `dotnet tool restore`, `dotnet ef database update --project backend/AccessRequestHub.Api`, `dotnet build`, and `dotnet test`. Document final commands and prerequisites in README after verifying them.

| Test coverage | Expected result |
| --- | --- |
| Alice requests CRM NonProduction/Read | Bob approves directly. History contains creation and one decision. |
| Alice requests CRM Production/Read | Bob advances to owner review. Carol approves. Dana cannot act. |
| Alice requests Finance Portal NonProduction/Admin | Bob advances to owner review. Dana approves. Carol cannot act. |
| Direct authorization checks | Wrong manager or owner, self-decision, Erin decision, stage bypass, spoofed requester, and out-of-scope reads are prevented. Use a test-only user relationship for a nonassigned manager if needed. |
| Sequential and overlapping duplicate submissions | Same key and equivalent payload converge on one request and one creation event. Changed payload receives 409. Verify scoping across requester IDs. |
| Stale version and overlapping approve/reject | Exactly one action succeeds for the supplied version. The loser receives 409. Persisted state and audit match the winner. |
| Reject at either stage | Blank reason fails validation. A valid reason is visible in audit. Later decisions against the terminal request fail. |
| Atomic audit | A forced audit persistence failure rolls back the corresponding request creation or transition. Check stored status, version, and event count using a fresh context. |

## Trade-offs

1. SQLite reduces local setup. It serializes writers, so test real file-backed contention and keep transactions short.
2. Seeded identity switching makes demo scenarios reproducible. It is a local simulation and cannot establish production identity.
3. Requester-scoped keys allow safe independent submissions. A changed payload with the same key returns a conflict so that a replay cannot silently replace the original intent.

## Evidence and plan changes

Prepare README for setup and demo, AI_USAGE for actual AI interactions and observed mistakes, REVIEW for findings and verification evidence, and INTEGRITY as an unsigned declaration for the candidate to review and complete. Record departures from this baseline here as implementation proceeds. Application builds, migrations, and tests are pending implementation.

### Phase 1 implementation adjustments

- SQLite's provider-level lock wait is bounded by the command timeout. The API returns a retriable 503 when the timeout is exhausted. This avoids a second automatic retry loop and keeps the caller's original UUID or version available for a deliberate retry.
- The integration suite uses a save interceptor to synchronize concurrent writers after validation and lookup. A separate test-only SQLite trigger forces audit persistence to fail and checks that the request transaction rolls back.
- Malformed JSON is handled as a client error in Development. The generated xUnit v2 dependency set was replaced with xUnit v3 after NuGet reported advisories in transitive packages.
- REVIEW.md records the final verification results and known limitations.

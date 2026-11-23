# Access Request Hub

Access Request Hub is a local demo for internal application access. A requester submits a justification, the assigned manager reviews it, and the application owner reviews high-risk requests. The request detail includes the current status and its audit history.

## Stack and prerequisites

- .NET 8 SDK, ASP.NET Core Web API, EF Core 8, and SQLite.
- Node.js 24 with npm, React 19, TypeScript, and Vite.
- xUnit and ASP.NET Core TestServer with temporary file-backed SQLite databases.
- NuGet and npm access for the first installation. The test runner uses local loopback communication.

This assessment uses the workspace's approved .NET 8 target. See REVIEW.md for the runtime support limitation and deployment prerequisites.

## Install, migrate, and run

From the repository root:

```sh
dotnet restore
dotnet tool restore
dotnet ef database update --project backend/AccessRequestHub.Api
dotnet run --project backend/AccessRequestHub.Api --launch-profile local
```

EF Core creates the database and seeds users and applications in the initial migration. The default database is `backend/AccessRequestHub.Api/access-request-hub.db`. For a fresh database without disturbing existing data, use a new absolute path in `ConnectionStrings__Hub` for both the migration and API commands. Keep `Foreign Keys=True;Default Timeout=5` in that connection string.

In a second terminal:

```sh
cd frontend
npm ci
npm run dev
```

Open [the local UI](http://127.0.0.1:5173). Vite proxies `/api` to the API on port 5080. The launch profile binds to loopback and enables the development environment. The demo API uses `X-Demo-User` to select an identity. Anyone who can reach the development API can impersonate a seeded user, so keep it local. The demo routes are unavailable outside Development or when `DemoIdentity:Enabled` is false.

## Demo users

| Identity | Responsibility |
| --- | --- |
| alice@example.local | Submit requests and view own history |
| bob@example.local | Review requests from Alice, his direct report |
| carol@example.local | Review high-risk requests for CRM |
| dana@example.local | Review high-risk requests for Finance Portal |
| erin@example.local | View all requests and timelines as Admin/Auditor |

## Demo scenarios

1. **Standard.** As Alice, request CRM / Non-production / Read. Switch to Bob and approve from Approval Inbox. The detail shows Approved.
2. **Production.** Submit CRM / Production / Read as Alice. Bob's approval moves the request to Pending system owner. Switch to Carol to approve it.
3. **Admin.** Submit Finance Portal / Non-production / Admin as Alice. Bob's approval sends it to Dana's inbox. Dana can approve it.
4. **Unauthorized.** The API returns 403 if Erin attempts to approve, Carol tries to bypass Bob, or the wrong owner attempts a decision. Use the integration suite to exercise these direct API calls.
5. **Duplicate.** Two submissions with the same requester, client UUID, and normalized payload return the same request ID. The integration suite covers sequential replay and overlapping submissions.
6. **Concurrent action.** Open the same pending request in two Bob sessions before either decides. Approve in one and submit a decision from the stale session. The latter refreshes the detail after a 409. The integration suite also forces overlapping saves at both stages.
7. **Rejected.** As an assigned approver, choose Reject and enter a reason. The timeline shows the reason, and the terminal request no longer permits decisions.

Switch to Erin to review all terminal and pending requests. The timeline orders events by request version and records actor, time, and before/after status.

## Build and test

From the repository root:

```sh
dotnet build
dotnet test
npm --prefix frontend ci
npm --prefix frontend run build
```

The suite creates isolated databases from the checked-in migration. It covers workflow routing, authorization, validation, idempotency, stale and concurrent decisions, terminal states, and rollback when audit persistence fails. The test storage and development database are independent.

## API and persistence

| Method and path | Purpose |
| --- | --- |
| GET /api/demo/users | List profiles for the local switcher |
| GET /api/applications | Return CRM and Finance Portal for the form |
| POST /api/requests | Submit with `clientRequestId`, `applicationId`, `environment`, `accessLevel`, and `justification` |
| GET /api/requests | List own requests. Erin can use `?all=true` |
| GET /api/approval-inbox | List requests currently actionable by the caller |
| GET /api/requests/{id} | Retrieve request detail and allowed decision state |
| GET /api/requests/{id}/audit | Retrieve the request timeline |
| POST /api/requests/{id}/approve | Approve using `expectedVersion` |
| POST /api/requests/{id}/reject | Reject using `expectedVersion` and `reason` |

The requester identity and assignments come from the database. API environments are `NonProduction` and `Production`, and access levels are `Read` and `Admin`. Production or Admin triggers a second approval. Each request records policy `v1`.

An equivalent retry returns 200 and the original request ID. First creation returns 201. Invalid input returns 400, missing or unknown demo identity returns 401, forbidden access returns 403, missing resources return 404, and stale, terminal, or mismatched replay attempts return 409. A storage lock that outlasts the database timeout returns a retriable 503. EF Core writes the request and its audit event in one transaction and enforces decisions against the original version.

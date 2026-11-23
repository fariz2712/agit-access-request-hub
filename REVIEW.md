# Phase 1 engineering review

Verified on Windows on 23 November 2026 using .NET SDK 8.0.421, ASP.NET Core runtime 8.0.27, Node 24.21.0, npm 11.19.0, and Microsoft Edge in headless mode. The locally verified scope is the Phase 1 demonstration.

## Verification evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Tool restore and database setup | Passed | `dotnet tool restore` resolved EF CLI 8.0.34. `dotnet ef database update --project backend/AccessRequestHub.Api` applied `InitialSchema` after rebuilding. The browser check used another fresh SQLite file and confirmed all five seeded users through the live API. |
| Backend build | Passed | `dotnet build` completed with 0 warnings and 0 errors. |
| Frontend clean install and build | Passed | `npm ci` installed the pinned dependency tree. `npm run build` completed TypeScript checking and Vite bundling. The first clean install failed because a Vite process held a native binary open. A sequential rerun passed. |
| Integration test suite | Passed | `dotnet test` reported 18 passed, 0 failed, and 0 skipped. Each test uses a fresh migrated SQLite database. All three overlapping-write cases passed again under the filtered rerun. |
| Browser smoke check | Passed | Local API and Vite exercised standard, Production, and Admin approval, rejection and audit reason, a stale second decision with refresh, and Erin's all-requests view. The list fit a 360 px viewport without horizontal overflow and no page errors were observed. The exploratory browser script was run locally and is not part of the repository test suite. |
| Package advisories | Passed | `npm audit` reported zero vulnerabilities. After the xUnit upgrade, `dotnet list AccessRequestHub.sln package --vulnerable --include-transitive` reported no vulnerable packages for either project using the configured NuGet source. This check covers packages and does not assess the installed shared runtime. |

## Assessment scenarios

| Scenario | Result | Verified behavior |
| --- | --- | --- |
| Standard | Passed | Bob's approval completed Alice's CRM NonProduction/Read request. Audit recorded Alice's submission and Bob's decision. |
| Production | Passed | Bob sent the CRM Production/Read request to Carol. Carol completed it. |
| Admin | Passed | Bob sent the Finance Portal NonProduction/Admin request to Dana. Dana completed it. |
| Unauthorized | Passed | Direct API tests denied unassigned managers and owners, owner review before the manager, Erin's decision, a requester's self-approval, and out-of-scope detail and audit reads. |
| Duplicate | Passed | Sequential and overlapping retries converged on one request and one creation event. Changed payload returned 409. Another requester could use the same UUID independently. |
| Concurrent action | Passed | Approve and reject against the same version yielded one success and one 409 at both stages. Persisted status and audit matched the winning operation. The browser showed a conflict and reloaded the detail after a stale action. |
| Rejected | Passed | Both stages required a reason. Timelines retained the reason, and further decisions against the terminal state returned 409. Approved requests were also terminal. |

## Findings and actions

| Finding | Severity | Action | Status | Evidence |
| --- | --- | --- | --- | --- |
| Malformed JSON in Development surfaced as HTTP 500. | Medium | Configure minimal API binding to return a client error and let the Problem Details middleware provide the response. | Resolved | `Invalid_json_and_enum_values_return_bad_request` initially failed and passed after the change. |
| xUnit v2 brought in two packages flagged by NuGet. | High | Replaced the generated test dependency set with xUnit v3 and updated the VSTest adapter. Removed the unused coverage collector. | Resolved | `dotnet nuget why` traced the legacy packages to xUnit v2. Full transitive advisory scan passed after the upgrade. |
| An audit insert can fail after the business operation has started. | High | Save the business change and audit event in a single EF transaction. | Verified | `Audit_write_failure_rolls_back_creation_and_transition` uses a SQLite trigger to force the failure and verifies the previous request and audit state from a database context. |
| Simulated identities are impersonable by a caller with access to the demo server. | High outside the local demo | Bind the launch profile and Vite to loopback. Gate demo routes on Development and an explicit enable flag. | Local mitigation verified | `Demo_routes_are_unavailable_in_production` and direct authorization tests. Production authentication is deferred. |
| .NET 8 ended support on 10 November 2026. | High outside the local demo | Retained the approved runtime target available in the assessment workspace. Move to a supported .NET release and repeat build, security, migration, and workflow verification before deployment. | Deferred | [Microsoft .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) |
| SQLite serializes writes and can time out under sustained contention. | Medium | Keep writes short, restrict replay recovery to the expected unique constraint, and expose persistent storage lock failures as a retriable 503. | Demo concurrency verified | Overlapping-write tests passed. Throughput and extended lock saturation were outside the timebox. |
| The audit timeline is append-only at the API level. | Medium outside the local demo | Restrict filesystem access and design backup, retention, and tamper-evidence controls before using real company data. | Deferred | There is no update or delete route for audit events. A user with direct SQLite file access can alter them. |

## Scope limits and candidate review

- The form preserves a client UUID for retries with an unchanged payload during the mounted form session. A page reload or a new form starts a new logical submission.
- Browser verification covered Edge and the recorded viewport check. A broader browser, accessibility, and load matrix was outside the Phase 1 timebox.
- The candidate still needs to reproduce setup locally, review the concurrency and authorization decisions, verify the AI usage record, and complete the unsigned integrity declaration before submission.
- Production authentication, operational monitoring and retention, deployment, and the assessor's Phase 2 change request are deferred.

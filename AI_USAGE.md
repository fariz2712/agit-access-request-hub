# AI usage

Codex assisted with planning, implementation, troubleshooting, and verification. The entries below describe work observed in the assessment chat and local repository. The candidate should verify this record before submission and add any relevant interactions from outside this chat.

| Request | AI suggestion or action | Disposition and rationale |
| --- | --- | --- |
| Read the full assessment and produce a Phase 1 plan before coding. | Proposed one ASP.NET project, one React app, SQLite migrations and seed data, and a database-enforced workflow. | Accepted by the candidate before implementation. This kept the critical workflow in one relational transaction and gave the assessor a local setup. |
| Prove duplicate submission and stale or concurrent decisions. | Used xUnit with the actual SQLite provider and a save interceptor to pause two requests after they had read the database. | Adopted. A timing-only parallel test could pass while exercising only sequential retries. The barrier ensures both operations reach the persistence boundary. |
| Verify migrations, test execution, and the frontend build. | Initially tried the migration with `--no-build` after scaffolding it and ran isolated build commands. Rebuilt the solution, then reran migration and tests with fresh databases and loopback access for VSTest. | Changed after examining command output. Only the run applying the migration with the rebuilt assembly, and subsequent fresh-database runs, count as evidence. |
| Verify frontend workflows in a real browser. | Ran a browser smoke check over the local Vite and API processes, then revised form labels after the check could not locate the application field by exact label. | Changed to explicit label/control associations for the form selects. Verification outcomes are recorded in REVIEW.md. |

## Three things AI got wrong

1. The first migration check reused the assembly built before the new migration existed. The command reported that the database was up to date. Its output did not establish that the schema had been applied. Rebuilding and verifying application of `InitialSchema` against fresh databases corrected the check.
2. The first browser smoke script expected the wrapped application select to have the exact label `Application`. The lookup timed out. The form now associates standalone labels with selects by ID.
3. The first clean npm install ran while a smoke-test Vite process held the Windows Rolldown native binary open. `npm ci` failed with `EPERM`. The install and smoke run were reordered to avoid sharing that resource.

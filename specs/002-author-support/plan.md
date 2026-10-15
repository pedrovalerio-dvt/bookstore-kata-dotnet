# Implementation Plan: FEAT-007 Author Support

**Branch**: `main` (planning only; no feature branch created)
**Feature identifier**: `002-author-support`
**Date**: 2026-10-15
**Spec**: [Approved scope and recorded assumptions](spec.md)
**Baseline**: `74e0899`, API contract 1.1.0
**Status**: Implemented locally; see [release evidence](release.md) for measured
results and remaining target-environment acceptance.

## Summary

Introduce Author as a first-class catalogue aggregate with create, list, get,
and soft-delete operations. Add optional `authorId` to books while retaining
the existing author credit and text search. Names are not identity keys.
Legacy associations require reviewed mappings, not automatic deduplication.

The user requires strict ordering between new links and Author retirement.
Move the standalone MongoDB deployment to a replica set and implement
transactional coordination that writes the Author document as well as the book.
A transactional read alone is insufficient to prevent the stale-read race.

## Technical Context

**Language/Version**: C# on .NET 10; existing Minimal API application.

**Primary Dependencies**: Existing MongoDB.Driver, FluentValidation, Serilog,
ASP.NET OpenAPI/Scalar, and Microsoft DI. Use central package management.
No new dependency is currently justified.

**Storage**: Existing MongoDB `books` collection plus `authors`; BSON ObjectId
references and UTC timestamps. Retain pinned `mongo:7.0.28-jammy` unless a
separate compatibility requirement warrants a reviewed dependency change.

**Testing**: xUnit/Moq, WebApplicationFactory, real MongoDB Testcontainers,
Coverlet, and existing PowerShell coverage gate.

**Target Platform**: Existing non-root Linux API container; Windows development.
Local/CI MongoDB becomes a single-node replica set. Production requires an
operator-approved replica-set topology, reachable advertised hostnames,
appropriate durability, and access protection.

**Project Type**: HTTP API with Core, Infrastructure, and API projects; no frontend.

**Performance Goals**: Exact `authorId` filtering must use an eligible index.
Measure transaction contention and representative catalogue queries; do not
invent an unmeasured latency SLA or imply substring search is index-backed.

**Constraints**: At least 70% overall and 85% Core line coverage; no generated
code exclusions; no PII in logs/errors; existing book behavior must remain valid.
No direct MongoDB types in Core or API contracts.

**Scale/Scope**: Four new Author operations, one optional book reference, one
additional book collection filter, reviewed association tooling, and topology/
operational changes. Author profile editing, restore, hard delete, pagination,
co-authorship, automatic identity resolution, and a new identity provider are excluded.

## Constitution Check

The constitution file is an unfilled template. Its example principles are not
ratified requirements. The applicable repository architecture instructions,
existing implementation patterns, and confirmed feature requirements govern.

| Gate | Design response | Result |
|---|---|---|
| Aggregate boundaries | Author owns profile/lifecycle; Book owns inventory and its optional link; no embedded book collection in Author | Pass |
| Domain invariants | Immutable Author factory/deactivation behavior; validated requests; strict coordinated writes for link/retirement | Pass |
| SRP/DIP | Endpoints translate HTTP, Core orchestrates domain operations through narrow interfaces, Infrastructure owns BSON/sessions | Pass |
| Async/cancellation | Driver calls and transaction callbacks are awaited and receive request cancellation; never parallel operations on one session | Pass |
| Tests | Unit, HTTP, database, concurrency, and full-workflow tests use `MethodName_Condition_ExpectedResult` | Planned gate |
| Coverage/format | Existing full coverage script and formatter; no exclusions or lowered thresholds | Planned gate |
| Documentation | Draft contracts here; regenerate actual OpenAPI only after implementation; update business/technical/release guides | Planned gate |
| Authorization/data protection | User explicitly chose unchanged application access behavior; external access approval required for profile/email exposure | Explicit limitation |
| Events/audit | No external event consumers or durable audit requirement supplied; structured committed-transition logs only | Justified scope |
| Financial compliance | No money/payment rules introduced; no PCI-DSS, SOX, or data-protection certification asserted | Not applicable |

Post-design recheck: the design respects the existing layer boundaries. Security
approval and real execution of quality gates remain release prerequisites, not
claims satisfied by planning.

## Project Structure

### Documentation (this feature)

```text
specs/002-author-support/
  spec.md
  plan.md
  research.md
  data-model.md
  quickstart.md
  contracts/http-api.md
  checklists/requirements.md
```

The subsequent [implementation task list](tasks.md) records delivery completion.
The root [glossary](../../GLOSSARY.md) records the agreed catalogue vocabulary.
Spec Kit's ignored `.specify/feature.json` selects this feature locally.

### Planned source changes

| Surface | Planned files and responsibility |
|---|---|
| Core Author | `src/Bookstore.Core/Authors/Author.cs`, `AuthorContracts.cs`, `AuthorValidators.cs`, `AuthorService.cs`, `IAuthorRepository.cs`; immutable entity behavior, request validation, orchestration, narrow persistence contract |
| Core coordination | `src/Bookstore.Core/Books/IAuthorBookCoordinator.cs`; transaction-neutral operation/results for linked creation |
| Existing Book | `Book.cs`, `BookContracts.cs`, `BookService.cs`, `IBookRepository.cs`; optional ID, input exclusivity, query conjunction, backward-compatible mapping |
| Infrastructure | `MongoAuthorRepository.cs`, `MongoAuthorBookCoordinator.cs`, existing `MongoBookRepository.cs`; BSON maps, indexed queries, transactional writes, explicit missing/inactive/conflict outcomes |
| Composition | `src/Bookstore.Infrastructure/DependencyInjection.cs`, `src/Bookstore.Api/Program.cs`; scoped services/repositories/coordinator; reuse singleton client/database and TimeProvider |
| HTTP | `src/Bookstore.Api/Authors/AuthorEndpoints.cs`, existing Book endpoints and error handler; statuses, Problem Details, logging, stable safe conflict mapping |
| OpenAPI | Existing `BookSchemaTransformer.cs` plus Author schema handling; correct required/optional fields, date-only format, Location metadata for CreateAuthor, trace headers, proposed version 1.2.0 |
| Deployment | `docker-compose.yml`, replica-set bootstrap/health scripts, `mongo-init/03-index-authors.js`; persistent initialization, primary readiness, explicit upgrade runbook |
| Reviewed mapping | `scripts/Link-LegacyAuthors.ps1` and `scripts/link-legacy-authors.js`; PowerShell invokes a versioned mongosh maintenance script through the existing MongoDB container, with dry-run/approved manifest, optimistic checks, resumable per-book changes |
| Tests | New `Authors/AuthorTests.cs`, `AuthorValidatorsTests.cs`, `AuthorServiceTests.cs`, `Http/AuthorEndpointsTests.cs`, `Persistence/MongoAuthorRepositoryTests.cs`, `AuthorBookConcurrencyTests.cs`, `AuthorWorkflowTests.cs`; extend existing Book tests/fixtures |
| Public docs | `docs/openapi.yaml`, functional and technical guides, README, feature release specification and QA acceptance checklist |
| CI | `.github/workflows/dotnet.yml`; retain full coverage execution and add the formatter gate; replica-set fixture self-initializes and disposes its resources |

**Structure Decision**: Reuse the existing three projects and one test project.
Do not introduce another application layer assembly, generic repository, CQRS
framework, message broker, or ORM.

## Detailed Design

### Domain and application responsibilities

Author has stable identity, profile, active status, and timestamps. Its creation
and deactivation functions enforce lifecycle rules; deactivating an inactive
Author leaves `updatedAt` unchanged. Use injectable TimeProvider and immutable
results, not public setters. Request validators enforce shape/format rules.

Book retains ownership of inventory; retirement never changes book activity.
Linking is an explicit cross-aggregate operation with a transactional consistency
requirement. Core consumes narrow coordinator outcomes such as created, Author
missing, Author inactive, or association conflict. It must not interpret
MongoDB exceptions, client sessions, or BSON.

No external domain events will be published in this iteration: there are no
subscribers or integrations requiring them. Record successful Author creation/
deactivation as structured logs containing IDs and trace IDs only. Do not add a
bus or describe these logs as a durable audit trail. Logs must be emitted after
successful commit, outside retryable callbacks.

### Strict linking/retirement consistency

1. Validate request and create stable identifiers/times before the retryable work.
2. Start a driver session and use `WithTransactionAsync` with primary reads,
   snapshot read concern, majority write concern, and propagated cancellation.
3. Linked creation conditionally writes the Author with `_id` and `active:true`,
   incrementing an internal `linkRevision`. This must change the document:
   setting an unchanged value does not establish the required conflict.
4. Obtain the Author name from that guarded operation, construct the book credit,
   and insert the book with its BSON ObjectId `authorId` using the same session.
5. Soft deletion writes that same Author document in its own transaction and sets
   `active:false` and `updatedAt` only on an active-to-inactive transition.
6. Concurrent transactions contend on the Author document. If deletion wins,
   a retry observes inactive and rejects creation; if creation wins, deletion
   follows it and the already-linked book is retained.
7. Use driver-supported transient/commit retries, not a broad custom catch loop.
   Every operation must use the session. Never perform external side effects
   inside the callback. An aborted insertion rolls back the revision write too.

An ordinary read followed by insert, even inside a snapshot transaction, does
not satisfy the invariant. Server-side write conflicts must be exercised in tests.
Internal coordination revisions do not update user-visible profile `updatedAt`.

Unlinked legacy creates keep their existing path. Existing stock updates must
preserve both `authorId` and the author credit.

### Identity, compatibility, and search

Expose a single 24-hex Author `id`, backed by `_id`; do not persist an additional
redundant string ID. Name/email are not unique indexes. New Book `authorId` is
optional and omitted when absent, matching current null-omission behavior.

Linked creation accepts `authorId` instead of free-text `author`; supplying both
is 400. The Author name becomes the initial persisted credit. No Author update
operation is planned, so no renaming synchronization is introduced.

Add `authorId` to collection GET. It is exact ObjectId equality; valid unknown
IDs return `[]`, invalid IDs return 400. AND it with category and/or existing
literal author-text filtering. Soft deletion does not hide linked books or
disable historical ID filtering. `/single` remains ID/exact-title lookup and
must explicitly reject author-based filters rather than silently ignore them.

### Errors, privacy, and observability

Use the existing Problem Details writer, `urn:trace:` instance, trace extension,
and `X-Trace-Id`. New endpoints return safe 400/404/409/415/500 outcomes as specified
in the [contract](contracts/http-api.md). Map only known domain conflicts to 409;
unexpected driver failures remain generic logged 500s.

The existing validation handler puts validation messages into both responses
and warning logs. New validators must not interpolate submitted email, birth
date, or profile values into those messages. Do not log bodies or full Author
objects; avoid identifiers containing profile information.

As explicitly requested, add no authentication middleware. This is not suitable
for unprotected public exposure: require deployment-level restricted access to
all Author endpoints, approved TLS/secrets handling, and operator-approved
retention/backup policies. Soft deletion is not privacy erasure.

### Replica-set deployment and reviewed legacy mapping

Local/CI may use one persistent replica-set member; it provides transaction
semantics, not high availability. Bootstrap must initiate only an uninitialized
set, wait for an elected writable primary, and never reconfigure an existing
set automatically. Advertised hosts must resolve from the API, test runner,
and administrative clients. Support startup on an existing data volume.

Production topology/credentials are operator-owned. Plan backup and restore
verification, maintenance window, FCV/version checks, standalone-to-replica-set
conversion, election/readiness verification, index application, and transaction
probe before rolling out the API. Never delete the existing volume.

Add a nonunique `books.authorId` index; evaluate a compound index only against
measured query plans. The Author `_id` index provides identity uniqueness;
an active-status index is optional pending query-plan evidence.

Reviewed mapping is explicit opt-in maintenance, not API-startup behavior.
The manifest records book ID, expected current credit, target Author ID, and
expected current association. Dry-run validates everything without writes.
Apply each link using the same Author guard and a conditional Book update in
one transaction. Preserve the old displayed credit and unrelated metadata.
Skip already-identical links without changing data; fail visibly on stale or
conflicting entries and record per-entry progress for safe resumption.
Use a maintenance window and a protected manifest; never infer namesake identity.
The maintenance script is not an HTTP endpoint or startup hook. Use the existing
container's mongosh rather than adding a host tool or fourth .NET project.
Integration tests must execute the actual script and verify the same guard,
rollback, cancellation/interruption recovery, and stale-manifest rules.

## Delivery Order and Dependencies

| Order | Work package | Depends on | Completion condition |
|---|---|---|---|
| 1 | Domain/contracts and quality cases | Confirmed scope | Author lifecycle, identity, status, mapping, and concurrency rules agreed |
| 2 | Replica-set runtime and fixture | 1 | Existing-volume bootstrap and real transaction probe pass locally/CI |
| 3 | Author model, validation, persistence, HTTP | 1, 2 | Four operations pass unit/HTTP/real-database tests |
| 4 | Coordinated linking and search | 3 | Optional reference and both deterministic concurrent orderings pass |
| 5 | Reviewed legacy association tool | 4 | Dry-run, rollback-on-failure, stale-input rejection, and resumption pass |
| 6 | Documentation and release evidence | 3, 4, 5 | Generated OpenAPI, guides, release spec, and QA checklist agree |
| 7 | Full release gates and rollout rehearsal | 6 | Coverage/format/container checks and backup/upgrade/rollback rehearsal pass |

## Test and Release Strategy

- Unit: name boundaries 0/1/255/256, whitespace, optional fields/email/date,
  duplicate names, immutable lifecycle, TimeProvider timestamps, repeated delete,
  and cancellation. Example: `Deactivate_AlreadyInactive_PreservesUpdatedAt`.
- HTTP: all statuses and payloads, Location, optional-field omission,
  `includeInactive` parsing, invalid/missing IDs, body/media failures,
  trace consistency, generic faults, and OpenAPI schemas.
- Persistence: BSON date/ObjectId mapping, legacy missing reference, index/query
  shape, same-name separation, exact ID plus category/text conjunction,
  atomic soft deletion, and session propagation.
- Transactions: controlled barriers/sessions, never timing-only sleeps. Verify
  delete-first rejects creation, create-first retains the book, aborted insert
  rolls back Author revision, and retried callbacks do not duplicate books/logs.
  Example: `CreateLinkedAsync_DeletionCommitsFirst_RejectsWithoutBook`.
- Acceptance: create Authors/books through HTTP against the replica-set fixture,
  retire an Author, then verify direct/list/book reads and new-link rejection.
- Regression: retain every existing test and old-client author-text workflow.
  Use the real pinned MongoDB image; do not mock transaction correctness.
- Gates: `.\scripts\Test-Coverage.ps1` (70% total, 85% Core) and
  `dotnet format Bookstore.slnx --verify-no-changes --no-restore --verbosity quiet`.
  Build/run the final non-root image and compare generated OpenAPI YAML with live JSON.

Rollback API code independently of topology: retain the replica set and added
fields/indexes, redeploy the prior image, and disable consumers of new endpoints.
The old mapper ignores `authorId`, and inventory updates set only their existing
fields. It cannot enforce new link invariants; freeze Author writes/mapping
before rollback. Do not automatically downgrade to standalone or reverse
reviewed data links. Reversing links requires a separately approved manifest.

## Complexity Tracking

| Added complexity | Why needed | Simpler alternative rejected because |
|---|---|---|
| Replica-set deployment and cross-aggregate coordinator | Explicit user requirement for strict transactional ordering | Standalone/check-then-insert can commit new links after retirement |
| Internal Author coordination revision | Establishes a real same-document write conflict | Snapshot reads or no-op writes do not prevent stale active-state validation |
| Reviewed association manifest/tool | Preserve identity and make legacy changes auditable operationally | Matching name strings can merge different people and cannot be safely inferred |

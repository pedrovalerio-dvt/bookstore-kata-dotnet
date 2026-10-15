# Release specification: FEAT-007 Author Support

**Date**: 2026-10-15
**Originating requirement**:
[joaopaulomendes-dvt/bookstore-kata-dotnet#1](https://github.com/joaopaulomendes-dvt/bookstore-kata-dotnet/issues/1)
**Baseline**: `74e0899`, author-text search/API 1.1.0
**New API contract**: 1.2.0
**Status**: Implemented locally; automated evidence below.
No release tag, commit, push, upstream issue closure, or production deployment
is performed by this implementation task.

## Scope and confirmed decisions

This release introduces managed Author identities, not merely another text
search. It delivers creation, default-active directory, direct retrieval,
and idempotent soft deletion. Books can optionally reference one Author.
Different Authors may share a name; identities, not names or email, are reused.

The user confirmed additive `authorId` with legacy credit compatibility,
reviewed-only legacy linking, retained profiles/books after retirement,
deployment-only access protection, and strict transactional ordering between
linking and retirement. That last decision requires replica-set MongoDB.
See [specification](spec.md), [plan](plan.md), [data model](data-model.md), and
[HTTP contract](contracts/http-api.md).

## Delivered operations and compatibility

| Operation | Delivered behavior |
|---|---|
| `POST /api/authors` | 201 profile/Location; required nonblank name <=255, optional date/nationality/email, active defaults true |
| `GET /api/authors` | 200 array of active Authors; `includeInactive=true` includes both states |
| `GET /api/authors/{id}` | 200 active/inactive profile; malformed ID 400, absent 404 |
| `DELETE /api/authors/{id}` | Empty 204; repeat preserves updatedAt; absent ID 404 |
| `POST /api/books` with `authorId` | Transactionally snapshot active Author name and persist reference; author text plus ID is 400, unknown Author 404, inactive 409 |
| `GET /api/books?authorId=...` | Exact identity equality, AND existing category/text predicates; unknown valid ID returns [] |

Existing free-text creation, literal case-insensitive author substring search,
categories, Book IDs, and inventory updates retain their behavior.
Null `authorId` is omitted from responses. `/single` still supports exactly
one Book ID or exact title; Author text/identity filters there are rejected.
Inactive Authors do not hide or modify linked books.

Nonblank optional text and names are preserved; empty optional text is invalid.
Birth date is calendar `YYYY-MM-DD`; timestamps are server-owned UTC instants
at BSON millisecond precision. No email/name uniqueness, profile editing,
restore, biography, website, pagination, or co-authorship is introduced.

Clients that previously sent an ignored `authorId` on Book create/list/single
must review it: the parameter/property now activates validation/filtering or
managed linking. Ordinary clients without it require no contract change.
Although the API additions are backward-compatible for normal existing clients,
the database deployment requirement is an operational upgrade.

## Technical work completed

| Tasks | Files/mechanism | Purpose |
|---|---|---|
| T001 | Instructions, checklist, task list, glossary | Preserve agreed language and project standards |
| T002-T003 | Compose, bootstrap/health scripts, shared replica Testcontainers fixture | Real transactional topology, existing-volume support, primary readiness and transaction probe |
| T004-T005 | Core Author record/factory/deactivation, request validation, repository/service and unit tests | Encapsulate lifecycle, safe validation messages, injectable time and cancellation |
| T006-T008 | MongoAuthorRepository, AuthorEndpoints, composition/OpenAPI, HTTP/database tests | Four Author operations, BSON mapping, explicit outcomes and post-commit ID-only logs |
| T009-T010 | Optional Book reference, service/repository contracts, guarded coordinator | Preserve legacy path; push identity conjunction to MongoDB and prevent stale-state linking |
| T011 | PowerShell wrapper, actual mongosh script, mapping integration tests | Protected reviewed manifest, no automatic matching, dry-run, atomic per-entry apply, safe resumption |
| T012-T014 | Linked-book unit/HTTP/workflow tests, command-barrier concurrency tests | Prove identity separation, historical visibility, both write-conflict orderings, rollback/retry safety |
| T015 | Idempotent Author-ID index and CI formatter | Eligible exact-identity index and unchanged coverage thresholds |
| T016 | OpenAPI, guides, operations runbook, release and QA artifacts | Keep external contract, business behavior and operator responsibilities aligned |
| T017 | Full coverage/format/image/runtime/schema checks | Release-quality evidence, not a proxy based on test count |

No package manifest change, alternative delivery framework, extra application
project, event broker, or new third-party account is required.

### Strict consistency implementation

MongoAuthorBookCoordinator allocates a stable Book ID, starts a session, and
uses driver `WithTransactionAsync` with snapshot/primary/majority options.
It actually modifies the active Author by incrementing internal `linkRevision`,
then inserts the Book in the same session. Retirement writes the same Author
document in its transaction, using immutable domain deactivation.

An ordinary transactional read cannot prevent stale-read write skew. The real
guarded write forces contention; transient retries resolve against the committed
state. Book insertion failure rolls back the guard. Guard revisions do not
change profile updatedAt. No callback publishes external events/logs; successful
transition logging happens only after persistence returns.

The deterministic concurrency tests hold actual MongoDB commands at barriers,
observe code-112 write conflicts, and release the winning transaction. They
prove deletion-first rejects the competing link and creation-first preserves
exactly one Book after retirement, without sleep-based ordering.

### Deployment and legacy linking

Compose runs one `rs0` member locally and initializes only an unconfigured set.
It rejects unexpected existing topology, retains the data volume, waits for
primary, seeds only an absent category collection, and reapplies indexes.
One member supplies transaction semantics, not production availability.

Legacy linking is explicit maintenance, never API startup behavior.
The script validates exact manifest shape and expected state, guards an active
Author, and changes only Book.authorId in a transaction. Original credits remain.
Each entry commits independently; failures are visible and partial progress is
resumable. An identical existing link skips without revision changes.

See [operations runbook](../../docs/author-operations.md) for conversion,
protected manifest usage, backup/restore, direct host connections and rollback.

## Automated verification evidence

Run:

```powershell
.\scripts\Test-Coverage.ps1
dotnet format Bookstore.slnx --verify-no-changes --no-restore --verbosity quiet
```

The complete suite passed **231 tests, zero failures, zero skips**. It includes
the original 141 tests plus 90 additional Author/relationship/maintenance cases.
The report is `artifacts/coverage/coverage.cobertura.xml`; CI runs the same gate
and publishes the report. All production assemblies and generated support are
included; no exclusions or threshold reductions were added.

| Coverage scope | Measured line coverage | Required gate |
|---|---|---|
| All production assemblies | 83.52% | >=70% |
| Core | 100% | >=85% |
| Infrastructure | 100% | Included in total |
| API, including generated OpenAPI support | 70.00% | Included in total |

Database tests use one collection-scoped replica set with unique databases.
This avoids launching six MongoDB servers concurrently while retaining real
database/concurrency evidence. Mapping tests execute the actual JS in mongosh,
covering malformed manifests, stale input, rollback, and partial resumption.
Shell-script statements themselves are not .NET Cobertura lines.

The runtime image built and ran as UID 1654. Isolated Compose verification
exercised profiles, namesakes, managed/legacy books, conjunctive filtering,
literal regex characters, retirement/idempotency, retained historical books,
safe trace-correlated failures, and absent Production Scalar UI.
The final locally built image identity was
`sha256:f6cdfb78798704a6e9b243fc0e8c315ab64d5e48cc8a9640b248236aa10c58b7`.
Replacing the API with that final build retained the stored retired Author and
linked Book. This is an uncommitted-source verification image, not a published tag.

The live document declares 1.2.0 and nine operations. Its generated YAML was
validated against the official OpenAPI 3.0 schema and compared structurally
with live JSON, including identity filtering and conflict outcomes.

The exact-identity index was exercised through an indexed explain probe.
A separate owned-volume rehearsal converted standalone MongoDB to the new
replica set, retained a fixed catalogue sentinel unchanged, committed a real
transaction, and reran bootstrap/indexes successfully. An isolated backup/
restore probe confirmed source/restored Book and Author counts agree.
These local probes do not certify a production backup or topology.
The actual PowerShell maintenance wrapper also passed dry-run, apply, preserved
credit, and identical-link resumption against the isolated runtime.

## Rollout, rollback, and approval

1. Obtain deployment access/retention approval for Author email/birth-date data.
2. Freeze writes, record image/volume identity, take and verify an approved backup.
3. Convert to the approved replica topology and require primary/transaction
   readiness. Apply idempotent indexes without deleting the volume.
4. Deploy the API, run target-environment acceptance and performance checks,
   then apply only separately approved legacy mapping if needed.
5. Capture image/commit, traces, data verification, QA sign-off and release approval.

Rollback API code while retaining the replica set and additive data. Freeze
Author writes/mapping and disable new consumers; the old API ignores references
and preserves them on inventory updates. Do not automatically delete profiles,
unlink books, downgrade topology, or overwrite a live database with a backup.

The [QA checklist](checklists/qa.md) intentionally remains unchecked until run
in the target environment. Production rollback rehearsal, access controls,
capacity/latency measurements, retention decisions, and production release
approval remain operator/QA responsibilities.

## Standards and explicit limitations

Aggregate boundaries, consistent vocabulary, SRP/DIP, async/cancellation,
safe Problem Details, BSON compatibility, inline rationale, and project test
naming are implemented. Domain and application coverage exceeds 85%.
There is no external domain-event consumer, financial model, regulated audit
ledger, or supplied identity policy. Consequently no event bus, financial
compliance certification, or aggregate authorization is claimed. The user
explicitly chose deployment-only protection rather than a new identity system.
Soft deletion retains data and is not privacy erasure.

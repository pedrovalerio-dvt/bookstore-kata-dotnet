# .NET Bookstore architecture and migration

## Architecture

```text
HTTP consumer (no frontend source in this repository)
  -> ASP.NET Core .NET 10 / grouped Minimal APIs
     -> BookService / AuthorService + domain lifecycle and validation (Core)
        -> IBookRepository / IAuthorRepository / IAuthorBookCoordinator
           -> BSON repositories and transactional coordinator (Infrastructure)
              -> replica-set MongoDB bookstore.books + bookstore.authors
```

The Java/Jersey/Maven runtime has been replaced by a .NET 10 solution.
`Bookstore.Api` is the composition root and HTTP adapter; `Bookstore.Core`
owns immutable book contracts, validation, category vocabulary, and operations;
`Bookstore.Infrastructure` owns BSON mapping, MongoDB connectivity, and queries.
Core has no ASP.NET or MongoDB dependency. Tests use dependency substitution,
not alternate production persistence behavior.

Book inventory and Author lifecycle have separate aggregate boundaries.
Managed linking introduces cross-document MongoDB transactions with a shared
Author guard to serialize links against retirement. There are no monetary
values, payments, external distributed services, event consumers, or audit/event
store requirements in the current operations. No event bus or unconsumed
domain-event abstraction was added. This is a deliberately small layered
design, not a claim of a complete retail DDD model or regulatory certification.

### Project structure

| Path | Responsibility |
|---|---|
| `Bookstore.slnx` | .NET solution |
| `Directory.Build.props` | .NET 10, nullable, implicit usings, warnings as errors |
| `Directory.Packages.props` | Central package versions for all projects |
| `global.json` | .NET 10 SDK selection and feature-band roll-forward |
| `src/Bookstore.Api/Program.cs` | DI, logging, errors, tracing, OpenAPI, host |
| `src/Bookstore.Api/Books/BookEndpoints.cs` | Five existing business/liveness routes |
| `src/Bookstore.Api/Authors/AuthorEndpoints.cs` | Author creation, directory, lookup, soft deletion; post-commit ID-only logs |
| `src/Bookstore.Core/Authors` | Immutable Author lifecycle, profile contracts/validation, service, repository boundary |
| `src/Bookstore.Core/Books/IAuthorBookCoordinator.cs` | Transaction-neutral linked-creation contract |
| `src/Bookstore.Infrastructure/Persistence/MongoAuthorRepository.cs` | Author BSON persistence and transactional retirement |
| `src/Bookstore.Infrastructure/Persistence/MongoAuthorBookCoordinator.cs` | Guarded active-Author write and Book insert in one transaction |
| `src/Bookstore.Api/Errors/ApiExceptionHandler.cs` | Validation/binding/unexpected exception mapping |
| `src/Bookstore.Core/Books/Book.cs` | Immutable book record |
| `src/Bookstore.Core/Books/BookContracts.cs` | Primary-constructor DTOs and immutable category vocabulary |
| `src/Bookstore.Core/Books/BookValidators.cs` | Input rules and identifier validation |
| `src/Bookstore.Core/Books/BookService.cs` | Validated operation orchestration and timestamp decisions |
| `src/Bookstore.Core/Books/IBookRepository.cs` | Async persistence boundary |
| `src/Bookstore.Infrastructure/Persistence` | Options and BSON repository |
| `src/Bookstore.Infrastructure/DependencyInjection.cs` | MongoDB client/database/repository registration |
| `tests/Bookstore.Tests` | Unit, HTTP, real database, and workflow tests |
| `scripts/Test-Coverage.ps1` | Full test/coverage policy gate |
| `.github/workflows/dotnet.yml` | CI runs the same gate and uploads coverage |
| `mongo-init` | Legacy category seeds and new idempotent book indexes |
| `docs/legacy` | Historical Java documentation from commit `cebc7fb` |
| `specs/001-search-by-author` | Approved feature specification, release task breakdown, and QA checklist |
| `specs/002-author-support` | Author management specification, design, executed tasks, release evidence, and QA plan |
| `scripts/Link-LegacyAuthors.ps1`, `scripts/link-legacy-authors.js` | Protected, reviewed, resumable legacy association maintenance |
| `scripts/mongodb-bootstrap.js`, `scripts/mongodb-health.js` | Local replica initialization and writable-primary health |

Java source and Maven configuration have been removed rather than maintained as
a second runnable stack. Historical source remains in Git at `cebc7fb`.

## HTTP contract

The existing `/api/books` route family and camel-case property names remain.
Trailing-slash variants are accepted by ASP.NET routing.

| Method and route | Successful result | Expected error results |
|---|---|---|
| `GET /api/books?category=...&author=...&authorId=...` (all optional) | 200, array | 400 invalid filters; 500 unexpected failure |
| `GET /api/books/single?id=...` or `?name=...` | 200, Book | 400 invalid/ambiguous/unsupported filter; 404 absent; 500 failure |
| `POST /api/books` | 201, Book and Location | 400 validation/binding; 404 unknown Author; 409 inactive Author; 415 media type; 500 failure |
| `PUT /api/books` | 200, complete Book | 400 validation/binding; 404 absent; 415 media type; 500 failure |
| `GET /api/books/health` | Empty 200 | Liveness only |
| `POST /api/authors` | 201, Author and Location | 400 validation/binding; 415 media type; 500 failure |
| `GET /api/authors?includeInactive=...` | 200, array | 400 invalid switch; 500 failure |
| `GET /api/authors/{id}` | 200, active/inactive Author | 400 malformed ID; 404 absent; 500 failure |
| `DELETE /api/authors/{id}` | Empty 204, including repeated retirement | 400 malformed ID; 404 absent; 500 failure |

Collection author search uses a case-insensitive literal substring. The
single-book route remains ID/title-only. There is no Book DELETE route. Routing failures such as
404 and 405 also use status-code Problem Details. Framework and proxy errors
outside the application, such as a disconnected socket, are not a JSON contract.
No upstream HTTP service exists; MongoDB uses its own wire protocol.

Live OpenAPI is available at `/openapi/v1.json`; Scalar at `/scalar/v1` is mapped
only in Development. The [YAML snapshot](openapi.yaml) is generated from the
running .NET metadata. The [functional guide](functional-guide.md) lists all
intentional legacy behavior changes.

DTO property annotations enrich OpenAPI; FluentValidation is the authoritative
runtime validation path. Query rules and cross-field constraints are described
in operation descriptions and covered by tests.

## Optimizations and implementation details

### Connection lifetime and asynchronous I/O

A single `IMongoClient` is registered as a DI-owned singleton and disposed with
the application. `IMongoDatabase` is shared, and repositories, services, and coordinator
are scoped. This replaces a new synchronous client for every operation and
normal-path-only manual disposal. Driver calls are awaited and receive the
request CancellationToken. No blocking `.Result`, `.Wait`, or `async void` is
used.

### Query pushdown and indexing

Category list filtering becomes a MongoDB equality predicate. ID lookup parses
an ObjectId and uses the `_id` index. Title lookup sends a title equality
predicate and requests one result instead of loading the entire collection.
No query strings are assembled from user input.

The index script creates nonunique `title` and `categoryId` indexes; duplicate
titles remain allowed. Listing still returns an unpaginated array to preserve
the chosen contract, so unfiltered listing remains O(N) materialization. No
unmeasured claim of unlimited catalogue scalability is made.

### Author substring search

The list endpoint binds optional `author` text and passes it, category, and
the request CancellationToken to `BookService.ListAsync`. Core rejects
non-null empty/whitespace-only author input with the existing ValidationException
path (400 Problem Details). Nonblank text is not trimmed or case-normalized.

`IBookRepository.ListAsync(categoryId, author, cancellationToken)` expresses the
two optional predicates. The MongoDB implementation adds a BSON regex on
`author` with option `i`, escaping input through `Regex.Escape` before building
`BsonRegularExpression`. The pattern is unanchored so literal text can match
anywhere in a stored author. The category equality filter and author regex are
combined with AND and executed in one async MongoDB find.

Escaping is required for correctness as well as safe handling of user text:
input such as `.*`, brackets, `+`, or backslashes must not become a pattern
operator. Matching uses MongoDB's regex case behavior, not collection collation,
accent folding, culture-specific transliteration, or fuzzy matching. Null/missing
authors do not satisfy the regex; duplicate names and inactive books remain
eligible. Result shape, unspecified ordering, and empty-array semantics are unchanged.

This unanchored case-insensitive substring search is not an index-backed
prefix/exact lookup. No author B-tree index is added with an unsupported
performance claim. MongoDB can use the existing category index when applicable,
but author-only search can scan the collection. There is no new latency SLA,
pagination, search service, dependency, stored field, or data migration.
Existing cancellation and exception/logging behavior remains in force.

The [approved feature](../specs/001-search-by-author/spec.md),
[release specification](../specs/001-search-by-author/release.md), and
[QA checklist](../specs/001-search-by-author/checklists/qa.md) record scope,
implementation tasks, automated evidence, rollout, and manual acceptance.

### Managed Author identity and transactional lifecycle

`Author` is immutable, owns creation/deactivation rules, and uses server-provided
time. Name is not a unique key. JSON uses one string `id` backed by BSON `_id`;
birth date maps DateOnly to UTC-midnight BSON date and `YYYY-MM-DD` JSON.
`createdAt`/`updatedAt` are UTC instants with BSON millisecond precision.
Optional profile fields are omitted when null. The internal Int64 `linkRevision`
never appears in API responses.

`AuthorService` validates IDs and delegates persistence through `IAuthorRepository`.
`BookService` validates mutually exclusive `author`/`authorId`, then uses
`IAuthorBookCoordinator` for managed links. `ListByAuthorIdAsync` adds ObjectId
equality to category/escaped-text predicates. All supplied filters combine with
AND. Unlinked legacy reads and stock updates retain their previous behavior.
The additive `authorId` index serves exact identity queries; no name/email
uniqueness or automatic profile creation is introduced.

Linked creation starts a driver session and runs `WithTransactionAsync` using
snapshot reads, primary read preference, majority writes, and cancellation.
It conditionally increments `linkRevision` on `{_id, active:true}`, resolves the
name, then inserts the Book in the same transaction. The Book ID is allocated
outside the callback so transient retries do not create extra identities.
Retirement reads the profile, applies domain deactivation, and writes that
same Author document inside its transaction. MongoDB write conflicts force
the losing transaction to retry against the committed state. A read-only
transaction or no-op guard would not prevent stale-read write skew.

If retirement commits first, linking returns 409; if linking commits first,
retirement preserves that Book. A missing Author returns 404. Failed inserts
roll back the guard revision. Repeated retirement preserves updatedAt.
Guard writes do not change profile timestamps. No operations run in parallel
on a session and no external side effects run in retryable callbacks.

Successful creation/deactivation logs contain Author IDs, not profile objects;
they are emitted after persistence commits. Safe validation messages avoid
interpolating email or profile values into response details or warning logs.
These logs are not a durable audit ledger and no domain-event bus is claimed.
Deployment must protect all Author endpoints; soft deletion is not erasure.

Reviewed linking runs through a protected mongosh maintenance script, not a
public endpoint or startup hook. Each manifest entry validates the expected
credit and missing association, guards the active Author, and conditionally
updates the Book in one transaction. Original credits and unrelated metadata
remain unchanged. Dry-run does not write; partial runs are resumable and
already-identical links skip. See the [operations runbook](author-operations.md).

### Atomic book updates and accurate outcomes

`FindOneAndUpdateAsync` modifies quantity, active, and inactiveDate and returns
`ReturnDocument.After`. It does not set immutable `_id`, does not upsert, and
returns null when no record matches. Consequently the endpoint returns real
404 outcomes and complete persisted metadata on success.

Quantity and active are nullable in the update DTO only to distinguish omitted
input from valid zero/false. Both are required by FluentValidation before any
database access. Server time is supplied via injectable `TimeProvider`, making
deactivation tests deterministic. Updates are atomic at one document, but
there is no optimistic concurrency token; absolute writes remain last-write-wins.

### Validation and category authority

Title must be nonblank, new quantity must be nonnegative, IDs must be 24 hex
characters, and categories must be exact names. Lookup requires one supported
filter, not ambiguous precedence. Author input on `/single` remains an explicit
validation error directing consumers to the collection search. Blank collection
author input is also a validation error. Category definitions remain a read-only list in Core; the
legacy categories collection is not consulted.

No new maximum text lengths, unique-title requirement, publication chronology,
or language-code vocabulary was invented. Existing malformed historical data
is not silently repaired. Unexpected BSON type errors propagate to the
generic error path and are logged.

## Data compatibility

The repository reads and writes the existing `bookstore.books` collection.
`_id` remains BSON ObjectId; field names remain `title`, `author`, `categoryId`,
`quantity`, `description`, `language`, `active`, `inactiveDate`, `publisher`,
and `publisherDate`. Dates remain BSON dates; no JSON strings are written into
date fields. Responses use ISO 8601 date-times; storage normalizes to UTC with
millisecond precision.

Missing/null legacy category and quantity map to 0; missing/null active maps
to false. Optional string/date fields map to null and are omitted from JSON
responses. Nullable legacy metadata is accepted on reads even though new
creation validates title. Invalid BSON types fail explicitly rather than
yielding success-shaped defaults.

The category seed remains for compatibility but does not affect category rules.
No bulk data migration or destructive collection rewrite is performed.

## Configuration and deployment

| Configuration | Default/local value |
|---|---|
| `Mongo:ConnectionString` / `Mongo__ConnectionString` | `mongodb://localhost:27017` locally; `mongodb://mongodb:27017` in Compose |
| `Mongo:DatabaseName` / `Mongo__DatabaseName` | `bookstore` |
| `ASPNETCORE_HTTP_PORTS` | `8080` in Compose |
| `ASPNETCORE_ENVIRONMENT` | Production unless explicitly configured |

Options are bound and validated at startup, including MongoDB URI syntax.
Server selection has a bounded five-second timeout. Database availability
itself is not required to serve liveness or OpenAPI.

The Dockerfile restores centrally managed dependencies, publishes the .NET
application in a build image, and runs it as the .NET image's non-root user.
Compose uses MongoDB `7.0.28-jammy` as a single-member `rs0` replica set, waits
for writable-primary health and successful bootstrap, preserves the named
data volume, and binds host ports to loopback. The pinned image is
also used by Testcontainers. Update the pin through normal dependency
maintenance rather than trusting an untracked mutable local image.

The bootstrap service initializes only an uninitialized replica set and
rejects an unexpected existing topology instead of reconfiguring it. It seeds
categories only when that collection is absent, then applies idempotent indexes
on both fresh and existing volumes. To reapply the indexes manually:

```powershell
docker compose run --rm mongodb-bootstrap
```

The command runs the mounted bootstrap and index scripts.
The old `/docker-entrypoint-initdb.d` mount is no longer
used. Inspect conflicting indexes instead of dropping them automatically.
See [Author operations](author-operations.md) for standalone conversion,
backup/restore, host URI, and rollback instructions. A one-node replica set
supports transactions but is not a production high-availability topology.

This Compose file is for local development. It does not configure production
MongoDB credentials, TLS, ingress, backups, or an identity provider. Use managed
secrets/environment configuration for credentials and an approved production
deployment policy; do not commit connection-string secrets.

## Error handling, logging, and security boundary

Problem Details includes type, title, status, detail, a `urn:trace:` instance,
and traceId. Validation and malformed bodies map to 400, unmatched books to
404, unsupported media types to 415, and unexpected exceptions to generic 500
responses. Unexpected exception details are logged server-side, never inserted
into response detail. The registered Problem Details writer always emits
`application/problem+json`, including when Accept excludes JSON; an error must
not turn into another failure because its error representation was excluded.

Serilog writes compact JSON. Request logs and application error logs share
TraceId through LogContext; `X-Trace-Id` is assigned at response start so the
exception handler cannot erase it when clearing a response. ASP.NET Activity
provides standard trace correlation. There is no outbound HTTP integration to
which headers must be forwarded. MongoDB calls are cancellable driver calls,
not HTTP requests.

No authentication or authorization mechanism has been added without a supplied
identity/access policy. The local loopback port restrictions reduce accidental
exposure but are not authorization. There are no financial amounts or payments,
so PCI-DSS/SOX compliance and financial audit guarantees are not asserted.
No immutable business audit log is implemented.

## Exact test classes and coverage policy

| Test class | Evidence provided |
|---|---|
| `Books/BookValidatorsTests.cs` | Missing/blank titles, quantity boundaries, every category, bad IDs, omitted update values, ambiguous/unsupported lookup |
| `Books/BookServiceTests.cs` | Mocked repository isolation, query pushdown, literal author forwarding, blank-author rejection, category intersection, creation, timestamps, updates, misses, failure propagation, cancellation |
| `Http/BookEndpointsTests.cs` | WebApplicationFactory real HTTP pipeline, author-query binding/encoding, multiple/empty search results, defaults, routes, statuses, Location, payloads, JSON/media errors, trace headers, redaction, OpenAPI, UI environment gate |
| `Persistence/MongoConfigurationTests.cs` | URI rejection, options binding, singleton client and scoped repository lifetimes |
| `Persistence/MongoBookRepositoryTests.cs` | Real MongoDB literal author substrings, case/accent behavior, AND filtering, punctuation escaping, authorless records, ObjectId equality, BSON dates, metadata preservation, no upsert, mapping errors, cancellation |
| `Persistence/BookWorkflowTests.cs` | HTTP-to-real-MongoDB create/find/list/update workflow and multi-book author search with category intersection |

`Http/BookstoreFactory.cs`, `Persistence/MongoFixture.cs`, and `TestBook.cs`
provide fixtures, dependency substitution, and representative test data.
One xUnit collection fixture owns a replica-set container and disposes it; tests use
uniquely named disposable databases, not the configured local bookstore.
Test names follow `MethodName_Condition_ExpectedResult`.

New Author test classes cover lifecycle/validation, service forwarding,
HTTP contracts/errors, BSON persistence, managed identity filtering, the full
HTTP workflow, and actual maintenance-script execution.
`AuthorBookConcurrencyTests` holds a real transaction with command-event barriers,
waits for MongoDB write conflict code 112, and then releases the winning commit.
Both orderings and guarded-write rollback are exercised without sleep-based races.
Mapping tests cover dry-run, literal legacy credit preservation, validation,
inactive/missing targets, write rollback, and partial-run resumption.

The Java baseline had one mocked create-controller test and no demonstrated
coverage threshold. The .NET tests include success, invalid input, not-found,
database, and unexpected-failure paths. `dotnet test` instruments every
production assembly and fails below 70% total line coverage. The full script
additionally requires 85% for `Bookstore.Core`, exceeding the company minimum
for business logic and satisfying the stricter repository guideline.

Coverage includes compiler/OpenAPI-generated code; no production assembly or
handwritten file is excluded to inflate the percentage. This can make API
assembly coverage lower than authored-source coverage. Read the Cobertura
report, not an assumption based on test count.

The migration verification run passed **110 tests, with no skipped tests**:

These are historical migration figures for `bdfddf6`. Author search results are
recorded in its release specification; current Author management results are in
the [Author Support release](../specs/002-author-support/release.md).

| Coverage scope | Measured line coverage |
|---|---|
| All production assemblies, including generated code | 77.16% |
| Core business logic | 100% |
| Infrastructure | 100% |
| API assembly, including generated OpenAPI support | 66.58% |
| Authored API source, inspected separately in the same report | 177/177 lines |

The lower API assembly figure is generated-code coverage, not an excluded
handwritten error or endpoint implementation. Policy gates use the unfiltered
77.16% total and 100% Core figures, not the separately inspected source-only
figure. The runtime image was also built and exercised with an isolated
MongoDB container, including index initialization/idempotency, create/find/update,
Problem Details trace correlation, non-root execution, and production UI gating.

Run `.\scripts\Test-Coverage.ps1` from the root. The same command runs in CI
with Docker available. A filtered non-container run is useful feedback but is
not accepted as the full policy gate.

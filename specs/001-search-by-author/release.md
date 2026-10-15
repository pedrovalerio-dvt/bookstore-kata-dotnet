# Release specification: Search by Author

**Date**: 2026-10-15
**API contract version**: 1.1.0
**Baseline**: `bdfddf6` (.NET migration)
**Release state**: Implemented; automated verification recorded below.
Manual environment acceptance and deployment approval remain the QA/release team's responsibility.

## Purpose and agreed scope

Users can retrieve every book whose author contains a supplied fragment,
regardless of capitalization. The Product Owner selected collection search
with optional category intersection, not first-result author lookup.

The [approved functional specification](spec.md) follows the installed Spec Kit
template and passed its [requirements checklist](checklists/requirements.md).
The [QA checklist](checklists/qa.md) is the release acceptance checklist, not a
claim that manual staging tests have already been performed.

## Delivered contract

```text
GET /api/books?author=beck
GET /api/books?author=beck&category=Programming
```

| Concern | Release behavior |
|---|---|
| Result | 200 with the existing array of Book records, including all matches |
| Author matching | Case-insensitive literal substring, anywhere in stored author text |
| Composition | Author AND category; category remains exact/case-sensitive |
| Missing author query | Previous unfiltered/category-only listing |
| Empty or whitespace-only author | 400 Problem Details with trace correlation |
| Nonblank outer/internal whitespace | Preserved as literal characters; no trimming |
| No matches | 200 with `[]` |
| Null/missing stored author | Excluded from author searches, retained in ordinary listings |
| Inactive/duplicate-author records | Included when the predicates match |
| Symbols | Literal, including regex metacharacters; not an expression language |
| Single-book route | Still rejects author and requires exactly one ID/title filter |
| Error propagation | Existing generic 500 response; internal diagnostics logged only server-side |

Examples use URL-encoded input where needed. `C++ (Jr.)` is sent as
`C%2B%2B%20%28Jr.%29`. Query parsing decodes once; an unencoded `+` is a space.

This is an additive collection query parameter. Response schemas, existing
successful status codes, stored fields, and book IDs are unchanged.
Requests previously containing an ignored collection `author` parameter now
activate filtering or blank-input validation. Consumers that sent such an
unused parameter must remove it to preserve their old unfiltered result set.

## Technical tasks completed

| Task | Files and implementation |
|---|---|
| T01: Confirm and specify behavior | `spec.md` and requirements checklist; collection search, case-insensitive partial matching, AND category composition. Spec Kit also records local, ignored state in `.specify/feature.json`. |
| T02: Extend the HTTP adapter | `src/Bookstore.Api/Books/BookEndpoints.cs`; optional author query binding, query descriptions, list metadata, and `/single` guidance |
| T03: Enforce list-input rules | `src/Bookstore.Core/Books/BookService.cs`; reject supplied blank authors before database I/O and forward nonblank text unchanged |
| T04: Extend the persistence contract | `src/Bookstore.Core/Books/IBookRepository.cs`; nullable author fragment with cancellation and XML contract documentation |
| T05: Query author text safely | `src/Bookstore.Infrastructure/Persistence/MongoBookRepository.cs`; escaped BSON regex with `i`, combined with existing category equality in one async find |
| T06: Preserve single-book scope | `src/Bookstore.Core/Books/BookValidators.cs`; retain rejection and improve guidance to collection search |
| T07: Explain new logic inline | XML comments on the changed service/repository contracts and HTTP handler; inline rationale for escaping user input |
| T08: Add isolated unit tests | `BookServiceTests`; raw text and CancellationToken forwarding, empty results, blank input, invalid category combination, cancellation propagation |
| T09: Add HTTP contract tests | `BookEndpointsTests`; author binding, URL encoding, multiple/empty results, bad input, safe errors, OpenAPI author parameter and version |
| T10: Verify real database behavior | `MongoBookRepositoryTests`; substrings/case, literal punctuation, whitespace, Unicode case versus accents, null/missing authors, inactive records, category intersection, cancellation |
| T11: Verify the full workflow | `BookWorkflowTests`; create representative data through HTTP, then author-only/combined/literal searches against Testcontainers MongoDB |
| T12: Publish documentation | Current functional and technical guides, README release links, API 1.1.0 live metadata, and generated OpenAPI v3 YAML snapshot |
| T13: Verify quality gates | Full coverage script, format/build checks, schema validation, and isolated runtime verification; evidence below |

## Implementation and optimization details

No new package, service registration, author entity, write path, or storage
schema was introduced. The existing async list operation now carries author
text through the layers. Core remains independent of MongoDB.

The repository builds a category equality predicate when supplied, then ANDs
an `author` regex with the escaped literal fragment and `i` option. Matching is
performed by MongoDB, not by loading all books and applying an in-memory
predicate. `Regex.Escape` is essential: client input must not add wildcards,
anchors, groups, or regex operators.

There is no new B-tree author index: unanchored case-insensitive substring
search cannot be presented as an efficient index-prefix lookup. Author-only
queries can scan the catalogue; category predicates may benefit from the
existing category index. Response size is still unbounded because pagination
was not in scope. No latency/throughput SLA is asserted.

DI lifetimes, one shared MongoClient, cancellation propagation, trace-correlated
logging, safe Problem Details, and all existing write validation are retained.
The operation is read-only, so it does not publish domain events or alter the
Book consistency boundary. It introduces no financial or personal-profile model
and makes no new authentication/compliance guarantees.

## Automated verification evidence

Execute the same full quality gate used by CI:

```powershell
.\scripts\Test-Coverage.ps1
dotnet format Bookstore.slnx --verify-no-changes --no-restore --verbosity quiet
```

The feature run passed **141 tests, zero failures, zero skips**, compared with
110 baseline tests. Of these, 31 new cases cover author search and regressions.
The suite includes Moq unit tests, WebApplicationFactory HTTP tests, and
real MongoDB `7.0.28-jammy` Testcontainers tests.

| Coverage measure | Result | Gate |
|---|---|---|
| All production assemblies, including generated code | 77.39% line coverage | At least 70% |
| `Bookstore.Core` | 100% line coverage | At least 85% |
| `Bookstore.Infrastructure` | 100% line coverage | Included in total |
| `Bookstore.Api`, including generated OpenAPI support | 66.66% line coverage | Included in total |

No production assembly, file, or generated code was newly excluded. The
Cobertura report is `artifacts/coverage/coverage.cobertura.xml`; CI uploads it.
The API figure includes framework-generated OpenAPI support, as documented in
the architecture guide.

The OpenAPI YAML is generated from `/openapi/v1.json`, not independently
handwritten. Verification compares parsed YAML with the live JSON document,
checks author is an optional string query parameter, and validates against the
official OpenAPI 3.0 schema. The document still contains five business/liveness
operations and declares contract version 1.1.0.

The runtime verification uses isolated API/MongoDB containers and temporary
test data, checks author-only and category intersection, verifies literal
symbols and blank-input Problem Details, and cleans its own containers/network.
No existing bookstore database or unrelated container is modified.
The release image built successfully and ran as non-root user `1654`.
Runtime checks also confirmed inactive matches, encoded plus signs, empty
results, trace/header correlation, and unchanged unfiltered and `/single` routes.

## Deployment and rollback

1. Run the coverage gate and format check with .NET 10 and Docker available.
2. Build/deploy the API through the existing container pipeline. For the local
   Compose environment, use `docker compose up --build -d api`.
3. Verify `/api/books/health`, `/openapi/v1.json` version 1.1.0, and the author
   query on known release-test catalogue data.
4. Run the environment acceptance checklist and record evidence before release approval.

There are no new environment variables, secrets, migrations, indexes, or
seed-data requirements. Preserve the MongoDB data volume. Production ingress,
TLS, identity policies, and credentials remain deployment responsibilities.

Rollback consists of redeploying the prior API image corresponding to
`bdfddf6`. No database rollback is needed because search changes no stored data.
Consumers relying on author filtering must disable/remove the parameter after
rollback; the previous collection operation ignored it and returned a broader
result set.

## Remaining limitations and release approval

No pagination, author editing, search ranking, author-name normalization, fuzzy
matching, accent folding, frontend, or identity integration is included.
Search does not guarantee ordering, transliteration, a cultural sort rule, or
performance at an unmeasured catalogue size.

Manual QA items are intentionally unchecked until executed in the target
environment. QA must record build/image identity, environment, test-data IDs,
response/trace evidence, acceptance decision, and any defects. No release tag,
commit, push, or production deployment is performed by this feature task.

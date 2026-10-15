# Tasks: FEAT-007 Author Support

Input: [plan](plan.md), [spec](spec.md), [model](data-model.md),
[contract](contracts/http-api.md), [research](research.md).

## Setup

- [X] T001 Inspect instructions, ignore files, and requirements checklist in `.github/instructions/`, `.gitignore`, `.dockerignore`, and `checklists/requirements.md`.

## Foundation

- [X] T002 Configure elected-primary replica-set startup in `docker-compose.yml` and `scripts/`; preserve existing volumes.
- [X] T003 Configure real transactional MongoDB fixture in `tests/Bookstore.Tests/Persistence/MongoFixture.cs`.

## US1: Register and discover Authors

Independent proof: create and retrieve distinct same-name Authors.

- [X] T004 [US1] Add lifecycle/validation/service tests in `tests/Bookstore.Tests/Authors/`; name is "Required, nonblank, maximum 255 UTF-16 code units"; optional text is nonblank and email validated.
- [X] T005 [US1] Implement immutable lifecycle, contracts, validators, service, and repository interface in `src/Bookstore.Core/Authors/`; timestamps server-owned, default active true, duplicate names allowed.
- [X] T006 [US1] Implement BSON persistence in `src/Bookstore.Infrastructure/Persistence/MongoAuthorRepository.cs`; ObjectId ID, optional UTC-midnight birth date, millisecond timestamps, internal revision.
- [X] T007 [US1] Wire HTTP, DI, safe errors and OpenAPI in `src/Bookstore.Api/Authors/`, `Program.cs`, and `src/Bookstore.Infrastructure/DependencyInjection.cs`.
- [X] T008 [US1] Add HTTP and database tests in `tests/Bookstore.Tests/Http/AuthorEndpointsTests.cs` and `Persistence/MongoAuthorRepositoryTests.cs`.

## US2: Associate books with Authors

Independent proof: exact identity separates namesakes and legacy creates remain valid.

- [X] T009 [US2] Add optional `authorId`, input exclusivity and AND filtering in `src/Bookstore.Core/Books/`, `src/Bookstore.Api/Books/`, and `src/Bookstore.Infrastructure/Persistence/MongoBookRepository.cs`.
- [X] T010 [US2] Implement guarded transactional linking in `src/Bookstore.Infrastructure/Persistence/MongoAuthorBookCoordinator.cs` with narrow Core contract.
- [X] T011 [P] [US2] Add reviewed dry-run/resumable mapping in `scripts/Link-LegacyAuthors.ps1`, `scripts/link-legacy-authors.js`, and integration tests; preserve original credits.
- [X] T012 [US2] Add linked-book unit, HTTP and real workflow tests in `tests/Bookstore.Tests/Books/`, `Http/`, and `Persistence/`.

## US3: Retire Authors

Independent proof: retirement hides directory entries but retains profiles/books and prevents new links.

- [X] T013 [US3] Implement transactional idempotent retirement in `src/Bookstore.Infrastructure/Persistence/MongoAuthorRepository.cs` and HTTP DELETE in `src/Bookstore.Api/Authors/AuthorEndpoints.cs`.
- [X] T014 [US3] Add deterministic concurrency/rollback tests in `tests/Bookstore.Tests/Persistence/AuthorBookConcurrencyTests.cs`; exercise both transaction orderings.

## Release

- [X] T015 Add indexes and CI formatting gate in `mongo-init/03-index-authors.js` and `.github/workflows/dotnet.yml`.
- [X] T016 Update `docs/functional-guide.md`, `docs/technical-architecture.md`, `README.md`, `specs/002-author-support/release.md`, and QA acceptance plan.
- [X] T017 Run `scripts/Test-Coverage.ps1`, enforce 70% total/85% Core without exclusions, format, build/run container, and regenerate/validate `docs/openapi.yaml`.

## Dependencies and implementation strategy

T001 precedes all work; T002/T003 establish transaction capability before
integration checks. T004 precedes T005; T005 precedes T006/T007/T008.
US2 depends on US1; US3 uses the same Author guarded-write boundary as US2.
The mapping script can be developed independently of C# after the contract is
fixed. Unit tests and deployment/mapping work are parallel opportunities on
different files. Complete Author registration as the first increment, then
linked creation, retirement, migration tooling, and final release evidence.
No commit or push is implied.

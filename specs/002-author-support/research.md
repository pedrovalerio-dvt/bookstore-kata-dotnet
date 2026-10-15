# Research and Decisions: Author Support

**Date**: 2026-10-15
**Scope**: Source-backed planning; no implementation or test execution.

## 1. Existing architecture

**Decision**: Extend the three current projects and existing test project.

**Rationale**: Core already defines immutable books, requests, validators,
services, and repository interfaces. Infrastructure owns BSON persistence and
singleton MongoClient composition; API owns routes and Problem Details.

**Evidence**: `src/Bookstore.Core/Books/Book.cs`, `BookContracts.cs`,
`BookService.cs`; `src/Bookstore.Infrastructure/DependencyInjection.cs`;
`src/Bookstore.Api/Books/BookEndpoints.cs`, `Errors/ApiExceptionHandler.cs`,
and `OpenApi/BookSchemaTransformer.cs`.

**Alternatives considered**: Another service, CQRS framework, or application
assembly adds operational/structural cost without a requirement.

## 2. Author identity and legacy data

**Decision**: Add optional ObjectId `authorId`; retain author credit and literal
substring search. Permit namesakes; no automatic Author creation or backfill.

**Rationale**: Explicitly selected by the user. Existing code stores `author` as
free text, and current updates cannot edit author metadata.

**Alternatives considered**: Required links break old clients; normalized unique
names incorrectly merge people; email uniqueness is not in the issue.

## 3. Retirement and visibility

**Decision**: Hide inactive Authors only from default Author listing; retain
direct retrieval and linked-book visibility. Reject new links, not old books.

**Rationale**: Explicitly selected by the user; Author and Book activity represent
different lifecycles. Repeated retirement should preserve historical timestamps.

**Alternatives considered**: Cascading book hiding/deletion changes catalogue
behavior; blocking retirement on all existing links was not selected.

## 4. Strict concurrency and database topology

**Decision**: Convert local/CI infrastructure to replica-set MongoDB, with an
operator-approved production conversion. Coordinate linking and retirement by
writing the same Author document inside transactions.

**Rationale**: The user explicitly rejected admission-time-only validation.
The current Compose configuration and MongoFixture start standalone MongoDB.
MongoDB transactions require replica-set/sharded topology. Snapshot reads can
be stale; a conditional `findOneAndUpdate` that actually modifies the Author
establishes the required write conflict.

**Alternatives considered**: A read-only transaction does not prevent write skew;
an in-process lock fails with multiple API instances; a distributed lock adds
lease/recovery complexity; preserving standalone cannot satisfy the chosen rule.

**Verified sources**:

- [MongoDB transaction production considerations](https://www.mongodb.com/docs/manual/core/transactions-production-consideration/):
  standalone restriction, stale reads, and document-modifying guarded reads.
- [C# driver transaction API](https://www.mongodb.com/docs/drivers/csharp/current/crud/transactions/):
  `WithTransactionAsync`, callback signature, cancellation, and exception handling.
- [Testcontainers MongoDB module](https://dotnet.testcontainers.org/modules/mongodb/):
  standalone is default; replica-set configuration is supported in documented modules.

Use the pinned Testcontainers version's supported builder API after confirming
its exact signature during implementation. The plan does not assume an
unverified method name or require an upgrade. If the pinned module cannot
initialize a set itself, use explicit container command/bootstrap and readiness
checks through existing Testcontainers facilities.

## 5. Lifecycle, timestamps, and events

**Decision**: Keep Author lifecycle in immutable domain behavior; use TimeProvider.
No external domain-event publishing or broker is introduced.

**Rationale**: There are no consumers, durable audit requirements, or existing
event infrastructure. Only committed transition logs are necessary here.
Retry callbacks cannot emit externally visible effects safely.

**Alternatives considered**: An outbox/bus would be justified for durable
downstream delivery, not for a hypothetical integration. Structured logs are
not a substitute for a regulated audit ledger.

## 6. Access and profile information

**Decision**: Preserve existing application access behavior as explicitly selected;
require deployment-only protection and release approval for profile exposure.

**Rationale**: Existing API has no identity provider or policy. Birth date/email
are profile data and must not enter warning logs or exception details.
Soft deletion does not erase retained data or backups.

**Alternatives considered**: Inventing an authentication provider exceeds the
agreed scope; omitting email was offered but not selected.

## 7. Proposed contract defaults

**Decision**: ObjectId-backed single public ID, calendar birth date, inactive
inclusion switch, 204 idempotent soft deletion, 409 inactive-link conflict,
and explicit rejection of free-text plus identity in one create request.

**Rationale**: These keep the API additive and outcomes deterministic without
adding update/restore capabilities absent from the issue. Existing book author
credits remain preserved during reviewed backfill.

**Alternatives considered**: Redundant IDs invite divergence; a timestamp for a
birth date adds timezone ambiguity; silent precedence hides caller mistakes.

All material user-facing scope and concurrency clarifications have been
resolved. Production replica hostnames, credentials, retention approval, and
catalogue-scale measurements are deployment inputs, not invented values.

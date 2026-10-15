# Validation Guide: Author Support

These scenarios apply to the implemented Author endpoints, replica-set bootstrap,
tests, and mapping tool. Execution evidence is recorded in [release.md](release.md);
the target-environment checklist remains a separate acceptance activity.
See the [HTTP contract](contracts/http-api.md) for exact statuses/payloads.

## Prerequisites

- .NET 10 SDK and a healthy Docker daemon.
- An isolated development/QA database, not production or a shared catalogue.
- The implemented replica-set bootstrap and an elected writable primary.
- Access protection appropriate to the supplied Author profile data.
- An approved backup/restore and topology conversion plan for existing volumes.

## Automated gates

From the repository root:

```powershell
.\scripts\Test-Coverage.ps1
dotnet format Bookstore.slnx --verify-no-changes --no-restore --verbosity quiet
docker compose up --build -d
```

Expected: no failed/skipped tests, at least 70% overall and 85% Core coverage,
no formatting changes, and healthy primary/API containers. The implemented
coverage suite must use replica-set Testcontainers, not standalone MongoDB.
Check `/api/books/health` separately; liveness alone does not prove database
topology, election, or transaction capability.

## Author lifecycle and linked-book workflow

Using an HTTP client against the isolated environment:

1. Create two Authors named `QA Same Name`; provide optional synthetic profile
   fields on one. Require different IDs, active true, and equal initial
   creation/update times. Confirm each Location resolves.
2. Create one book per Author using only `authorId`; keep their IDs.
   Verify returned reference/credit and exact-ID filtering separates namesakes.
3. Create an old-client free-text book. Verify it has no `authorId` and remains
   visible in category and literal substring searches.
4. Combine identity/category/text filters. Require intersection, not union.
   Confirm regex metacharacters remain literal in the old text filter.
5. Soft-delete the first Author. Require 204, absence from default Author list,
   presence with `includeInactive=true`, and successful direct retrieval.
6. Verify linked books remain listed/retrievable with their original references,
   metadata, and inventory. Creating another link to that Author returns 409.
7. Repeat deletion and confirm no timestamp change. Try an unknown ID (404)
   and malformed ID (400). Verify profile/input values are not in error details.
8. Check name boundaries, optional-field validation, invalid birth date/email,
   malformed JSON/media types, exclusive author/authorId input, and bad query IDs.

## Transaction and failure proof

Run the dedicated real-database concurrency tests. Use deterministic barriers
to exercise each ordering, not sleeps or only simultaneous HTTP requests.

1. Retirement commits first: linked creation returns inactive conflict and no
   Book is inserted.
2. Linked creation commits first: retirement succeeds afterward and the Book remains.
3. Fail insertion after the guarded Author write: neither Book nor revision
   changes commit.
4. Force a retryable write conflict: no duplicate Book or successful-transition
   log is emitted; callbacks remain free of external side effects.
5. Cancel/fail requests and verify sessions are disposed, errors remain safe,
   and subsequent requests still succeed.

## Reviewed legacy mapping

Use the [operations runbook](../../docs/author-operations.md) for exact wrapper commands.
Prepare a protected approved manifest containing book ID, expected credit,
expected association, and target Author ID.

Dry-run must leave all documents unchanged. Apply only approved entries in a
maintenance window. Verify unrelated fields and original credits are preserved,
inactive/missing targets are rejected, stale expected values cause visible
failure, and an interrupted run can resume without duplicate changes.

## Release evidence and rollback rehearsal

- Build/run the final image as non-root and verify transactions on the same
  topology used for release.
- Export `/openapi/v1.json`; verify proposed 1.2.0 metadata and all new schemas/
  outcomes, then regenerate and validate the YAML snapshot.
- Exercise the documented existing-volume conversion without deleting data.
  Confirm primary election, retained records, and indexed Author-ID queries.
- Record coverage, image/commit identity, traces, concurrency outcomes, and
  representative query/transaction latency. Do not invent a universal SLA.
- Rehearse API rollback while retaining the replica set and data. Disable new
  consumers and Author writes/mapping; verify legacy inventory updates preserve
  additive references. Never automatically remove volumes or downgrade topology.
- Obtain deployment access/retention approval before exposing profile/email data.
  Record QA/release sign-off separately from this planning document.

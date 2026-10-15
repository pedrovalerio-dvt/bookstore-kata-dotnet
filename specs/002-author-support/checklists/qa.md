# QA acceptance checklist: FEAT-007 Author Support

**Feature**: [Specification](../spec.md)
**Evidence**: [Release specification](../release.md)
**Contract**: [Current OpenAPI](../../../docs/openapi.yaml)
**Operations**: [Deployment and mapping runbook](../../../docs/author-operations.md)

This is a target-environment/manual execution plan. Unchecked items do not mean
automated failure; automated results are recorded in the release specification.
Record tester/date, environment, image/commit, database, test IDs, actual result,
trace evidence, defects, and approval. Use synthetic profile data only.

## Environment and release gates

- [ ] Confirm an isolated QA database and approved access protection for profiles/email.
- [ ] Confirm .NET 10, Docker, the pinned MongoDB image and an elected replica primary.
- [ ] Run the full coverage script: all tests pass, none skip, >=70% total and >=85% Core.
- [ ] Save the unfiltered Cobertura report; confirm no new production exclusions.
- [ ] Run formatter verification and build/run the final non-root API container.
- [ ] Verify HTTP health separately from database/transaction readiness.
- [ ] Record backup/restore evidence and previous API image before deployment.
- [ ] Verify target replica advertised hostnames resolve from actual clients.

## Author creation and profile validation

- [ ] Create a minimal Author; require 201, unique 24-hex ID, active true and valid Location.
- [ ] Confirm initial createdAt equals updatedAt and timestamps are server-owned.
- [ ] Create a profile with calendar birth date, nationality and synthetic valid email; retrieve unchanged.
- [ ] Create a profile with explicit active false; default directory excludes it.
- [ ] Create two Authors with exactly the same name; require distinct IDs and no deduplication.
- [ ] Verify names of 1/255 UTF-16 code units succeed and 256 fails.
- [ ] Verify missing/null/empty/whitespace name fails with 400 and inserts nothing.
- [ ] Verify supplied empty/whitespace email or nationality fails; null/omitted values succeed.
- [ ] Verify invalid email and invalid date/date-time-as-birth-date return 400.
- [ ] Verify names and nonblank optional text are not silently trimmed/normalized.
- [ ] Confirm no invented nationality vocabulary, email uniqueness or future-birth-date rule.
- [ ] Confirm optional null fields are absent from JSON.
- [ ] Verify malformed JSON, numeric-string coercion and null active are rejected.
- [ ] Verify unsupported content type returns 415.

## Author directory, retrieval and retirement

- [ ] Confirm default directory contains active profiles only.
- [ ] Confirm includeInactive=true includes both states; false restores default behavior.
- [ ] Confirm empty or nonboolean includeInactive is 400.
- [ ] Confirm empty directory is 200 [] and do not rely on ordering.
- [ ] Retrieve both active and inactive Authors by ID.
- [ ] Confirm uppercase hexadecimal IDs resolve the same identity with canonical output.
- [ ] Confirm malformed IDs return 400 and valid unknown IDs return 404.
- [ ] Delete an active Author with linked books; require empty 204 and active false on retrieval.
- [ ] Confirm createdAt/profile fields remain unchanged and updatedAt reflects retirement.
- [ ] Repeat deletion; require 204 with exactly the same updatedAt.
- [ ] Confirm no hard-delete, profile-update or restoration endpoint was added.

## Managed and legacy Book relationships

- [ ] Create one book per same-name Author using authorId only; verify identity separation.
- [ ] Confirm new linked book credit is the managed Author name, with stored/reference ID preserved.
- [ ] Confirm legacy free-text and authorless creation still succeed without creating Authors.
- [ ] Confirm both non-null author and authorId return 400, including empty free text.
- [ ] Confirm malformed/empty authorId is 400, unknown Author 404, inactive Author 409.
- [ ] Confirm rejected creation leaves no Book or coordination revision change.
- [ ] Filter books by exact authorId; namesakes' and unlinked books do not leak into results.
- [ ] Combine authorId/category/text in different query orders; require AND intersection.
- [ ] Confirm valid unknown identity returns 200 [] and malformed identity returns 400.
- [ ] Confirm text search still uses case-insensitive literal substrings, including escaped punctuation.
- [ ] Confirm existing ID/exact-title lookups, create defaults and categories are unchanged.
- [ ] Confirm /single rejects Author identity/text filters rather than ignoring them.
- [ ] Update inventory/activity; verify Author reference, credit and publication metadata remain unchanged.
- [ ] Retire an Author and verify linked books remain retrievable/searchable, including inactive books.

## Transactions and fault behavior

- [ ] Review/run deterministic delete-first test: actual contention is observed and no new link commits.
- [ ] Review/run deterministic create-first test: exactly one historical Book remains after retirement.
- [ ] Force a Book-write failure after Author guard; verify both guard and Book rollback.
- [ ] Exercise driver transaction retry; confirm no duplicate Books or transition logs.
- [ ] Cancel requests; confirm cancellation propagates and sessions dispose without blocking later requests.
- [ ] In isolation, make MongoDB unavailable; require generic 500 without internal connection/profile details.
- [ ] Confirm errors remain application/problem+json even for restrictive Accept.
- [ ] Confirm instance, traceId, response X-Trace-Id and JSON logs correlate.
- [ ] Confirm successful logs contain IDs, not profile/email/birth-date objects.
- [ ] Confirm invalid-profile responses/warnings never echo submitted profile values.

## Deployment and reviewed legacy mapping

- [ ] Rehearse existing-volume standalone conversion with verified backup; do not delete volumes.
- [ ] Confirm bootstrap initializes only uninitialized rs0 and refuses unexpected topology.
- [ ] Repeat bootstrap/restart; require retained counts/IDs, writable primary and no seed duplication.
- [ ] Confirm ix_books_authorId exists and exact-identity query has an eligible index.
- [ ] Confirm substring search is not incorrectly presented as a fast indexed-prefix operation.
- [ ] Prepare and approve exact book/credit/target identity mapping with namesake review.
- [ ] Run default dry-run; verify no documents/revisions change.
- [ ] Apply reviewed links; preserve original credits, dates, inventory and unrelated metadata.
- [ ] Verify malformed/duplicate/extra-field manifest entries fail before writes.
- [ ] Verify stale credit, conflicting existing link, and missing/inactive targets fail visibly.
- [ ] Verify failed Book update rolls back Author revision.
- [ ] Resume a partially committed run; identical links skip and remaining entries apply once.
- [ ] Verify same-link resumption remains a no-op if the Author was subsequently retired.
- [ ] Confirm progress contains no credits/email and maintenance files have restricted permissions.
- [ ] Confirm owned temporary files are cleaned; exercise operator recovery after forced interruption.
- [ ] Rehearse API rollback while retaining replica topology/references; freeze new writes/consumers.

## Documentation, performance and sign-off

- [ ] Confirm live API 1.2.0 has nine operations with Author/Book reference/error schemas and Location headers.
- [ ] Compare generated YAML to live JSON and validate OpenAPI v3.
- [ ] Verify Scalar works only in Development, not Production.
- [ ] Follow business guide and operator runbook examples in the target environment.
- [ ] Measure representative directory/query response sizes and transaction contention/latency.
- [ ] Record catalogue scale and actual outcomes; no universal latency SLA is claimed.
- [ ] Approve external access, TLS/secrets, retained profile data and backup policies.
- [ ] Record defects, QA sign-off, Product Owner acceptance and production release approval.

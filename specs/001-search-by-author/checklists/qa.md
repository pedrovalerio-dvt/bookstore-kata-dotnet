# QA release checklist: Search by Author

**Feature**: [Approved specification](../spec.md)
**Release**: [Release specification and automated evidence](../release.md)
**Contract**: [OpenAPI YAML](../../../docs/openapi.yaml)

These items are a manual/environment acceptance plan. Unchecked items do not
imply automated failures; automated checks are recorded separately in the release
specification. Record environment, image/commit identity, tester, execution date,
test-data IDs, observed result, and trace evidence for every acceptance run.

## Environment and baseline

- [ ] Use an isolated QA database; confirm test writes cannot reach production.
- [ ] Confirm .NET 10, healthy Docker/MongoDB, API configuration, and base URL.
- [ ] Run `.\scripts\Test-Coverage.ps1`; require all tests to pass with no skips.
- [ ] Confirm the Cobertura report meets 70% overall and 85% Core line coverage.
- [ ] Run the documented formatter check and build the runtime container.
- [ ] Confirm `/api/books/health` returns empty 200 before testing catalogue operations.
- [ ] Save the prior API image identity and verify rollback access before rollout.

## Representative test catalogue

Create the following books with `POST /api/books`; record every returned ID.
All successful creations must return 201 with a complete Book and Location.
Use separate titles so assertions do not rely on unspecified ordering.

| Label | Title | Author | Category | Active |
|---|---|---|---|---|
| A | QA Programming One | Kent Beck | Programming | true |
| B | QA Programming Two | KENT BECK | Programming | false |
| C | QA Fiction | Kent Beck | Fiction | true |
| D | QA Other Writer | Other author | Programming | true |
| E | QA No Author | Omit author | Programming | true |
| F | QA Literal Plus | C++ (Jr.) | Programming | true |
| G | QA Literal Pattern | prefix.*suffix | Fiction | true |

- [ ] Create A-G in isolation, then verify unfiltered listing contains all seven.
- [ ] If legacy-data acceptance is required, insert a separate valid document
  with a missing `author` field using approved QA database access; verify it
  remains listable. Never run direct-data fixtures against production.

## Primary author search

- [ ] `?author=Kent` returns exactly A, B, C.
- [ ] `?author=kEnT` returns exactly the same IDs as `Kent`.
- [ ] `?author=eNt%20b` matches across the internal space and returns A, B, C.
- [ ] `?author=BECK` matches the suffix and returns A, B, C.
- [ ] Confirm B is returned despite inactivity; no active-status filter was added.
- [ ] `?author=nobody` returns 200 with `[]`, not 404 or a null body.
- [ ] Search excludes E and any legacy missing-author document.
- [ ] Every returned item retains the existing Book shape, ID, quantity,
  category, and publication metadata; no search-result DTO/envelope was added.
- [ ] Compare sets of IDs, not response ordering.

## Category intersection

- [ ] `?author=beck&category=Programming` returns exactly A and B.
- [ ] `?category=Fiction&author=beck` returns only C; query parameter order is irrelevant.
- [ ] `?author=beck&category=Science` returns 200 with `[]`.
- [ ] `?author=Kent&category=unknown` returns 400 Problem Details.
- [ ] Empty category and incorrectly cased `programming` remain 400.
- [ ] Omitting author keeps the existing category-only result set, including E.
- [ ] Omitting both filters keeps the existing complete-catalogue result set.

## Input encoding, literal matching, and invalid input

- [ ] `?author=` returns 400 Problem Details; it must not return all books.
- [ ] `?author=%20%20` and `?author=%09%0A` return 400.
- [ ] `?author=%20Kent%20` does not silently trim into a match for A-C.
- [ ] `?author=C%2B%2B%20%28Jr.%29` returns only F.
- [ ] `?author=.*` returns only G; it must not act as a wildcard.
- [ ] Test a stored author containing brackets, backslash, or anchors; use a
  URL-encoded matching fragment and verify symbols are literal.
- [ ] Verify `&`, `+`, `#`, and `?` in author text are URL-encoded by the caller;
  a decoded symbol is not interpreted as another query parameter or regex syntax.
- [ ] With accented QA author data, verify same-accent case variants match and
  the unaccented spelling is not promised to match.
- [ ] Repeat valid search and verify no book fields, counts, or inactive dates change.

## Error handling and compatibility

- [ ] Every invalid search returns `application/problem+json` with type, title,
  status, detail, instance, and traceId.
- [ ] `instance` equals `urn:trace:<traceId>` and matches the `X-Trace-Id` header/log correlation.
- [ ] Repeat invalid search with `Accept: text/plain`; it still returns safe JSON Problem Details.
- [ ] In an isolated fault test, make MongoDB unavailable and confirm a generic
  500 response without internal connection details or stack traces.
- [ ] Cancel an author request; verify cancellation is propagated in the
  automated tests and the application remains usable for later requests.
- [ ] `/api/books/single?author=Kent` remains 400 with guidance to collection search.
- [ ] ID lookup and exact-title lookup retain existing success/not-found behavior.
- [ ] Create/update workflows retain status codes, validation, and metadata preservation.
- [ ] Confirm authentication/authorization and deployment access policies are
  unchanged; do not interpret the feature as adding identity protection.

## Documentation, performance, and rollout

- [ ] Live `/openapi/v1.json` reports API version 1.1.0 and optional author
  query input on collection GET, with partial/literal/case-insensitive semantics.
- [ ] Compare the generated live document with the committed YAML snapshot.
- [ ] Verify the functional guide's examples and category/author AND semantics.
- [ ] Verify Scalar documents author search in Development and remains absent
  in Production.
- [ ] Use representative target catalogue sizes to record observed search
  latency and response sizes. Report regressions; no universal latency SLA is
  claimed and author-only search may scan the catalogue.
- [ ] Confirm no database migration, author index, new secret, environment
  variable, or package is needed for deployment.
- [ ] Exercise rollback in a safe environment; remove the author parameter
  from consumers when reverting because the prior collection API ignored it.
- [ ] Record defects, QA sign-off, Product Owner acceptance, and release approval.

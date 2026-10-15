# Legacy Java Bookstore functional guide

> Historical baseline from commit `cebc7fb`, before the .NET migration.
> Source paths and behavior below refer to that commit, not the current API.
> See the [current functional guide](../functional-guide.md).

## Purpose and evidence boundary

This guide describes the catalogue and stock-management behavior implemented in
this repository. It is intended for Product Owners and Business Managers and
records current behavior, including defects, rather than an ideal future design.
The implementation is a Java HTTP API with MongoDB storage. Despite the
repository name, there is no .NET application, browser interface, or outbound
HTTP client in the tracked application source. Consequently, no user-interface
screens, frontend validation, or web-to-backend integration can be established
from this repository.

The conclusions below come from the controller, service, data models, startup
code, database initializer, and the existing controller test. They are
source-derived, not observations of a running deployment. Dates and automatic
HTTP error handling depend on the configured framework defaults.

The companion [OpenAPI specification](openapi.yaml) is the machine-readable
contract. The [technical architecture](technical-architecture.md) explains
implementation details and source references.

## Supported capabilities

| Capability | Current behavior |
|---|---|
| Create a book | Adds a new catalogue record with a generated identifier. |
| Update a book | Sets absolute quantity and active status; maintains the inactivity timestamp. |
| Get a book | Attempts an exact title and/or identifier lookup; identifier lookup is defective. |
| List books | Returns the catalogue, optionally filtered by a recognized category name. |
| Health check | Confirms that an HTTP handler responds, not that storage is operational. |

There is no implemented deletion, author search, category-management API,
pagination, sorting control, sales, reservations, checkout, pricing, or stock
movement history. The earlier README's claim that deletion is supported is not
backed by a route in the code.

No application authentication or authorization is configured. The code does not
distinguish shoppers, administrators, or stock managers. This does not establish
whether an external deployment gateway restricts access.

## Information recorded for each book

| Information | Business meaning and current constraint |
|---|---|
| Identifier (`id`) | Generated on creation. Used in the update request body. Identifier lookup cannot currently be relied upon. |
| Title (`title`) | Must be present and non-null to reach the create success branch. Empty or whitespace-only titles are allowed. There is no application uniqueness check. |
| Author (`author`) | Optional free text. There is no functioning author filter. |
| Category (`categoryId`) | Creation takes a category **name**, but responses contain a numeric category ID. Omitting the category stores `0`, which is outside the six defined categories. |
| Quantity (`quantity`) | Whole-number absolute stock value. Missing values default to `0`. Negative values are not rejected. It is not a stock increment or decrement command. |
| Description (`description`) | Optional free text. |
| Language (`language`) | Optional free text, not a validated language code. |
| Active status (`active`) | Defaults to `true` on creation but `false` on update if omitted. It does not hide a book from list or lookup operations. |
| Inactive date (`inactiveDate`) | Optional date on creation. On update, generated using server time for inactive books and cleared for active books. |
| Publisher (`publisher`) | Optional free text. |
| Publisher date (`publisherDate`) | Optional date. No chronology or relationship to other dates is validated. |

Text length, title/author uniqueness, language vocabularies, date ordering,
nonnegative stock, and consistency between active status and inactive date are
not validated by application logic. Transport-level JSON parsing and Java data
types can still reject values that cannot be represented.

### Categories

| Numeric response/storage ID | Exact creation/filter name |
|---|---|
| 1 | Fiction |
| 2 | Non-Fiction |
| 3 | Science |
| 4 | Biography |
| 5 | Children |
| 6 | Programming |

Names are case-sensitive and are not trimmed. For example, `Programming` is
recognized, but `programming`, `6`, and `Programming ` are not. These categories
are defined in application code. Although the database initializer also creates
category records, editing those database records does not change API behavior.

## Workflow: create a book

1. Submit a JSON object to `POST /api/books`.
2. The controller checks only whether the title is non-null. A missing or null
   title returns status `404` with an empty body. An empty title passes this check.
3. If a category name is supplied, the service resolves it against the six
   names above. An unknown non-null name causes an unhandled server error
   rather than a business validation message. A missing/null category becomes `0`.
4. The service generates an identifier and inserts a new MongoDB book document.
5. The API returns `201` and the constructed book representation. It does not
   set a `Location` response header.

Example request:

```json
{
  "title": "Test Driven Development",
  "author": "Kent Beck",
  "categoryId": "Programming",
  "quantity": 3,
  "language": "English",
  "active": true,
  "publisher": "Addison-Wesley"
}
```

The response contains a generated string `id` and numeric `categoryId: 6`.
Missing optional text/date fields remain null in the model and are normally
omitted by the default JSON serializer.

Creation does not derive an inactivity timestamp from active status. An inactive
book may be created without an inactive date, and an active book may be created
with one. Submitting the same details again creates another record: there is no
application duplicate detection or idempotency key.

## Workflow: update stock and active status

1. Obtain the target identifier from a create or list response.
2. Submit `id`, `quantity`, and `active` to `PUT /api/books`. The identifier is
   in the JSON body, not in the URL.
3. The controller rejects a missing/null ID with an empty `404`. A malformed
   nonblank identifier causes a server error. A blank/whitespace identifier
   generates a fresh identifier but still attempts an update, not an insert.
4. The service writes quantity, active status, and inactive date only.
5. If the database operation returns without an exception, the controller
   returns `201`, even if no book matched the identifier.

```json
{
  "id": "507f1f77bcf86cd799439011",
  "quantity": 5,
  "active": false
}
```

**Always send both quantity and active status.** Omitting quantity overwrites it
with `0`; omitting active overwrites it with `false` and triggers deactivation.
This operation does not preserve omitted values like a partial patch.

| Update action | Inactive date behavior |
|---|---|
| Set active to `false` | Sets the date to the server's current time. |
| Repeat an inactive update | Replaces the previous date with a new timestamp. |
| Set active to `true` | Clears the inactive date to null. |

Title, author, category, description, language, publisher, and publisher date
cannot be changed through this update DTO. Their stored values are left intact.
The response is not a fresh copy of the stored record: it contains update
values, numeric category `0`, and null/omitted descriptive fields. Consumers
must not replace their complete book display with this response or interpret
category `0` as a successful recategorization.

A successful status is **not confirmation that a book was found or changed**.
The service does not inspect the database matched/modified counts and does not
create a missing record. A subsequent list can be used to inspect persisted
state; the single-book ID lookup is unreliable.

## Workflow: get one book

Call `GET /api/books/single` with optional query parameters:

| Parameter combination | Implemented behavior |
|---|---|
| `name` only | First exact, case-sensitive title match from an unsorted full catalogue scan. |
| `id` only | Attempts lookup, but compares string object identity rather than ID text. Normal requests cannot rely on a match. |
| Both `name` and `id` | Searches by name first, then scans again for ID. An ID match replaces the name result; an ID miss leaves the name result unchanged. This is not an AND filter. |
| Neither | Returns `200` with no entity and does not query storage. |
| `author` present | Immediately fails with an unhandled exception, including when its value is empty. |
| No match | Returns `200` with an empty body, not `404`. |

Example: `GET /api/books/single?name=Test%20Driven%20Development`.
Duplicate titles are permitted, so title lookup is not an unambiguous business
key and selection order is not guaranteed. If a stored book has a null title,
name lookup can fail while scanning it.

Active status and stock quantity do not restrict lookup. An inactive or
zero/negative-stock book is still eligible.

## Workflow: list books

Call `GET /api/books` to retrieve all books or
`GET /api/books?category=Programming` to retrieve category `6`.

The API returns `200` and a JSON array; no records produces `[]`. It includes
inactive books and books with any stock quantity. A recognized category with
no matching records produces `[]`.

**An unrecognized category returns all books.** Missing, empty, incorrectly
cased, or otherwise unrecognized values disable filtering rather than
returning an error. This differs from creation, where an unknown category
causes a server error.

There is no page size, continuation token, total-count envelope, explicit sort,
title search, or active-status filter. Results are a bare array with unspecified
ordering. Large catalogues are read in full even for category filtering.

## Failure handling and operational meaning

| Situation | Observable application outcome / limitation |
|---|---|
| Missing/null create title | Empty `404`. |
| Missing/null update ID | Empty `404`. |
| No single-book result | Empty `200`. |
| Unsupported author filter | Unhandled exception, normally `500`. |
| Unknown non-null create category | Unhandled exception, normally `500`. |
| Malformed nonblank update ID | Unhandled exception, normally `500`. |
| Database unavailable or incompatible stored document | Unhandled failure; no friendly business error is constructed. |
| Malformed JSON / unsupported request media type | Framework-level failure, normally `400` / `415`; precise decoding cases depend on the provider. |
| Unsupported response media type | Framework negotiation can return `406`. |
| Unmatched URL / unsupported HTTP method | Framework routing normally returns `404` / `405`. |

There is no common error JSON object, business error code, Problem Details
implementation, or client-facing error message contract. The exception text
`Filtering by author is not implemented yet` exists in the code but is not a
guaranteed HTTP response body.

`GET /api/books/health` returns an empty `200` without querying MongoDB. It is
only a reachability signal; it must not be interpreted as proof that catalogue
reads or writes are available.

## Product implications and unimplemented guarantees

The current API supports basic catalogue insertion and stock/status edits, not
a complete retail inventory workflow. In particular:

- Quantity updates overwrite a value; there are no reservations, atomic
  increments, nonnegative-stock protection, or conflict/version checks.
- Inactivity is a flag, not deletion or a visibility rule. The timestamp is the
  latest deactivation update time, not an immutable history.
- There is no audit trail, user attribution, purchase workflow, financial
  information, or documented retention policy.
- Identifier lookup, successful-update confirmation, category input semantics,
  omitted-field resets, and error feedback impose integration risks.

These are implementation limitations, not approved future requirements.
No remediation or application changes are included in these deliverables.

## Evidence map

| Subject | Source |
|---|---|
| Routes and controller status codes | `src/main/java/com/bookstore/controller/BookController.java:9-93` |
| Create fields and defaults | `src/main/java/com/bookstore/controller/CreateBookDTO.java:5-100` |
| Update fields and defaults | `src/main/java/com/bookstore/controller/UpdateBookDTO.java:3-33` |
| Category matching and list behavior | `src/main/java/com/bookstore/service/BookService.java:23-78` |
| Creation, update, and category resolution | `src/main/java/com/bookstore/service/BookService.java:81-147` |
| Lookup order and ID comparison defect | `src/main/java/com/bookstore/service/BookService.java:149-175` |
| Category vocabulary | `src/main/java/com/bookstore/model/Category.java:10-19` |
| Existing create-path test | `src/test/java/com/bookstore/controller/BookControllerTest.java:18-84` |

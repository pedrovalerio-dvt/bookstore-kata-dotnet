# Bookstore functional guide

## Scope and compatibility

This guide describes the migrated .NET 10 API. There is no web frontend in this
repository. The API manages a catalogue and absolute stock quantities; it does
not implement purchasing, reservations, pricing, deletion, or user accounts.

Existing routes, camel-case JSON field names, MongoDB identifiers, and stored
book metadata are retained. Defects and invalid-input behavior were deliberately
corrected during migration. The [legacy guide](legacy/functional-guide.md)
records the Java behavior at commit `cebc7fb`; it is not the current contract.
See [OpenAPI](openapi.yaml) for payloads and
[architecture](technical-architecture.md) for implementation and rollout details.

## Catalogue information

| Field | Meaning and current rule |
|---|---|
| `id` | Generated 24-character hexadecimal MongoDB ID; preserved for existing records. |
| `title` | Required, nonblank on creation; duplicate titles remain allowed. |
| `author` | Optional free text. Collection search supports case-insensitive partial matching. |
| `categoryId` | Creation accepts an exact category **name**; responses contain a numeric category ID. Missing/null creates category `0`. |
| `quantity` | Nonnegative integer representing absolute stock, not a stock adjustment. Creation defaults to `0`; update must explicitly supply it. |
| `description`, `language`, `publisher` | Optional free text, without a controlled vocabulary or new length restriction. |
| `active` | Creation defaults to `true`; update must explicitly supply it. Inactive books remain visible in list and lookup results. |
| `inactiveDate` | Active creation must not supply a non-null value. Inactive creation preserves a supplied date or uses server UTC time. Updates stamp server UTC time when inactive and clear the date when active. |
| `publisherDate` | Optional ISO 8601 date-time; no business chronology restriction. |

Dates are stored as MongoDB UTC dates with millisecond precision. Null response
fields are omitted. Legacy records may still contain missing metadata, negative
stock, or inconsistent dates; migration does not rewrite historical data.

| Numeric ID | Exact category name |
|---|---|
| 1 | Fiction |
| 2 | Non-Fiction |
| 3 | Science |
| 4 | Biography |
| 5 | Children |
| 6 | Programming |

Category names are case-sensitive and are not trimmed. `Programming` is valid;
`programming`, `6`, and `Programming ` are not. The category vocabulary is defined
in code, not controlled by edits to the legacy database categories collection.

## Create a book

Submit `POST /api/books`:

```json
{
  "title": "Test Driven Development",
  "author": "Kent Beck",
  "categoryId": "Programming",
  "quantity": 3,
  "active": true,
  "publisher": "Addison-Wesley"
}
```

A successful response is `201 Created` with the complete inserted book and a
`Location` header pointing to `/api/books/single?id=<generated-id>`.
The returned `categoryId` is `6`, not `"Programming"`.

Blank/missing title, negative quantity, unknown category, and an active book
with an inactivity date return `400`. Invalid JSON or invalid date syntax also
returns `400`. A non-JSON request media type returns `415`.

Creation does not deduplicate books and does not support idempotency keys.
Retrying a successful creation can insert another book.

## Update quantity and active status

Submit `PUT /api/books`:

```json
{
  "id": "507f1f77bcf86cd799439011",
  "quantity": 5,
  "active": false
}
```

All three properties must be present with valid non-null values. Omitting
quantity or active returns `400` rather than silently resetting inventory.
The identifier must be exactly 24 hexadecimal characters. Negative stock is
rejected. Quantity is an absolute replacement, not an increment or decrement.

Success returns `200 OK` with the complete persisted record, including existing
title, category, author, and publication metadata. A well-formed ID that does
not exist returns `404`; no new book is inserted.

Setting active to false sets inactiveDate to the current server UTC time,
including on repeated inactive updates. Setting active to true clears it.
Title, author, category, description, language, publisher, and publisher date
are not editable through this operation.

Each update is atomic for one book. There is no version/conflict check: competing
absolute quantity assignments use last-write-wins behavior.

## Get one book

Use exactly one lookup filter:

- `GET /api/books/single?id=507f1f77bcf86cd799439011`
- `GET /api/books/single?name=Test%20Driven%20Development`

ID lookup now compares the actual MongoDB ID value, including uppercase
hexadecimal input, rather than Java string reference identity. Title lookup is
an exact match under the collection's collation (case-sensitive with the normal
MongoDB default). Duplicate titles are allowed; selection is not ordered.

A found book returns `200`; a miss returns `404`. Supplying neither filter,
both filters, an invalid ID, a blank title, or any `author` parameter returns
`400`. To search by author, use the collection operation described below;
author does not become a unique single-book lookup key.

## List books

`GET /api/books` returns all books as a JSON array.
`GET /api/books?category=Programming` returns numeric category `6`.
No matching records produces `[]`.

An unknown, empty, or incorrectly cased category returns `400`; it no longer
silently disables filtering. Filtering is executed in MongoDB rather than
after loading the entire catalogue.

There is still no pagination, explicit sorting, title filter on list, or
active-status filter. Results include inactive and legacy negative-stock books.
Consumers must not assume ordering.

## Search by author

Use `GET /api/books?author=kent` to find every book whose stored author contains
`kent`, regardless of capitalization. For example, `kent`, `KENT`, and `kEnT`
all match `Kent Beck`. A fragment can occur anywhere in the author text.
Multiple books by the same author are returned, including inactive books.
The operation does not alter inventory or any catalogue record.

Combine category and author to narrow results:

```text
GET /api/books?author=beck&category=Programming
```

Both conditions must match: this request returns Programming books whose author
contains `beck`, not all Programming books or all books by that author.
Category matching remains exact and case-sensitive.

| Search input or result | Outcome |
|---|---|
| Author omitted | Existing unfiltered/category-only behavior is unchanged. |
| Author has no matches | `200 OK` with `[]`, not `404`. |
| Author is empty or whitespace-only | `400` Problem Details. |
| Author is nonblank with leading/trailing spaces | Spaces are preserved and must occur in the stored text; they are not trimmed. |
| Author contains punctuation or search symbols | Text is literal: `.*` matches those two characters, not every author. |
| Stored author is null or missing | Does not match a supplied author search; still appears in unfiltered listings. |
| Valid author with an invalid category | `400`; the category filter is not silently discarded. |

URL-encode reserved characters. For example, the fragment `C++ (Jr.)` can be
sent as `author=C%2B%2B%20%28Jr.%29`; a literal plus sign must be encoded as
`%2B`, since an unencoded plus in a query is interpreted as a space.

Search is not fuzzy matching, accent folding, transliteration, or a separately
managed author directory. An accented name does not match its unaccented spelling.
There is no new pagination or ordering guarantee. Large-catalogue substring
search may require more time than indexed exact-ID lookup.

## Error feedback and operation

All application errors use an RFC 7807 Problem Details response, for example:

```json
{
  "type": "about:blank",
  "title": "Book not found",
  "status": 404,
  "detail": "No book matched the supplied identifier or title.",
  "instance": "urn:trace:example-trace-id",
  "traceId": "example-trace-id"
}
```

The `instance` and `traceId` values correlate with the `X-Trace-Id` response
header and JSON request logs. They are diagnostic identifiers, not book IDs.
Unexpected server failures return a generic `500` message, not internal
exception details. Authentication/authorization is unchanged: no application
identity provider or access policy is configured. Production access control
must be supplied by an approved deployment policy.

`GET /api/books/health` returns an empty `200`. It establishes HTTP liveness
only and does not prove MongoDB readiness.

## Migration changes requiring consumer review

| Java behavior | Current .NET behavior |
|---|---|
| Missing title or update ID returned empty 404 | Invalid input returns 400 Problem Details |
| Blank title/negative stock accepted | Rejected on new writes |
| Unknown create category failed with 500 | Returns 400 |
| Unknown list category returned all books | Returns 400 |
| ID lookup used reference equality | Correct MongoDB ID lookup |
| No match returned empty 200 | Returns 404 Problem Details |
| Both lookup filters had precedence rules | Exactly one filter required |
| Author query on `/single` threw an unhandled exception | Returns explicit 400 on `/single`; collection author search is available separately |
| Missing update fields reset inventory | Both fields required; missing fields rejected |
| Update returned 201 and incomplete metadata | Returns 200 and complete persisted state |
| Unmatched update falsely returned success | Returns 404; no insert |
| Inactive create could omit inactivity timestamp | Uses server UTC timestamp if omitted |
| Active creation accepted an inactivity date | Rejects inconsistent new input |

No book deletion, metadata editing, reservation protection, immutable audit
history, financial processing, or identity system was added.

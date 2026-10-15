# HTTP Contract: Author Support

**Status**: Implemented; [generated OpenAPI](../../../docs/openapi.yaml) is the
machine-readable contract.
**API version**: 1.2.0 (additive to 1.1.0).

## Common behavior

Base prefix `/api`. JSON uses camelCase and omits null response properties.
IDs are 24-hex strings; uppercase input is accepted and canonical lowercase
returned. All responses include existing trace correlation. Errors use
`application/problem+json` with type, title, status, detail, instance, and traceId.
Unexpected failures return generic 500; validation never echoes profile values.
No authentication scheme is added by this feature.

## Author resource

```json
{
  "id": "507f1f77bcf86cd799439011",
  "name": "Kent Beck",
  "birthDate": "1961-03-31",
  "nationality": "United States",
  "email": "author@example.invalid",
  "active": true,
  "createdAt": "2026-10-15T10:00:00.000Z",
  "updatedAt": "2026-10-15T10:00:00.000Z"
}
```

Required response fields: `id`, `name`, `active`, `createdAt`, `updatedAt`.
Optional fields: `birthDate`, `nationality`, `email`.
Internal coordination state is never exposed.

### POST /api/authors

Input: required `name`; optional/null `birthDate`, `nationality`, `email`;
optional non-null boolean `active` defaulting to true. Identifiers/timestamps
are not request fields; they are always server-controlled.

Name is nonblank and at most 255 UTF-16 code units. Supplied optional text must
be nonblank, and email must satisfy FluentValidation email-format validation.
Birth date uses `date` format, not `date-time`. No uniqueness, past-date,
country-vocabulary, biography, or website rule is introduced.

Success: `201` with Author and `Location: /api/authors/{id}`.
Failures: `400` validation/malformed body, `415` unsupported media type,
`500` unexpected persistence failure. Duplicate names succeed with distinct IDs.

### GET /api/authors

Optional query `includeInactive`: boolean, default false.
False returns active Authors only; true includes both states.
Malformed/empty boolean returns 400.

Success: `200` with an array of Authors; no matches is `[]`.
No pagination or ordering guarantee is introduced.
Failures: `400` invalid query, `500` unexpected failure.

### GET /api/authors/{id}

Success: `200` with the Author, including an inactive Author.
Failures: `400` malformed ID, `404` well-formed ID not found, `500` unexpected failure.
Do not constrain route matching so malformed IDs accidentally become router 404s.

### DELETE /api/authors/{id}

Soft-delete only. Success: empty `204` for active-to-inactive and repeated
deletion of an existing inactive Author. Preserve timestamps on the repeat.
Existing books are not modified. Creation and soft deletion must satisfy
the strict ordering in the implementation plan.

Failures: `400` malformed ID, `404` unknown ID, `500` unexpected failure.
There is no cascade, restoration endpoint, or profile update endpoint.

## Book contract extensions

### POST /api/books

Add optional/null `authorId` to existing request. Non-null input must be a
valid ID and cannot be combined with non-null `author`.

```json
{
  "title": "Managed Author Example",
  "authorId": "507f1f77bcf86cd799439011",
  "categoryId": "Programming",
  "quantity": 2
}
```

If the Author is active, create transactionally and return existing `201` Book
response/Location, with additional `authorId` and persisted `author` credit.
Existing free-text and omitted-author clients keep their current behavior.

New failures: `400` malformed ID or mutually exclusive inputs; `404` referenced
Author missing; `409` referenced Author inactive after transactional resolution.
Use a stable Author-related title/detail so the cause is distinguishable from
a missing Book. Retain existing 400/415/500 behavior.

### GET /api/books

Add optional `authorId` exact-equality query.
Combine with existing `author` substring and `category` using AND.
Empty/malformed `authorId` returns 400; a well-formed unknown ID returns 200 `[]`.
Inactive Author identities remain searchable for historical books.

Example: `/api/books?authorId=507f1f77bcf86cd799439011&category=Programming`.
Existing author-text semantics, null-author behavior, inactive-book inclusion,
and unspecified ordering remain unchanged.

### Book responses and existing operations

All Book responses may add `authorId`; omit it when absent. The old author
credit remains present when non-null. Stock updates preserve both values.
Existing `/api/books/single` remains exactly-one ID/title lookup. Explicitly
reject supplied `authorId` there as 400, like the existing author parameter.
Health, category definitions, update input, and existing status codes are unchanged.

## OpenAPI implementation requirements

Add four operations, Author/create schemas, optional Book ID fields/filter,
correct required/non-null lists, date-only format, 204 empty response, and all
documented Problem Details outcomes. Extend Location metadata beyond CreateBook.
Preserve trace headers on success and error responses and Development-only Scalar.
Generate `docs/openapi.yaml` from live JSON and schema-validate it; do not copy
this proposed contract into the current snapshot before the API exists.

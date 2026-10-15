# Data Model: Author Support

This describes the implemented local schema; production deployment is not claimed.
See the [glossary](../../GLOSSARY.md) and [functional specification](spec.md).

## Author aggregate

| Public field | Domain representation | BSON storage | Rule |
|---|---|---|---|
| `id` | String | `_id`: ObjectId | Server-generated; canonical lowercase 24-hex output |
| `name` | String | String | Required, nonblank, maximum 255 UTF-16 code units; preserve supplied spelling/spacing |
| `birthDate` | Optional DateOnly | Optional UTC-midnight BSON date | JSON `YYYY-MM-DD`; no timezone or time-of-day |
| `nationality` | Optional string | Optional string | Null/omitted accepted; supplied text must be nonblank; no country vocabulary |
| `email` | Optional string | Optional string | Null/omitted accepted; supplied text must pass email validation; not unique |
| `active` | Boolean | Boolean | Defaults true on create; strict JSON boolean |
| `createdAt` | DateTimeOffset | UTC BSON date | Server time, immutable; millisecond precision |
| `updatedAt` | DateTimeOffset | UTC BSON date | Equals creation time initially; changes on active-to-inactive only |
| Not exposed | 64-bit coordination revision | `linkRevision`: Int64 | Starts at zero; actual transactional guard write; never a public profile field |

MongoDB `_id` is the only stored identifier. Public `id` is its representation,
not a second uniqueness field. Optional null profile fields are omitted in
JSON, consistent with current serialization.

Name/email indexes are not unique. Same-name profiles intentionally remain
distinct. A missing/invalid stored required field is an explicit persistence
failure, not fabricated successful data.

### State transitions

- Creation may set active true (default) or explicitly false.
- Active -> inactive changes active and updatedAt atomically.
- Inactive -> inactive is an idempotent no-op, including timestamp/revision.
- Inactive -> active, profile editing, and physical deletion are not exposed.
- Link coordination changes internal revision only; it is not a profile update.

The domain entity owns these lifecycle rules. Application services validate
requests and coordinate persistence; storage does not invent a second set of rules.

## Book aggregate extension

| Field | Change | Compatibility |
|---|---|---|
| `authorId` | Optional string in domain/JSON, BSON ObjectId when stored | Missing/null remains an unlinked legacy book; absent JSON property when null |
| `author` | Retain current optional string | New linked create snapshots the Author name; legacy free-text create unchanged |
| All existing fields | No semantic change | Preserve IDs, metadata, quantity, dates, activity, and category behavior |

Cardinality: one Author has zero or more books; each book has zero or one
managed Author. Author does not contain a growing list of book IDs.

Create input may contain `authorId` or `author`, never both non-null. An empty
`authorId` is invalid rather than an instruction to use free text. The existing
inventory update DTO remains unchanged and cannot edit or clear a link.

Reviewed association tooling adds an absent link only when the expected current
credit and association match. It preserves the original credit, including
spelling variants; this intentionally differs from new linked creation.

## Consistency boundary

Normal Author lifecycle updates are single-document changes. Linked creation
and reviewed linking require cross-aggregate transactions: conditionally modify
an active Author's revision and insert/update the book in the same session.
Retirement transactions write the same Author document.

If linking commits first, retirement retains that historical relationship.
If retirement commits first, a competing/retried link cannot commit. Failed
Book writes roll back the guarded Author write. Unknown commit outcomes use
driver-supported commit handling, not an unconditional second insertion.

## Indexes and migration

- Retain existing `books.title` and `books.categoryId` indexes.
- Add nonunique `books.authorId` index using an idempotent deployment script.
- Retain automatic unique `authors._id`; no speculative unique name/email index.
- Keep existing author substring search unchanged; the new ID index is not
  claimed to accelerate unanchored case-insensitive regex.
- Create collections/indexes before transactional traffic and validate index
  definitions on existing volumes. Do not depend solely on fresh-volume init.
- Existing Book documents remain unchanged until explicitly reviewed linking.
- Rollback leaves additive collections/fields intact; no automatic unlink/delete.

## Privacy and retention

Author email and birth date are returned only under deployment-approved access.
Do not include profile values in logs, migration progress, or conflict details.
Soft deletion retains the profile and is not an erasure workflow. Operators must
approve retention and access controls; this feature supplies no compliance claim.

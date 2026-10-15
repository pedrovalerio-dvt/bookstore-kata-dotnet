# Feature Specification: FEAT-007 Author Support

**Feature Branch**: No branch created; planning on `main`.

**Created**: 2026-10-15

**Status**: Implemented locally; business choices confirmed. Target-environment
acceptance remains separate from automated release evidence.

**Input**: Implement the requirements of
[joaopaulomendes-dvt/bookstore-kata-dotnet#1](https://github.com/joaopaulomendes-dvt/bookstore-kata-dotnet/issues/1),
"FEAT-007: Author Support", without breaking the released book catalogue.
This specification supplements, rather than replaces, the completed
[author search specification](../001-search-by-author/spec.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register and discover Authors (Priority: P1)

A catalogue manager registers an Author once and retrieves that profile for
reuse across books, rather than maintaining disconnected names.

**Why this priority**: Stable Author identity is the foundation for management
and relationships.

**Independent Test**: Register two Authors with the same name and retrieve each
by its distinct identity and in the Author directory.

**Acceptance Scenarios**:

1. **Given** a valid name of at most 255 characters, **When** an Author is
   registered, **Then** a distinct identity, active status, and server-managed
   creation and update times are returned.
2. **Given** optional birth date, nationality, and valid email, **When** an Author
   is registered, **Then** those supplied values are retained.
3. **Given** an existing Author name, **When** another Author with that name is
   registered, **Then** both identities remain distinct.
4. **Given** blank/oversized names or an invalid email/date, **When** registration
   is attempted, **Then** it fails explicitly without creating an Author.

### User Story 2 - Associate books with an Author (Priority: P1)

A catalogue manager selects a registered Author when creating books and retrieves
all books associated with that identity, even when another Author has the same name.

**Why this priority**: Identity-based reuse addresses disconnected author credits
without incorrectly merging namesakes.

**Independent Test**: Create two same-name Authors, link separate books to each,
and retrieve only the books belonging to the selected identity.

**Acceptance Scenarios**:

1. **Given** an active Author, **When** a book is created with that Author's
   identity, **Then** the book retains that identity and the Author's name as its credit.
2. **Given** an old client using only a free-text author credit, **When** it
   creates or searches books, **Then** its existing successful behavior is preserved.
3. **Given** an unknown or inactive Author, **When** a new link is requested,
   **Then** creation is rejected without inserting a book.
4. **Given** existing books with author credits only, **When** Author management
   is deployed, **Then** no automatic name-based matching or merging occurs.
5. **Given** an approved mapping of existing books to Authors, **When** that
   mapping is applied, **Then** only the reviewed associations change and
   unrelated book metadata is preserved.

### User Story 3 - Retire an Author without losing books (Priority: P2)

A catalogue manager soft-deletes an Author while retaining the historical
profile and catalogue relationships.

**Why this priority**: Author lifecycle must not erase books or break references.

**Independent Test**: Retire an Author with linked books and verify directory,
direct retrieval, existing books, and new-link outcomes.

**Acceptance Scenarios**:

1. **Given** an active Author with books, **When** it is retired, **Then** its
   active flag becomes false and the books and their links remain unchanged.
2. **Given** an inactive Author, **When** the default directory is viewed,
   **Then** it is absent; direct retrieval and explicit inactive inclusion retain it.
3. **Given** an already inactive Author, **When** retirement is repeated,
   **Then** it succeeds without changing the original retirement update time.
4. **Given** concurrent retirement and book linking, **When** both complete,
   **Then** they have a strict order: a committed retirement prevents a
   subsequently ordered link; an earlier committed link is retained.

### Edge Cases

- Same-name Authors are not duplicates by identity; email is not an identity key.
- Empty directories and book searches return an empty collection.
- An invalid identity is distinguished from a well-formed identity with no match.
- A request must not supply both a managed Author identity and a free-text credit.
- Inactive books remain included in book results; Author retirement is independent
  of book activity and inventory.
- Partial author-text search retains its literal case-insensitive matching.
- A failed/retried link operation must not leave a book without its intended
  relationship or publish duplicate successful-change notifications.
- Reviewed legacy mapping must reject stale, ambiguous, or conflicting assignments.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Support Author creation, listing, direct retrieval, and soft deletion.
- **FR-002**: Require a nonblank name of at most 255 characters; retain optional
  birth date, nationality, and valid email. Active defaults to true.
- **FR-003**: Assign stable unique Author identities and server-managed timestamps.
- **FR-004**: Permit different Authors with the same name and reuse identities
  across books instead of creating an Author implicitly for each book.
- **FR-005**: Permit each book to reference zero or one managed Author, while
  retaining compatibility with existing free-text credits.
- **FR-006**: Resolve a linked book's initial credit from the selected Author;
  reject supplying both identity and free text.
- **FR-007**: Support exact Author-identity filtering alongside existing category
  and author-text filters; all supplied conditions must match.
- **FR-008**: Exclude inactive Authors from the default directory; support their
  explicit inclusion and direct retrieval.
- **FR-009**: Preserve existing linked books when an Author is retired and reject
  new links to that Author, including strictly ordered concurrent operations.
- **FR-010**: Make repeated retirement successful without changing historical timestamps.
- **FR-011**: Leave legacy data unchanged unless an explicitly reviewed mapping is
  applied; do not infer identity from equal names.
- **FR-012**: Return explicit validation/not-found/conflict feedback with correlation
  information, without exposing internal exceptions or submitted profile values.
- **FR-013**: Preserve existing catalogue creation, inventory updates, retrieval,
  category rules, and released text-search behavior.
- **FR-014**: Retain the application's existing access behavior. Profile information,
  including email, requires approved deployment access protection before exposure.

### Key Entities

- **Author**: A managed person with identity, name, optional profile information,
  active status, and creation/update times.
- **Book**: An existing catalogue item with stock and metadata; optionally associated
  with one managed Author and retaining a displayed author credit.
- **Reviewed association**: An explicitly approved pairing of an existing book with
  an Author; it is not a name-matching rule.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All four Author-management operations pass the documented acceptance
  scenarios, including every invalid-input and missing-record outcome.
- **SC-002**: In a fixture with two same-name Authors and separate books,
  identity filtering returns exactly the selected Author's books.
- **SC-003**: All existing book acceptance scenarios remain successful, with no
  forced changes to existing author credits or identifiers.
- **SC-004**: In both controlled concurrent orderings, no book link is committed
  after an earlier-ordered retirement; existing books remain retrievable.
- **SC-005**: Every reviewed mapping change is attributable to an approved entry,
  and an interrupted mapping run can be resumed without duplicate associations.

## Assumptions

- Confirmed on 2026-10-15: optional Author links, retained free-text compatibility,
  reviewed legacy mapping, duplicate names allowed, retained inactive profiles
  and books, and deployment-only access protection.
- The user explicitly chose strict transactional ordering and accepted the
  required database deployment change; a check-then-insert race is not acceptable.
- No profile update, restoration, hard deletion, bulk automatic deduplication,
  biography, website, co-authorship, or frontend is included in this iteration.
  The issue mentions biography/website as motivation but excludes them from its
  proposed schema and lists no profile-update operation.
- Birth date is a calendar date, not a timestamp. No nationality vocabulary,
  future-date restriction, or email uniqueness rule is introduced.
- Proposed detail: names and nonblank optional text are preserved without
  normalization; empty optional text is invalid. Normal collection ordering
  remains unspecified and pagination remains outside this iteration.
- No user identity provider, role model, or regulatory certification is supplied.
  Deployment approval must address profile access and retention separately.

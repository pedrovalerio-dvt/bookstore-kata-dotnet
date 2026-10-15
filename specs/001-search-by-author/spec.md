# Feature Specification: Search by Author

**Feature Branch**: No branch hook is configured; implementation uses the current branch.

**Created**: 2026-10-15

**Status**: Approved scope

**Input**: Implement author search with production code, inline documentation,
unit and integration coverage, updated API/business/technical documentation,
a release specification, and a comprehensive QA checklist.

## User Scenarios & Testing

### User Story 1 - Find books from a remembered author fragment (Priority: P1)

As a catalogue user, I want to find every book whose author contains the name
fragment I remember, without needing to match capitalization.

**Why this priority**: A single author can have multiple books, and users may
not know the full stored author name.

**Independent Test**: Search for `kEnT` in a catalogue containing multiple books
by Kent Beck, a book by another author, and an authorless book.

**Acceptance Scenarios**:

1. **Given** two books by Kent Beck, **When** a user searches for `kEnT`,
   **Then** both books appear and books by unrelated or unknown authors do not.
2. **Given** no matching authors, **When** a user searches,
   **Then** the result is an empty collection, not a missing-book error.
3. **Given** an inactive matching book, **When** a user searches,
   **Then** it remains eligible for the results.

### User Story 2 - Narrow author results by category (Priority: P2)

As a catalogue user, I want to combine an author fragment and category so that
only books satisfying both conditions appear.

**Why this priority**: Authors can publish books across categories.

**Independent Test**: Search an author's fragment and Programming when that
author has books in Programming and Fiction.

**Acceptance Scenarios**:

1. **Given** matching authors in two categories, **When** the user chooses
   Programming, **Then** only matching Programming books are returned.
2. **Given** a valid author fragment and an unknown category,
   **When** the user searches, **Then** the existing category validation fails
   rather than silently broadening the result.

### Edge Cases

- Empty or whitespace-only author fragments are rejected.
- Nonblank fragments are not trimmed; leading, trailing, and internal spaces
  are literal search characters.
- Symbols such as `.`, `*`, brackets, plus signs, and backslashes are literal,
  not search expressions.
- Books with null or missing authors do not match a supplied author fragment.
- Omitting author preserves current category-only and unfiltered listing.
- Existing single-book lookup remains restricted to ID or exact title.

## Requirements

### Functional Requirements

- **FR-001**: Users MUST be able to search the catalogue by an optional author
  fragment and receive every matching book in the existing collection format.
- **FR-002**: Author matching MUST be case-insensitive substring matching.
- **FR-003**: Search text MUST be interpreted literally, not as an executable
  pattern or expression.
- **FR-004**: A supplied blank fragment MUST produce the existing structured
  invalid-input feedback.
- **FR-005**: Author and category filters MUST be combined with AND semantics.
- **FR-006**: A valid search with no matches MUST return an empty collection.
- **FR-007**: Omitting author MUST preserve current listing behavior, including
  inactive records and category validation.
- **FR-008**: Authorless books MUST remain available in unfiltered/category-only
  listings but MUST NOT match a nonblank author search.
- **FR-009**: This release MUST NOT change creation, updates, title/ID lookup,
  ordering guarantees, pagination, or access-control policy.

### Key Entities

- **Book**: Existing catalogue record with optional author text and a category.
  No new persistent entity or property is introduced.
- **Author fragment**: User-provided nonblank literal text used to select books;
  it is not an author identifier or a separately managed author record.

## Success Criteria

### Measurable Outcomes

- **SC-001**: In the acceptance catalogue, 100% of books containing the requested
  author fragment are returned and zero unrelated/authorless books are included.
- **SC-002**: Changing capitalization of the same tested fragment does not
  change the result set.
- **SC-003**: Every combined-filter result satisfies both author and category;
  zero books satisfying only one condition are returned.
- **SC-004**: Empty, whitespace-only, and literal-symbol acceptance cases all
  produce the documented result, without changing stored catalogue data.

## Assumptions

- The Product Owner selected collection search, combined category filtering,
  and case-insensitive partial author matching during clarification.
- Search uses the existing collection operation; single-book author lookup
  remains unsupported.
- Matching does not promise accent folding, transliteration, tokenization,
  fuzzy spelling correction, or Unicode normalization.
- No new frontend, authentication system, author-maintenance workflow, or
  data-migration job is in scope.
- No pagination or search performance SLA is introduced.

# Bookstore technical architecture

## Scope, method, and confidence

This is an as-is description based on all tracked application source files,
the Maven build, container configuration, MongoDB initializer, and existing test.
It does not propose or implement a migration.

The repository contains **Java, Jersey/JAX-RS, Grizzly, and MongoDB**, not a .NET
service. There is no tracked web frontend, HTTP API client adapter, external API
integration, or frontend-to-backend configuration to document. An external
consumer is shown below only as the caller of this API; its implementation is
outside the available evidence.

Application branches, field types, persistence commands, and explicit status
codes are directly established by source. JSON-B defaults and automatic JAX-RS
responses are framework behavior, not custom application contracts. No running
deployment or wire-level tests were used to confirm provider-specific date
formatting, coercion, null-body handling, or error representations.

Related artifacts:
[OpenAPI 3.0.3 specification](openapi.yaml) and
[functional guide](functional-guide.md).

## High-level architecture

```text
External consumer (implementation not in this repository)
    |
    | HTTP, port 8080; /api/books...
    v
Grizzly HTTP server
    |
    v
Jersey resource dispatch + JSON-B serialization/binding
    |
    v
BookController
    |
    | direct synchronous Java calls
    v
BookService
    |                         |
    | MongoDB synchronous     | in-memory category lookup
    | driver, port 27017      v
    |                    Category.STATIC_CATEGORIES
    v
MongoDB: bookstore.books

Mongo initialization script -> bookstore.categories
                              (not read by the API)
```

This is one API process plus one database service, not separate web and backend
applications. Controller, service, and models are packages within the same JAR.
There is no repository abstraction, message bus, background worker, cache,
domain-event infrastructure, or separate application/domain project.

### Technology and build inventory

| Concern | Implemented choice |
|---|---|
| Maven coordinates | `com.bookstore:bookstore-java:0.1.0-SNAPSHOT`, JAR packaging |
| Language target | Java 11 source and target in `pom.xml` |
| HTTP API | Jakarta REST API 3.1.0 |
| Server/framework | Jersey 3.1.0, Grizzly HTTP container, Jersey HK2 |
| JSON | `jersey-media-json-binding`, registered `JsonBindingFeature`; no custom JSON-B configuration |
| Storage | MongoDB synchronous Java driver 4.11.0 |
| Database container | `mongo:7.0` |
| Unit testing | JUnit Jupiter 5.9.0 and Mockito 5.12.0 |
| Build plugins | Compiler 3.8.1, Surefire 3.0.0-M7, JaCoCo 0.8.10, Shade 3.4.1 |
| Container build/runtime | Maven 3.8.6 with Temurin 11; Temurin 11 JRE Jammy |

The Shade plugin creates an executable dependency-containing JAR, sets
`com.bookstore.BookstoreApp` as `Main-Class`, and merges service descriptors.
JaCoCo attaches an agent and generates a report during `verify`; no coverage
threshold is configured.

`COMPANY_GUIDELINES.md` describes .NET 10, Minimal APIs, Problem Details, and
other target standards. Those statements are not evidence that the legacy
Java application implements them. This document and the OpenAPI artifact
describe the code that actually exists.

## Startup and configuration

`BookstoreApp.main` builds a `ResourceConfig`, explicitly registers
`JsonBindingFeature` and `BookController`, and starts Grizzly with base URI
`http://0.0.0.0:8080/api/`. The effective application prefix comes from this
factory URI; `@ApplicationPath("/api")` is also present on the bootstrap class.
There is no additional `/api` prefix to append.

The server binds all interfaces on port 8080. The entry point prints a startup
message. No shutdown hook, configurable listener URI, TLS setup, CORS policy,
custom exception mapper, authentication filter, or authorization policy is
registered in the application source.

| Setting | Effective source of value |
|---|---|
| HTTP host/port/prefix | Hardcoded in `BookstoreApp`: `0.0.0.0:8080/api/` |
| MongoDB URI | Hardcoded in `BookService`: `mongodb://mongodb:27017` |
| Database | Hardcoded `bookstore` |
| Book collection | Hardcoded `books` |
| `MONGODB_URI` environment variable | Set by Compose but **never read** by application code |
| Category definitions | Static Java list, not database configuration |

Changing the Compose environment variable alone does not change the database
target. A directly launched API outside Compose still needs DNS/host resolution
for `mongodb`; merely running MongoDB at `localhost` is insufficient.

## HTTP interface and low-level dispatch

All explicit resource methods are on `BookController`, whose class-level path
is `/books` and whose declared output media type is `application/json`.
POST and PUT explicitly consume JSON. There are no path parameters or
versioned routes.

| HTTP method | Effective route | Handler | Explicit normal branches |
|---|---|---|---|
| GET | `/api/books/health` | `health()` | `200`, empty |
| GET | `/api/books` | `listBook(category)` | `200`, array |
| GET | `/api/books/single` | `getBook(id, name, author)` | `200`, Book or no entity |
| POST | `/api/books` | `createBook(request)` | `201`, Book; `404`, empty if title null |
| PUT | `/api/books` | `updateBook(request)` | `201`, constructed Book; `404`, empty if ID null |

There is no DELETE handler, `/books/{id}` route, author-search implementation,
categories route, runtime OpenAPI endpoint, or Swagger UI. The YAML in this
change is a static documentation artifact, not a new server endpoint.

JAX-RS supplies implicit HEAD behavior for GET resources and automatic OPTIONS
handling. Routing/media negotiation can produce 404, 405 (with `Allow`), 406,
and 415 outside controller branches. These are framework behavior rather than
additional explicitly implemented handlers.

### Object construction and dependency boundaries

The default controller constructor instantiates `new BookService()` directly.
A second constructor accepts a `BookService` and is used by the unit test.
Although HK2 and Jakarta Inject dependencies are present, no custom service
binding or application-managed MongoClient singleton is configured.

`BookService` combines business decisions, document mapping, query execution,
and connection management. All database operations are synchronous.

## Request and response models

### CreateBookDTO

| JSON property | Java type | Omitted-field value / interpretation |
|---|---|---|
| `title` | `String` | Null; controller returns 404 |
| `author` | `String` | Null |
| `categoryId` | `String` | Null; model category remains 0 |
| `quantity` | `int` | 0 |
| `description` | `String` | Null |
| `language` | `String` | Null |
| `active` | `boolean` | **True**, initialized in DTO |
| `inactiveDate` | `java.util.Date` | Null |
| `publisher` | `String` | Null |
| `publisherDate` | `java.util.Date` | Null |

No Bean Validation annotations, explicit string normalization, length limits,
or range checks are applied. `categoryId` is misleadingly named: non-null
values must be exact category names for successful service execution.

### UpdateBookDTO

| JSON property | Java type | Omitted-field value / interpretation |
|---|---|---|
| `id` | `String` | Null; controller returns 404 |
| `quantity` | `int` | 0; overwrites stored quantity |
| `active` | `boolean` | False; overwrites status and stamps inactiveDate |

The private identifier field is named `Id`, but its `getId`/`setId` bean
accessors expose JSON property `id`. The DTO has no other mutable metadata.
Unknown-field and explicit-null-to-primitive handling are not customized;
consumers should not rely on a documented application-level policy for them.

### Book response

`Book` exposes `id`, `title`, `author`, `description`, `language`, and `publisher`
as strings; `categoryId` and `quantity` as primitive integers; `active` as a
primitive boolean; and `inactiveDate` and `publisherDate` as `java.util.Date`.

The registered JSON-B feature uses default bean mappings. Default JSON-B
normally omits null reference properties; the OpenAPI response therefore does
not require optional string/date properties. Date-time strings in the YAML
represent the default JSON-B date mapping, not custom parsing or precision
guarantees established by tests. There is no custom timezone/date format or
response envelope.

An explicit `Response.status(OK).entity(null).build()` has no entity: the
single-book miss is documented as an empty `200`, not a Book-shaped error,
JSON null, or `404`. Health and the two application `404` branches also build
responses with no entity.

## Service algorithms

### List and category filter

`getBookList(category)` resolves a non-null string by exact equality against
`Category.STATIC_CATEGORIES`. A recognized positive category ID triggers
`listBooks()` and an in-memory filter by numeric book category. Missing or
unrecognized category input calls `listBooks()` without filtering.

`listBooks()` creates a MongoClient and calls `find()` without a filter or
sort, maps every document into an `ArrayList<Book>`, closes the client on the
normal path, and returns the list. There is no pagination or active-status
predicate. Complexity is O(N) reads and O(N) memory even for a category filter.

### Single-book lookup

`getBook(id, name)` initializes a null result.

1. If name is non-null, load all books and find the first
   `b.getTitle().equals(name)` match.
2. If ID is non-null, load all books again and test `b.getId() == Id`.
   This is reference identity, not string value equality.
3. Return the retained result, possibly null.

When both inputs are present this can read the full collection twice. An ID
miss does not erase a name result; an ID match overrides it. No ObjectId
parsing is performed for this lookup. A null stored title can throw during
name matching. Before calling the service, the controller rejects any non-null
author parameter by throwing `RuntimeException`.

### Shared create/update method

Both mutations call `upsertBook(...)`, but the method name does **not** mean
MongoDB upsert behavior:

1. Construct a new `Book`. Use a nonblank supplied ID or generate a new
   ObjectId hexadecimal string.
2. Copy all supplied arguments. A non-null category invokes
   `validateCategory(name).getId()`; unmatched names return null and thus throw.
3. Create a new MongoClient and select `bookstore.books`.
4. If the original ID argument was null, insert a complete document.
5. Otherwise, set inactiveDate to server time when inactive, then run
   `updateOne` with a `$set` document.
6. Close the client on the normal path and return the constructed Book,
   without reloading storage.

The create controller supplies all create fields and a null ID. The update
controller supplies only ID, quantity, and active; all other service arguments
are null. Consequently the update response has null metadata and category 0,
even though the stored metadata is untouched.

The update filter and command are:

```javascript
// Values below are explanatory placeholders, not executable application code.
filter = { _id: ObjectId(bookId) }
update = {
  $set: {
    _id: ObjectId(bookId),
    quantity: submittedQuantity,
    active: submittedActive,
    inactiveDate: submittedActive ? null : serverCurrentTime
  }
}
```

The `$set` includes the same `_id` used by the filter. There is no
`UpdateOptions.upsert(true)` and no inspection of `UpdateResult`.
For a well-formed unmatched ID, a normal zero-match update still returns 201.
Blank ID input is non-null, so it generates a new ID but stays on this
update-only branch. A malformed nonblank ID throws during ObjectId conversion.

Each MongoDB single-document update is a database operation; there is no
multi-document transaction, optimistic concurrency version, compare-and-set,
stock increment, idempotency mechanism, or business audit record. Concurrent
quantity assignments can overwrite each other.

## Persistence model

### `bookstore.books`

| BSON field | Written type | Read mapping / legacy fallback |
|---|---|---|
| `_id` | ObjectId | Required by mapping; converted to hexadecimal string |
| `title` | String or null | `getString`, no fallback |
| `author` | String or null | `getString`, no fallback |
| `categoryId` | Integer | Missing/null becomes 0 |
| `quantity` | Integer | Missing/null becomes 0 |
| `description` | String or null | `getString`, no fallback |
| `language` | String or null | `getString`, no fallback |
| `active` | Boolean | Missing uses false via `getBoolean("active", false)` |
| `inactiveDate` | BSON date or null | `getDate` |
| `publisher` | String or null | `getString`, no fallback |
| `publisherDate` | BSON date or null | `getDate` |

Creation writes every field, including nullable ones. Updates write only
`_id`, quantity, active, and inactiveDate through `$set`. No explicit books
collection initializer, schema validator, secondary index, or title-uniqueness
constraint is defined in repository code. MongoDB supplies its normal `_id`
index and can create the collection on first insertion.

The table's missing-field fallbacks do not provide general schema tolerance.
Wrong BSON types can throw from typed getters; missing/non-ObjectId `_id` can
also fail. Explicit-null `active` is not protected by a separate null guard
before unboxing to primitive boolean.

### `bookstore.categories`

`mongo-init/01-init-bookstore.js` creates `categories`, inserts the six ID/name
pairs, and creates a unique index on `id`. It does not seed books. The API never
reads this collection; its category authority is the duplicated static Java
list. Database edits do not affect accepted create/filter names.

The Mongo image executes initialization scripts during initialization of a fresh
data directory. An existing named data volume is not a mechanism for rerunning
changed seed data.

## Deployment and integrations

Compose defines `api` and `mongodb` on the `bookstore-network` bridge network.
The API connects to the MongoDB service name `mongodb` on port 27017. Host
port mappings expose API port 8080 and MongoDB port 27017.

MongoDB persists `/data/db` in named volume `mongodb-data`; the initializer is
bind-mounted read-only into `/docker-entrypoint-initdb.d/`. The API has no
persistent filesystem volume.

The Dockerfile uses a two-stage build:

1. Resolve Maven dependencies in the build image, copy source, and run
   `mvn package -DskipTests -q`.
2. Copy the shaded JAR into the Java 11 runtime image as `/app/app.jar` and
   execute `java -jar app.jar`.

Compose `depends_on` establishes startup dependency ordering but has no
database health condition. Neither container defines an application healthcheck.
The `/books/health` resource does not query MongoDB, so it is not a readiness
probe for database-backed operations.

No credentials, MongoDB authentication setup, TLS, external API endpoint,
resilience policy, or application retry/circuit-breaker configuration is
defined. Driver-internal defaults must not be mistaken for explicit
application retry guarantees.

### Build/run commands

From the repository root with Docker Compose and its daemon available:

```powershell
docker compose up --build
```

The documented host API base is `http://localhost:8080/api`.
No deployed/staging URL is available in repository configuration.

With a compatible Java runtime and Maven installed, the build lifecycle is:

```powershell
mvn test
mvn verify
java -jar .\target\bookstore-java-0.1.0-SNAPSHOT.jar
```

Direct execution still uses the hardcoded `mongodb` hostname. `mvn test` runs
the existing mocked controller test and does not establish database connectivity.
The commands describe repository configuration; they are not a claim that a
deployment was launched as part of this documentation work.

## Errors, observability, and resource lifecycle

There is no custom exception mapper or common error DTO. The controller's
explicit failures are empty 404 responses; runtime, mapping, parsing, and
database exceptions are delegated to Jersey/Grizzly. Framework errors must
not be documented as RFC 7807 or assigned invented JSON fields.

| Origin | Case | Contract/confidence |
|---|---|---|
| Controller | Null title / null update ID | Explicit 404, no entity |
| Controller | Non-null author filter | Unhandled RuntimeException; normally 500 |
| Service | Unknown non-null create category | Null dereference; normally 500 |
| Service | Malformed nonblank update ID | ObjectId conversion exception; normally 500 |
| Service/driver | MongoDB unavailable, invalid document types | Unhandled failures; normally 500 |
| JSON provider | Malformed/unbindable body | Framework-controlled, commonly 400; individual cases not wire-verified |
| JAX-RS dispatch | Media negotiation / unmatched path / unsupported method | Framework-controlled 406/415/404/405 |

The OpenAPI artifact defines reusable error responses without inventing an
error-body schema. Empty application branches are distinguished from unknown
framework body formats. No application 401, 403, 409, or structured validation
422 behavior is present.

Only a startup `System.out.println` is explicitly implemented for application
logging. There is no application-defined structured request logging, trace ID,
metrics export, distributed tracing, or audit log. Framework/driver logging
may exist independently.

Clients are created per storage call instead of sharing a long-lived pool.
Normal paths close them explicitly; exceptions before `close()` can leak
resources because there is no try-with-resources or finally block. There is
also no explicit graceful HTTP shutdown.

## Project structure

```text
repository root
|-- pom.xml                         Maven dependencies, test, coverage, shading
|-- Dockerfile                      Build and runtime images
|-- docker-compose.yml              API/MongoDB network, ports, persistence
|-- README.md                       Entry point and documentation links
|-- COMPANY_GUIDELINES.md            Target company standards, not current stack
|-- mongo-init
|   `-- 01-init-bookstore.js         Category seed collection and unique index
|-- src
|   |-- main/java/com/bookstore
|   |   |-- BookstoreApp.java        Grizzly/Jersey startup
|   |   |-- controller
|   |   |   |-- BookController.java  Five explicit resource methods
|   |   |   |-- CreateBookDTO.java   Creation input bean
|   |   |   `-- UpdateBookDTO.java   ID/quantity/active input bean
|   |   |-- model
|   |   |   |-- Book.java            Mutable response/service data bean
|   |   |   `-- Category.java        Category bean and static vocabulary
|   |   `-- service
|   |       `-- BookService.java     Rules, MongoDB calls, document mapping
|   `-- test/java/com/bookstore/controller
|       `-- BookControllerTest.java  One mocked create-path unit test
|-- docs
|   |-- openapi.yaml                Static as-is OpenAPI 3.0.3 contract
|   |-- functional-guide.md         Product/business-facing behavior
|   `-- technical-architecture.md   This document
|-- .github / .agents               Assistant instructions, skills, hooks
|-- .specify                        Specification tooling/templates
`-- .vscode / .mcp.json             Development-tool configuration
```

The developer-tool directories are not application runtime components.
There are no tracked application resources, frontend assets, deployment
environments, or CI build/test workflow files in the inspected repository.

## Existing assurance and important limitations

`BookControllerTest.testCreateBookSuccess` constructs a mocked BookService,
submits a populated CreateBookDTO, verifies the exact forwarded service
arguments, and asserts status 201 and response object identity. It does not
start Jersey, exercise JSON serialization, or connect to MongoDB.

There are no existing service, HTTP integration, database integration, error-path,
lookup, update, category-filter, or date-format tests. The container build skips
tests. A green mocked controller test alone would not establish the OpenAPI
wire contract or the correctness of persistence behavior.

Key implementation limitations are the ID-reference comparison, unimplemented
author filter, full-collection scans, silent zero-match update success,
omitted-field resets, partial update response, category failure asymmetry,
hardcoded configuration, unmanaged exceptional resource cleanup, and absent
application-level access control/error contract. These are documented current
conditions, not changes made by this documentation work.

## Source reference index

| Evidence | Location |
|---|---|
| Listener URI and resource registrations | `src/main/java/com/bookstore/BookstoreApp.java:12-24` |
| Routes, media types, author failure | `src/main/java/com/bookstore/controller/BookController.java:9-46` |
| Update forwarding and status branches | `src/main/java/com/bookstore/controller/BookController.java:48-69` |
| Create forwarding and status branches | `src/main/java/com/bookstore/controller/BookController.java:71-93` |
| Input model definitions | `src/main/java/com/bookstore/controller/CreateBookDTO.java:5-100`; `src/main/java/com/bookstore/controller/UpdateBookDTO.java:3-33` |
| Response model | `src/main/java/com/bookstore/model/Book.java:5-110` |
| Static category definitions | `src/main/java/com/bookstore/model/Category.java:10-19` |
| Effective MongoDB configuration | `src/main/java/com/bookstore/service/BookService.java:15-17` |
| List, filter, and BSON mapping | `src/main/java/com/bookstore/service/BookService.java:23-78` |
| ID generation, insert, update, category resolution | `src/main/java/com/bookstore/service/BookService.java:81-147` |
| Lookup algorithm and ID defect | `src/main/java/com/bookstore/service/BookService.java:149-175` |
| Existing test | `src/test/java/com/bookstore/controller/BookControllerTest.java:18-84` |
| Dependencies and lifecycle plugins | `pom.xml:1-132` |
| Image build and entry point | `Dockerfile:1-14` |
| Compose services, ports, network, mounts | `docker-compose.yml:1-32` |
| Category seed and index | `mongo-init/01-init-bookstore.js:1-16` |

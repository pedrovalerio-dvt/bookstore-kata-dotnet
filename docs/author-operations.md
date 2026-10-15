# Author Support operations runbook

## Access and topology prerequisites

Author linking requires MongoDB transactions. Standalone MongoDB is no longer
sufficient for linked creation or retirement. Local Compose runs one member
named `rs0`, advertised as `mongodb:27017`; it is not high availability.
Production operators must supply approved replication, credentials, TLS,
backups, retention, monitoring, and restricted access to Author profile/email data.
The application still has no authentication provider.

The Compose bootstrap initializes only an uninitialized set, rejects unexpected
set/member configuration, waits for a writable primary, seeds categories only
if absent, and applies indexes. It does not delete data, forcibly reconfigure
a set, or create managed Authors from legacy name strings.

## Fresh local development

```powershell
docker compose up --build -d
docker compose ps -a
docker compose logs mongodb-bootstrap
```

Require healthy MongoDB and bootstrap exit code 0 before API traffic.
The API container uses `mongodb://mongodb:27017/?replicaSet=rs0`.
Host-side development must bypass the Docker-only advertised member hostname:

```powershell
$env:Mongo__ConnectionString = "mongodb://localhost:27017/?directConnection=true&replicaSet=rs0"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project .\src\Bookstore.Api -- --urls http://localhost:8080
```

For only MongoDB plus a host API, start both `mongodb` and `mongodb-bootstrap`.
Do not start the API until bootstrap completes. `/api/books/health` proves
HTTP liveness only, not transaction capability.

## Existing standalone catalogue conversion

These are local Compose commands. Production conversion must follow an approved
maintenance procedure for the actual topology, authentication, and backup system.

1. Freeze API writes and maintenance tools. Record API image/commit and the
   exact existing database volume. Do not change the Compose project name
   when upgrading: that would select a different volume.
2. Take an approved consistent backup and verify restoration in isolation.
   For an unauthenticated local container, an example is:

   ```powershell
   New-Item -ItemType Directory -Force .\artifacts\author-upgrade | Out-Null
   docker compose exec mongodb mongodump --db bookstore --archive=/tmp/bookstore-author-upgrade.archive --gzip
   # Use the single ID printed by this command as <mongo-container-id> below.
   docker compose ps -q mongodb
   docker cp <mongo-container-id>:/tmp/bookstore-author-upgrade.archive .\artifacts\author-upgrade\bookstore.archive
   ```

   Keep backup files access-restricted and outside version control. Inspect
   command exit codes; do not proceed on backup/restore failure. Use a separate
   isolated MongoDB instance for the restore rehearsal. Never overwrite the
   source database as a way to test restoration.
3. Stop local API writes and cleanly stop MongoDB:

   ```powershell
   docker compose stop api mongodb
   ```

4. Deploy the new Compose definition using the same project and data volume:

   ```powershell
   docker compose up -d mongodb mongodb-bootstrap
   docker compose logs mongodb-bootstrap
   docker compose ps -a
   ```

5. Verify the bootstrap completed successfully, the set has an elected writable
   primary, and stored book counts/IDs match the backup manifest:

   ```powershell
   docker compose exec mongodb mongosh --quiet --eval 'printjson(db.adminCommand({hello:1}))'
   docker compose exec mongodb mongosh bookstore --quiet --eval 'printjson(db.books.getIndexes())'
   ```

   Require `setName: "rs0"`, `isWritablePrimary: true`, retained catalogue data,
   and indexes `ix_books_title`, `ix_books_category`, and `ix_books_authorId`.
   An unexpected existing set configuration is an operator-review failure,
   not a reason to force reconfiguration.
6. Run an isolated transactional smoke test, then deploy:

   ```powershell
   docker compose up --build -d api
   ```

   Register a synthetic Author, create a linked Book, retire the Author,
   confirm existing-book visibility and 409 on new links, and capture trace
   evidence. Perform this in a QA database, not by inserting test profiles
   into the production catalogue.
7. Resume traffic only after quality gates, data checks, access review, and
   deployment approval. Never use `docker compose down -v` during conversion.

Reapply idempotent indexes/bootstrap with `docker compose run --rm mongodb-bootstrap`.
Inspect conflicts instead of deleting existing indexes or volumes.

## Reviewed legacy association manifest

Create the target Authors explicitly and review identities; equal names do not
establish identity. Use an access-restricted UTF-8 JSON file without a byte-order
mark. Every entry must have exactly these fields:

```json
[
  {
    "bookId": "507f1f77bcf86cd799439012",
    "expectedAuthor": "Historical spelling",
    "expectedAuthorId": null,
    "authorId": "507f1f77bcf86cd799439011"
  }
]
```

`expectedAuthor` is the exact existing credit or null for null/missing.
`expectedAuthorId` must be null: this tool adds reviewed links, not reassignments.
Duplicate book entries, malformed IDs, and extra/missing properties are rejected
before writes. Mapping preserves the original author credit and all unrelated
book fields, not the current managed Author spelling.

Dry-run is the default:

```powershell
.\scripts\Link-LegacyAuthors.ps1 -ManifestPath .\approved-author-links.json
.\scripts\Link-LegacyAuthors.ps1 -ManifestPath .\approved-author-links.json -Apply
```

Optional `-ContainerName` selects one explicitly named local MongoDB container;
`-DatabaseName` defaults to `bookstore`. The wrapper is for the unauthenticated
local `rs0` container topology only. For authenticated production, operators
must execute the reviewed JS through their approved secret-handling procedure,
not add credentials to this wrapper, logs, command history, or committed manifests.

The wrapper copies the script and manifest into a unique mode-700 directory,
sets files to mode 600, and removes only those owned files on normal completion/
failure. If the process is forcibly killed, an operator must remove that exact
directory's files securely. Original approved manifests remain operator-owned.

Dry-run uses snapshot reads but writes nothing; apply revalidates. Each entry
commits independently, guards the active Author by incrementing its revision,
and conditionally adds the Book reference in one transaction. Safe JSON progress
includes entry number, book ID, and status, never credits or profile values.
`MAPPING_FAILED` denotes an unexpected database/script failure; inspect server
diagnostics under approved access rather than assuming success.

On a stale credit, conflicting association, missing/inactive Author, or database
failure, the tool exits nonzero. Earlier committed entries remain. Correct the
approved input/data and resume: an identical existing link with matching credit
is skipped without revision changes, even if the Author was later retired.
A failed Book write rolls back its Author guard too.

## Rollback

Freeze Author writes and mapping before rollback. Redeploy the previous API
image and disable new Author/identity-filter consumers. Retain the replica set,
added collection, references, indexes, and volume; the old API ignores
`authorId` and inventory updates preserve it.

Do not automatically return to standalone, delete Author profiles, unlink books,
or restore a backup over a live database. Data reversal requires a separately
reviewed plan and expected-state checks. A backup restore is disaster recovery,
not the normal additive-feature rollback.

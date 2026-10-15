// Invoke with the protected manifest path in BOOKSTORE_MAPPING_MANIFEST; apply is explicit.
const fs = require("fs");
function fail(code) {
  const error = new Error(code);
  error.mappingCode = code;
  throw error;
}
function validateManifest(value) {
  if (!Array.isArray(value) || value.length === 0) fail("INVALID_MANIFEST");
  const ids = new Set();
  return value.map(entry => {
    if (entry === null || typeof entry !== "object" || Array.isArray(entry) ||
        Object.keys(entry).sort().join(",") !== "authorId,bookId,expectedAuthor,expectedAuthorId" ||
        typeof entry.bookId !== "string" || !/^[a-fA-F0-9]{24}$/.test(entry.bookId) ||
        typeof entry.authorId !== "string" || !/^[a-fA-F0-9]{24}$/.test(entry.authorId) ||
        !(entry.expectedAuthor === null || typeof entry.expectedAuthor === "string") ||
        entry.expectedAuthorId !== null) fail("INVALID_MANIFEST");
    const bookId = entry.bookId.toLowerCase();
    if (ids.has(bookId)) fail("DUPLICATE_BOOK");
    ids.add(bookId);
    return { ...entry, bookId: ObjectId(bookId), authorId: ObjectId(entry.authorId) };
  });
}

(async function runMapping() {
  let session;
  let entryNumber = 0;
  try {
    const mode = process.env.BOOKSTORE_MAPPING_MODE || "dry-run";
    if (!["dry-run", "apply"].includes(mode)) fail("INVALID_MODE");
    const manifest = validateManifest(JSON.parse(fs.readFileSync(process.env.BOOKSTORE_MAPPING_MANIFEST, "utf8")));
    const hello = await db.adminCommand({ hello: 1 });
    if (!hello.setName || !hello.isWritablePrimary) fail("PRIMARY_REQUIRED");
    session = db.getMongo().startSession();
    const database = session.getDatabase(db.getName());
    for (const entry of manifest) {
      entryNumber++;
      let result;
      await session.withTransaction(async () => {
        result = undefined;
        const book = await database.books.findOne({ _id: entry.bookId });
        if (!book) fail("BOOK_NOT_FOUND");
        if ((book.author ?? null) !== entry.expectedAuthor) fail("STALE_CREDIT");
        if (book.authorId instanceof ObjectId && book.authorId.equals(entry.authorId)) {
          result = "skipped";
          return;
        }
        if (book.authorId !== undefined && book.authorId !== null) fail("ASSOCIATION_CONFLICT");
        const author = await database.authors.findOne({ _id: entry.authorId });
        if (!author) fail("AUTHOR_NOT_FOUND");
        if (author.active !== true) fail("AUTHOR_INACTIVE");
        if (mode === "dry-run") {
          result = "validated";
          return;
        }
        const guard = await database.authors.updateOne(
          { _id: entry.authorId, active: true },
          { $inc: { linkRevision: Long.fromNumber(1) } });
        if (guard.matchedCount !== 1 || guard.modifiedCount !== 1) fail("AUTHOR_GUARD_CONFLICT");
        const link = await database.books.updateOne(
          { _id: entry.bookId, author: entry.expectedAuthor, authorId: null },
          { $set: { authorId: entry.authorId } });
        if (link.matchedCount !== 1 || link.modifiedCount !== 1) fail("BOOK_WRITE_CONFLICT");
        result = "linked";
      }, {
        readConcern: { level: "snapshot" },
        writeConcern: { w: "majority" },
        readPreference: "primary"
      });
      // Never log in the retryable callback, or include profile/credit/manifest values.
      print(JSON.stringify({ entry: entryNumber, bookId: entry.bookId.toHexString(), status: result }));
    }
    print(JSON.stringify({ status: "complete", mode, entries: manifest.length }));
  } catch (error) {
    print(JSON.stringify({ entry: entryNumber, status: "failed", code: error.mappingCode || "MAPPING_FAILED" }));
    if (session) await session.endSession();
      quit(1);
    }
    if (session) await session.endSession();
  })();

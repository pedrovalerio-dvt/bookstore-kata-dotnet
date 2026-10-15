// Only local single-member topology is bootstrapped; existing configurations are never rewritten.
let primaryReady = false;
for (let attempt = 0; attempt < 120; attempt++) {
  try {
    const status = db.adminCommand({ replSetGetStatus: 1 });
    if (status.ok === 1) {
      const config = db.adminCommand({ replSetGetConfig: 1 }).config;
      if (config._id !== "rs0" || config.members.length !== 1 ||
          config.members[0].host !== "mongodb:27017") {
        throw new Error("BOOKSTORE_TOPOLOGY_MISMATCH");
      }
      if (db.adminCommand({ hello: 1 }).isWritablePrimary) {
        primaryReady = true;
        break;
      }
    }
  } catch (error) {
    if (error.message === "BOOKSTORE_TOPOLOGY_MISMATCH") {
      print("Bootstrap refused: existing replica topology requires operator review.");
      quit(1);
    }
    if (error.code === 94) {
      try {
        db.adminCommand({
          replSetInitiate: { _id: "rs0", members: [{ _id: 0, host: "mongodb:27017" }] }
        });
      } catch (initError) {
        // Another bootstrap may have won; only AlreadyInitialized is harmless.
        if (initError.code !== 23) {
          print("Bootstrap initiation failed; review MongoDB diagnostics.");
          quit(1);
        }
      }
    } else if (![6, 7, 76, 89, 91, 133, 13435, 11600, 11602].includes(error.code) &&
               !["MongoNetworkError", "MongoServerSelectionError"].includes(error.name)) {
      print("Bootstrap failed; review MongoDB diagnostics.");
      quit(1);
    }
  }
  sleep(1000);
}
if (!primaryReady) {
  print("Bootstrap timed out waiting for an rs0 writable primary.");
  quit(1);
}
if (!db.getSiblingDB("bookstore").getCollectionNames().includes("categories")) {
  load("/opt/bookstore/01-init-bookstore.js");
}
load("/opt/bookstore/02-index-books.js");
load("/opt/bookstore/03-index-authors.js");
print("Replica primary and Author indexes ready.");

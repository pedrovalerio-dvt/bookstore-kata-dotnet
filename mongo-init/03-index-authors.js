const authorDatabase = db.getSiblingDB("bookstore");
if (!authorDatabase.getCollectionNames().includes("authors")) {
  authorDatabase.createCollection("authors");
}
authorDatabase.books.createIndex({ authorId: 1 }, { name: "ix_books_authorId", unique: false });

const booksDb = db.getSiblingDB("bookstore");
booksDb.books.createIndex({ title: 1 }, { name: "ix_books_title" });
booksDb.books.createIndex({ categoryId: 1 }, { name: "ix_books_category" });

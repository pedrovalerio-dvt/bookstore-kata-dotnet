# Bookstore catalogue

The catalogue describes books, their stock, and the authors credited with them.
Author-management terms below describe the agreed FEAT-007 target model.

## Language

**Author**:
A separately identified person credited with writing books. Different Authors
may share the same name.
_Avoid_: Unique author name, author string as identity

**Author credit**:
The name displayed on a book. A legacy credit need not identify a managed Author.
_Avoid_: Author identity

**Linked book**:
A book associated with one managed Author; one Author may have many linked books.
_Avoid_: Co-authored book, automatically matched author

**Inactive Author**:
An Author retained for historical reference but unavailable for new book links.
Existing linked books remain in the catalogue.
_Avoid_: Erased author, deleted books

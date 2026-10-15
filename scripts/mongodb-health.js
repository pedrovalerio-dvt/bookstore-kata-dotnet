try {
  const hello = db.adminCommand({ hello: 1 });
  quit(hello.ok === 1 && hello.setName === "rs0" && hello.isWritablePrimary ? 0 : 1);
} catch {
  quit(1);
}

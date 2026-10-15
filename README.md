# Bookstore

Book catalogue and inventory API using .NET 10 Minimal APIs and MongoDB.
Create books, find by ID or exact title, search by partial author name,
filter by category, and update stock
and active status. Existing MongoDB ObjectIds and document field names are retained.

## Prerequisites

- .NET 10 SDK (selected by `global.json`).
- Docker with Linux containers and Docker Compose.
- PowerShell 7 for the full coverage-gate script.

## Getting started

```powershell
docker compose up --build
```

The API listens at `http://localhost:8080/api`. Compose binds published ports to
loopback and retains MongoDB data in `mongodb-data`. Do not remove that volume
when migrating an existing catalogue. This is a local development deployment,
not an authenticated production topology.

For local .NET execution with only MongoDB in Docker:

```powershell
docker compose up -d mongodb
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Mongo__ConnectionString = "mongodb://localhost:27017"
dotnet run --project .\src\Bookstore.Api -- --urls http://localhost:8080
```

`Mongo__DatabaseName` defaults to `bookstore`. Connection strings must not be
committed with credentials. Existing databases require the idempotent index
script described in the architecture document.

## API documentation

- [OpenAPI YAML](docs/openapi.yaml) and live JSON at
  `http://localhost:8080/openapi/v1.json`.
- Scalar UI at `http://localhost:8080/scalar/v1` in **Development only**.
  Compose defaults to Production; set `ASPNETCORE_ENVIRONMENT=Development`
  explicitly in the API container for a local UI.
- [Functional guide](docs/functional-guide.md).
- [Technical architecture and migration changes](docs/technical-architecture.md).
- [Historical Java baseline](docs/legacy/technical-architecture.md).
- [Search by Author release specification](specs/001-search-by-author/release.md)
  and [QA checklist](specs/001-search-by-author/checklists/qa.md).

No staging endpoint is configured in this repository.

Author search example: `GET /api/books?author=beck&category=Programming`.
Author matching is a case-insensitive literal substring; both filters must
match when combined. Blank author input returns `400`; no matches returns `[]`.
The single-book route still accepts only ID or exact title.

## Testing and coverage

```powershell
.\scripts\Test-Coverage.ps1
```

This runs xUnit unit tests, WebApplicationFactory HTTP tests, and isolated
MongoDB Testcontainers tests. Docker must be running. `dotnet test` also enforces
at least **70% aggregate line coverage**; the script additionally requires
**85% business-logic coverage**. No production assembly is excluded from
instrumentation. Reports are written to `artifacts\coverage\coverage.cobertura.xml`.
CI runs the same script and uploads the report.

For a fast unit/HTTP-only check without a database:

```powershell
dotnet test --filter "Category!=Database" /p:CollectCoverage=false
```

That focused command is not a full coverage-policy check.

## Architecture and design notes

`Bookstore.Api` contains grouped Minimal API routes, Problem Details, live
OpenAPI, and JSON Serilog logging with trace correlation. `Bookstore.Core`
contains immutable contracts, FluentValidation rules, and testable book
operations. `Bookstore.Infrastructure` implements async MongoDB queries and
atomic inventory updates behind `IBookRepository`, using one DI-owned client.
The MongoDB driver is retained instead of EF Core because existing BSON data
and MongoDB behavior must be preserved; no relational ORM is required.

The migration intentionally fixes legacy defects and changes invalid-input
responses and update success status. Review the compatibility table before
upgrading a consumer. There is no frontend, authentication provider, delete
operation, or financial transaction model.

## Maintainers

Use the repository's GitHub issues and pull requests for maintenance coordination.
No team contact channel or CODEOWNERS file is configured.

# Harness Architecture

## Tools

| Tool | Why | Install (reference) |
|------|-----|---------------------|
| **GitHub MCP** | Fetch issues (included in `.mcp.json`) | Uses Copilot GitHub auth — `https://api.githubcopilot.com/mcp/` with `X-MCP-Toolsets: issues` |
| **CodeGraph** | Code navigation (base) | `npm i -g @colbymchenry/codegraph` → `codegraph install` → in repo `codegraph init` |
| **RTK** | Compress shell/test noise | `brew install rtk-ai/tap/rtk` (or Cargo from `rtk-ai/rtk`) → `rtk gain` to verify → `rtk init -g --copilot` |
| **Caveman** | Chat prose lite only | `npx skills add JuliusBrussee/caveman -g` → use `/caveman lite` in chat |
| **Graphify** (optional) | Project/docs/schema map | Install per [Graphify](https://github.com/safishamsi/graphify), then add the MCP block below |
| **Spec Kit** (optional) | Spec-driven delivery | [github/spec-kit](https://github.com/github/spec-kit) `specify init` for Copilot |
| **grill-with-docs** (optional) | ADRs / CONTEXT alignment | From [mattpocock/skills](https://github.com/mattpocock/skills) |

### Optional Graphify MCP

Add only if Graphify is installed. Portable `.mcp.json`:

```json
"graphify": {
  "type": "stdio",
  "command": "graphify",
  "args": [".", "--mcp"]
}
```

VS Code `.vscode/mcp.json` uses the same server entry under `"servers"`.

## How Harness is configured

- **GitHub MCP** to load an issue before work  
- **CodeGraph** for code structure  
- **Graphify** for project/docs (if present)  
- **RTK** for noisy shell  
- **Caveman lite** for chat only — never for SPEC/ADR/PR text  
- **Spec Kit** alone as the delivery spine if you use one  

No phase profiles. No parallel Superpowers + Spec Kit.

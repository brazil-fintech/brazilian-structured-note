# The COE booking platform

A .NET 10 back end and a React front end for registering COEs against B3 payoff figures.

The design question the platform answers is: **B3 publishes figures, each with its own set of
registered fields and its own rules — how do we support a new one without a release?**

The answer is that no figure is ever written in code. A figure is a **domain file**; a worker
compiles it into a **template**; the API and the React app are both generic readers of that
template. Adding `COE001042` means adding `domain/figures/coe001042-….json`.

## The pieces

```
domain/                    ← the source of truth for every figure (see domain/README.md)
        │
        │  file watch / poll
        ▼
src/Coe.Worker             ← ingestion: reads domain files, compiles, versions, enables
        │
        ▼
   MSSQL  figure.Figure · figure.FigureTemplate · asset.Asset · ref.Holiday · ref.Underlying
        │
        ▼
src/Coe.Api                ← template + asset endpoints, and the validation authority
        │
        │  GET  /api/figures/{code}/template
        │  POST /api/assets/validate     ← called as the user types
        │  POST /api/assets              ← full validation, then save
        ▼
web/                       ← React: asset list, figure picker, dynamic form
```

| Project | What it is |
|---|---|
| `src/Coe.Core` | The shared contract: the template model, the portable expression AST and its evaluator, the validation engine, and the entities. No I/O, no framework. |
| `src/Coe.Ingestion` | Reading domain files and compiling them: the infix expression parser, name resolution, fragment merging, and the ingestion pass itself. |
| `src/Coe.Infrastructure` | MSSQL via EF Core, the figure catalog, the template cache, the booking service, and the server-side checks. |
| `src/Coe.Api` | Minimal-API endpoints and DI wiring. |
| `src/Coe.Worker` | The hosted service that runs ingestion on a file watch and an interval. |
| `web/` | React + TypeScript. Contains a mirror of the expression evaluator and validation engine. |

## The template is the contract

A template describes one figure completely:

- **sections** — the common block, always on screen, plus one tab per block (payoff, underlying,
  basket, cash flows, barriers, autocall, observations, terms). Repeating sections are grids.
- **fields** — data type, units, decimals, bounds, options, B3 field name, formula symbol, and
  the conditions that make an attribute visible, required, read-only or derived.
- **rules** — cross-field checks, each with a severity, the attributes it lands on, the
  attributes it reads, and where it may run.

Both sides read exactly this. The React app has no knowledge of what a call spread is, and
neither does the API's validation engine.

### Conditions travel as an AST, not as source

Domain files are authored in a small infix language (`cap > 0 and cap <= 500`). The **worker
parses it once** and stores the resulting AST. Two consequences:

- The browser never needs a parser, and can never disagree with the server about precedence.
- A typo becomes a compile error that quarantines the figure at ingestion time, rather than a
  runtime failure in front of a user.

`ExpressionEvaluator.cs` and `web/src/engine/evaluate.ts` implement the same node kinds and the
same treatment of missing values. They are edited together; the test suites on both sides carry
the same cases so drift shows up as a failure.

The one deliberate difference: the server compares `decimal`, the browser compares IEEE
doubles, so the TypeScript comparison uses a small tolerance. Otherwise a basket of three 33.33%
weights would show an error in the browser that the API would not raise.

## Validation: three scopes, one gate

| Scope | When | What it checks |
|---|---|---|
| `field` | as the user types, debounced | only the changed paths and whatever reads them |
| `form` | continuously, in the browser | everything fillable, without "you must fill this in" noise |
| `submit` | on save, **always on the API** | everything, including the server-only checks |

The narrow `field` scope is what makes as-you-type validation usable: a rule declares the
attributes it reads (`dependsOn`, computed at ingestion), so a keystroke re-runs the handful of
rules that care and leaves the rest of the form alone.

**The API is the authority.** Client-side checks exist for latency, not for safety: every save
runs the full submit-scope pass server-side before anything is written, re-derives computed
attributes from their inputs, and refuses on any error. A payload that skipped the browser
entirely is checked exactly as strictly.

Warnings behave differently from errors on purpose. An error blocks the save. A warning does
not — the user can save through it, and the warnings they accepted are stored on the asset so
the decision is auditable.

Checks that need reference data — business-day calendars, code uniqueness — cannot run in a
browser. Those rules name a **server check** by id and are answered by the validate endpoint,
so they still reach the user as they type, just over the wire.

## Ingestion, versioning and enabling a figure

The worker hashes each domain file **together with the fragments it extends**, so editing a
shared block re-issues a version for every figure that inherits it. Unchanged content is
skipped.

A file that compiles produces a new immutable `FigureTemplate` version, made active in the same
transaction that stands the previous one down. A file that does not compile publishes nothing:
the figure is `Quarantined` with the errors recorded, and the previously active template keeps
serving. Assets store the template version they were booked against, so an edit never rewrites
the meaning of an asset already in the book.

`AutoEnableNewFigures` decides whether a newly compiled figure is bookable immediately or waits
in `Pending` for a desk to release it.

## Endpoints

| | |
|---|---|
| `GET /api/figures` | figures available for booking (`?includeDisabled=true` for the rest) |
| `GET /api/figures/{code}/template` | the compiled template; `?version=` for a specific one |
| `GET /api/assets?referenceDate=…` | assets live on the date: `issueDate <= referenceDate <= maturityDate` |
| `GET /api/assets/{id}` | the full instance document, plus the rowversion for concurrency |
| `POST /api/assets/validate` | as-you-type validation; messages pinned to instance paths |
| `POST /api/assets` · `PUT /api/assets/{id}` | full validation, then save |
| `GET /api/reference/{source}` | lists a field's `optionSource` (currently `underlyings`) |
| `POST /api/admin/ingest` | re-read the domain files now, without waiting for the worker |

## Data model

`asset.Asset` keeps the whole instance document in `ValuesJson` and duplicates the handful of
attributes the list screen filters and sorts on into indexed columns. The reference-date filter
is the primary access path and is served by `IX_Asset_Live (MaturityDate, IssueDate)`; the grid
never opens the JSON. Booking is the only writer of those columns, so they cannot drift.

Concurrency on edit uses the SQL Server `rowversion`: the client sends back what it loaded, and
a save against a stale version returns 409 rather than overwriting someone else's work.

The schema lives in `db/*.sql` and is applied at startup by both the API and the worker. Every
script is written to be re-runnable, so a fresh database and a long-lived one converge without a
migration-history table to drift out of sync. Scripts are additive — never rewrite one that has
shipped, add the next number.

## Running it

```bash
# 1. SQL Server
docker compose up -d mssql

# 2. Compile the domain files and keep watching them
dotnet run --project src/Coe.Worker

# 3. The API (also applies db/*.sql on startup)
dotnet run --project src/Coe.Api          # http://localhost:5080

# 4. The React app; /api is proxied to the API in dev
cd web && npm install && npm run dev      # http://localhost:5173
```

Connection string: `ConnectionStrings:Coe`, overridable with
`ConnectionStrings__Coe` in the environment.

```bash
dotnet test          # expression, compiler and validation-engine suites
cd web && npm test   # the TypeScript mirror of the same cases
```

`dotnet test` compiles every checked-in domain file, so a bad edit fails the build rather than
quarantining a figure in production.

## What is deliberately not here

- **No pricing or valuation.** The platform registers and validates terms; it does not mark
  positions. `docs/calculations.md` documents the conventions a valuation service would use.
- **No B3 connectivity.** `Código IF` is a field an operator fills in after registration;
  wiring it to the file-transfer interface is separate work.
- **No authentication.** Endpoints read `HttpContext.User` for the audit stamp and work
  unauthenticated; put the platform behind your identity provider before exposing it.

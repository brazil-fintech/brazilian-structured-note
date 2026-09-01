# The COE booking platform

A .NET 10 back end and a React front end for registering COEs against B3 payoff figures.

The design question the platform answers is: **B3 publishes figures, each with its own set of
registered fields and its own rules — how do we support a new one without a release?**

The answer is that no figure is ever written in code. A figure is a **domain file**; a worker
compiles it into a **template**; the API and the React app are both generic readers of that
template. Adding `COE001042` means adding `domain/figures/coe001042-….json`.

## The pieces

```
reference/b3/              ← B3's published exports: figures, domains, fields, underlyings
domain/                    ← the source of truth for every figure (see domain/README.md)
        │
        │  file watch / poll  ·  domain files are checked against reference/b3/
        ▼
src/Coe.Worker             ← ingestion: reads domain files, compiles, versions, enables
        │
        ▼
   MSSQL  figure.Figure · figure.FigureTemplate · asset.Asset
          ref.Holiday · ref.Underlying · b3.Figure · b3.Domain · b3.StrategyField
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
| `src/Coe.Infrastructure` | ADO.NET over `Microsoft.Data.SqlClient`, the figure catalog, the template cache, the booking service, and the server-side checks. |
| `src/Coe.Observability` | Serilog wiring and the OpenTelemetry trace/metric pipeline, shared by both hosts. |
| `src/Coe.Api` | Minimal-API endpoints and DI wiring. |
| `src/Coe.Worker` | The hosted service that runs ingestion on a file watch and an interval. |
| `web/` | React + TypeScript. Contains a mirror of the expression evaluator and validation engine. |
| `tests/Coe.Benchmarks` | BenchmarkDotNet harness for the validation path. |

**There is no ORM.** Queries are hand-written SQL against `Microsoft.Data.SqlClient`, for two
reasons that matter here: the shapes the platform needs are unusual for an ORM (a page and its
unpaged total in one statement, an `OUTPUT` clause returning a rowversion, a single-statement
upsert), and the instance document is JSON that no entity mapping would model usefully. The cost
is that the SQL is yours to maintain; the tests in `DatabaseTests` run it against a real server.

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

## B3's reference data is the authority

The exports under [`reference/b3/`](../reference/b3/README.md) — the figure catalogue, the
registration domains, the strategy-field dictionary and the underlying master — say what B3 will
accept. The platform checks itself against them rather than against hand-written lists, in two
places.

**At compile time.** A `figureCode` must exist in B3's catalogue under B3's name; a field
declaring `b3Domain` must give every option a `b3Code` that exists and is still enabled; a field
declaring `b3FieldCode` must agree with the dictionary on type, size and decimals. A figure that
fails is quarantined rather than published, so a rename surfaces at ingestion instead of at
registration.

**At run time.** The worker loads the exports into `b3.*` and `ref.Underlying`, which is what
backs the underlying picker and the `underlyingRegistered` check.

### Internal codes and B3 codes are kept separate

An option carries both: the mnemonic `code` the instance stores and the rules are written
against (`STANDARD`), and the `b3Code` a registration file must carry (`3`). Storing B3's numeric
code directly would make every rule cryptic — `maturityRemunerator in ['2','5','7']` — and would
turn a B3 code change into a rewrite of every rule that names the option. Keeping them separate
makes that a reference-data update, and the compiler proves the mapping is still valid.

Asset classes are the exception: they are stored using B3's own spelling
(`ACOES INTERNACIONAIS`), because that is what the underlying master is keyed on, and a mapping
table between the two would be a thing to get wrong for no benefit.

### What this caught

Aligning to the real exports corrected invented values that would have been rejected at
registration: the basket type had two options where B3 publishes eight; the underlying classes
were missing `JUROS`, `JUROS INTERNACIONAIS`, `TITULOS PUBLICOS` and `TITULOS PRIVADOS` — the
last of which the physical-delivery rule is supposed to depend on; the maturity remunerator was
missing `SOFR VCP` and pointed at code 12, which B3 has disabled; the specific redemption
condition was free text where B3 registers a two-value domain. And of seventeen hand-written
placeholder underlyings, eleven did not exist in B3's master at all — including `IBOV`, which B3
lists as `IBOVESPA`.

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
| `GET /api/figures/catalogue` | B3's whole catalogue with each figure's availability here, plus coverage counts — what the picker renders |
| `GET /api/figures/{code}/template` | the compiled template; `?version=` for a specific one |
| `GET /api/assets?referenceDate=…` | assets live on the date: `issueDate <= referenceDate <= maturityDate` |
| `GET /api/assets/{id}` | the full instance document, plus the rowversion for concurrency |
| `POST /api/assets/validate` | as-you-type validation; messages pinned to instance paths |
| `POST /api/assets` · `PUT /api/assets/{id}` | full validation, then save |
| `GET /api/reference/{source}` | lists a field's `optionSource` (`underlyings`, 1,582 codes from B3's master) |
| `POST /api/admin/ingest` | re-read the domain files now, without waiting for the worker |

## Performance

The validate endpoint is called on every keystroke, so its cost is a user-visible number.
Measured on .NET 10 with `tests/Coe.Benchmarks` (`dotnet run -c Release --project tests/Coe.Benchmarks`),
against the call spread figure — 51 attributes, 30 rules:

| | mean | allocated |
|---|---|---|
| validate, field scope (one changed attribute) | 9.1 µs | 8 KB |
| validate, submit scope (whole instance) | 17.3 µs | 19 KB |
| recompute derived attributes | 0.6 µs | 0.8 KB |
| **deserialize a stored template** | **336 µs** | **212 KB** |

The last row is the one that shaped the design: parsing a template costs roughly **37× a full
validation**, so a cache miss dominates everything else on the request. Hence:

- **Template versions are cached for the life of the process.** A published version is immutable,
  so a hit is always correct. Only the *pointer* to the active version is re-read, on a 30-second
  TTL — which is what lets a newly published template take effect without a restart.
- **The browser fetches each template once.** A request for an explicit version is answered with
  a strong ETag and a one-year `Cache-Control`; the 47 KB call-spread template revalidates to a
  304 with an empty body. The active-version request gets a 30-second window instead, because
  picking up new templates is the point of it.

Inside a pass, the narrowing is what keeps `field` scope cheap: rules declare the attributes they
read (`dependsOn`, computed at ingestion), the changed paths are normalised once into a
`ChangeSet`, and matching is then set lookups rather than a regex per dependency per rule.

On the database side:

- **One round trip per screen.** The asset list carries its unpaged total via `COUNT(*) OVER ()`
  and joins the figure name, instead of a second `COUNT` and a name lookup. A save returns its new
  rowversion through an `OUTPUT` clause rather than a follow-up `SELECT`.
- **The list never reads `ValuesJson`.** The instance document is `nvarchar(max)`; reading fifty
  of them to render a grid would dominate the query's I/O and none of it would be displayed. The
  grid columns are a projection maintained by the booking path.
- **Every string parameter has an explicit size.** Left to infer, SqlClient sizes an `nvarchar`
  parameter from the value it happens to hold, so the same query issued with a 6- and a
  12-character name arrives as two statements and fills the plan cache with near-duplicates.
- **Server-side checks take no I/O.** The holiday calendar is loaded once and cached; whether an
  instrument code is taken is resolved in one query *before* the pass. Both are handed to the
  engine as facts, so validation stays synchronous, pure CPU, and free of an N+1 inside the rule
  loop.
- **Sequential GUIDs.** `Guid.CreateVersion7()` keeps inserts at the end of the clustered index
  instead of splitting pages across it.

## Observability

Three signals, joined together.

**Logs** — Serilog, configured from the `Serilog` section of `appsettings.json`. Console with a
readable template in development, JSON in production. Every line carries `service.name`, machine
and environment, and — through `TraceContextEnricher` — the `TraceId` and `SpanId` of the activity
it happened inside. That last part is the join: a slow request is a span, its log lines carry the
span id, and the metrics say whether it is one request or a trend.

**Traces** — OpenTelemetry. ASP.NET Core and SqlClient instrumentation, plus three domain sources:
`Coe.Validation` (a span per pass, tagged with figure, template version, scope, rules evaluated
and findings), `Coe.Ingestion` (a span per pass and per figure compiled) and `Coe.Booking` (a span
per save, tagged with the outcome). Health probes are filtered out so they cannot dominate.

**Metrics** — the `Coe` meter, plus runtime and ASP.NET Core instrumentation:

| instrument | what it answers |
|---|---|
| `coe.validation.duration` | is the per-keystroke call still fast? (explicit ms buckets) |
| `coe.validation.rules_evaluated` | is field-scope narrowing still working? |
| `coe.validation.messages` | are errors or warnings spiking, by severity and origin? |
| `coe.asset.saves` | saved / rejected / conflict |
| `coe.template.cache_lookups` | hit or miss — a miss is the 336 µs above |
| `coe.ingestion.runs`, `.templates_published`, `.duration` | is the worker keeping up, and is anything quarantined? |
| `coe.sql.command.duration`, `coe.sql.retries` | database latency, and transient faults being absorbed |

Point `Observability:OtlpEndpoint` at a collector to export; `docker compose --profile
observability up -d` starts one locally. `ConsoleExporter: true` dumps to stdout for a quick look.
Sampling is head-based via `TraceSampleRatio`.

Health: `/health/live` (process is up) and `/health/ready` (database reachable).

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

Connection string: `ConnectionStrings:Coe`, overridable with `ConnectionStrings__Coe` in the
environment. In Development the host creates the database if it is missing
(`Database:CreateDatabaseIfMissing`); elsewhere that is off, because silently creating an empty
database on a mistyped connection string hides the mistake.

**`InvariantGlobalization` must stay off.** `Microsoft.Data.SqlClient` throws *"Globalization
Invariant Mode is not supported"* on its first connection, so any container image running the API
or worker needs ICU.

```bash
dotnet test          # expression, compiler and validation-engine suites
cd web && npm test   # the TypeScript mirror of the same cases

# The database tests need a server; without one they skip rather than fail.
COE_TEST_SQL="Server=localhost,1433;User Id=sa;Password=Your_password123;TrustServerCertificate=True" \
  dotnet test
```

Each database test run creates a throwaway database, applies the repository's own scripts in
`db/` to it, and drops it afterwards — so it exercises the real schema rather than an
approximation of it.

`dotnet test` compiles every checked-in domain file, so a bad edit fails the build rather than
quarantining a figure in production. Set `COE_DOMAIN_DIR` to point the suite at a catalog
outside the repository.

## What is deliberately not here

- **No pricing or valuation.** The platform registers and validates terms; it does not mark
  positions. `docs/calculations.md` documents the conventions a valuation service would use.
- **No B3 connectivity.** `Código IF` is a field an operator fills in after registration;
  wiring it to the file-transfer interface is separate work.
- **No authentication.** Endpoints read `HttpContext.User` for the audit stamp and work
  unauthenticated; put the platform behind your identity provider before exposing it.

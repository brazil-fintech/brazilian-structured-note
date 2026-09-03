# Running the platform from GitHub

Everything the platform is made of — the API, the ingestion worker and the booking screen — is
published from this repository, so running it is a pull rather than a build:

| Piece | Where it is published | What it is |
|---|---|---|
| API | `ghcr.io/brazil-fintech/brazilian-structured-note/api` | The booking and clearing endpoints, and the schema it applies at startup. |
| Worker | `ghcr.io/brazil-fintech/brazilian-structured-note/worker` | Compiles `domain/` into templates and keeps B3's exports fresh. |
| Web | `ghcr.io/brazil-fintech/brazilian-structured-note/web` | The React screen behind nginx, which also proxies `/api`. |
| Web (static) | `https://brazil-fintech.github.io/brazilian-structured-note/` | The same screen on GitHub Pages, pointed at whichever API you give it. |
| Development machine | GitHub Codespaces | The repository with the .NET SDK, Node and Docker already on it. |

The images carry the three directories the hosts read from disk — `db/`, `domain/` and
`reference/b3/` — so a container needs nothing from a checkout: a fresh API creates its schema,
and a fresh worker compiles all 88 figures of B3's catalogue against the exports baked into the
image. Images are built from `deploy/*.Dockerfile` by
[`.github/workflows/publish.yml`](../.github/workflows/publish.yml) on every push to `main`,
tagged `latest`, `sha-<commit>` and — for a `v*` tag — `1.2.3` and `1.2`.

## The whole stack, from the published images

```bash
curl -O https://raw.githubusercontent.com/brazil-fintech/brazilian-structured-note/main/deploy/docker-compose.hosted.yml

# The one setting with no default. It is the sa password of the SQL Server this brings up.
echo "COE_SQL_PASSWORD=$(openssl rand -base64 24)aA1!" > .env

docker compose -f docker-compose.hosted.yml up -d
```

- the booking screen on <http://localhost:8080>
- the API on <http://localhost:5080> (`/health/ready`, `/api/figures/catalogue`)
- SQL Server on `localhost:1433`

The first pass takes about a minute: SQL Server starts, both hosts apply `db/*.sql` — the
scripts are idempotent, so whichever gets there first wins and the other is a no-op — and the
worker compiles the catalogue. `docker compose -f docker-compose.hosted.yml logs -f worker`
shows it; `GET /api/figures/catalogue` reports `bookable: 88` once it is done.

Everything else has a default and is listed in [`deploy/hosted.env.example`](../deploy/hosted.env.example)
— the ports, the database name, the issuer short name written into the CETIP upload files, an
OTLP endpoint to export traces and metrics to, and whether the CETIP sync runs at all.

### One container at a time

```bash
docker run --rm \
  -e "ConnectionStrings__Coe=Server=<host>,1433;Database=Coe;User Id=sa;Password=<password>;TrustServerCertificate=True;Encrypt=True" \
  -p 5080:8080 \
  ghcr.io/brazil-fintech/brazilian-structured-note/api:latest
```

Settings are the ones in `appsettings.json`, in the environment-variable form ASP.NET Core reads
(`Section__Key`, an array index for a list). The ones a container usually sets:

| Variable | What it does |
|---|---|
| `ConnectionStrings__Coe` | The SQL Server the platform runs on. Required. |
| `Database__CreateDatabaseIfMissing` | Creates the database when it is absent. Off outside Development, because creating an empty one on a mistyped connection string hides the mistake. |
| `Cetip__Enabled` | `false` keeps the platform on the exports committed in the image, with no FTP call at all. |
| `Clearing__ParticipantName` | The issuer short name written into the upload files when a certificate does not name one. |
| `Cors__Origins__0` | An origin allowed to call the API from a browser — needed only when the app is served from somewhere else, such as GitHub Pages. |
| `Observability__OtlpEndpoint` | A collector to export traces and metrics to. |

The image's own defaults point `Database__ScriptDirectory`, `Ingestion__DomainDirectory` and
`Ingestion__ReferenceDirectory` at the copies inside it. The worker writes the exports it fetches
back into the reference directory, so mount a volume at `/app/reference/b3` to keep them across
restarts — the compose file does.

The API image declares no `HEALTHCHECK`: its runtime image ships without an HTTP client, and
installing one to poll a port would be a package to keep patched for it. The probes are on the
API itself — `/health/live` and `/health/ready` — for whatever runs the container to call.

## The password is a secret, not a setting

`deploy/docker-compose.hosted.yml` has no default for `COE_SQL_PASSWORD` and refuses to start
without it. It comes from two places, neither of them a committed file:

- **On GitHub** — repository *Settings → Secrets and variables → Actions → New repository
  secret*, named `COE_SQL_PASSWORD`. The build workflow's SQL Server and the smoke test that
  boots the published images both read it.
- **On a machine running the stack** — `deploy/.env`, which is in `.gitignore`.

Generate it from base64 (`openssl rand -base64 24`): the password travels through a connection
string and a shell health probe, so spaces, quotes and `;` are worth avoiding.

Local development is the exception and stays as it was: `docker-compose.yml` and the
`appsettings.json` connection strings carry a well-known password for a SQL Server on
`localhost` that exists for the length of a debugging session. Override it anywhere it matters
with `ConnectionStrings__Coe`.

## The booking screen on GitHub Pages

[`.github/workflows/pages.yml`](../.github/workflows/pages.yml) builds `web/` and publishes it to
GitHub Pages on every push to `main` that touches it. The page is the screen and nothing else —
it has no API of its own — so it has to be told where to send its calls. In order:

1. `?api=https://host/api` on the URL, which is remembered afterwards. This is how a visitor
   points the published page at their own instance, without a rebuild.
2. What a previous visit remembered (`localStorage`, `coe.apiBaseUrl`).
3. `config.js`, written at deploy time from the `COE_API_BASE_URL` repository variable
   (*Settings → Secrets and variables → Actions → Variables*), and at container start from the
   environment variable of the same name.
4. `VITE_API_BASE_URL`, if the bundle was built with one.
5. `/api` on the page's own origin — what the dev server proxies, and what the web image's nginx
   proxies to the API container.

An API called from another origin has to allow it: `Cors__Origins__0=https://<owner>.github.io`
on the API, or run the app from the web image instead, where nginx puts both on one origin and no
preflight sits in front of every validation call.

## Codespaces

*Code → Codespaces → Create codespace* gives a machine with the .NET SDK, Node and Docker already
installed ([`.devcontainer/devcontainer.json`](../.devcontainer/devcontainer.json)), and the ports
forwarded — the dependency list in the [repository README](../README.md#dependencies),
installed for you. From there the platform runs exactly as it does locally:

```bash
docker compose up -d mssql
dotnet run --project src/Coe.Worker
dotnet run --project src/Coe.Api
cd web && npm run dev
```

## Setting this up on a fork

Three one-time settings, none of which the workflows can do for themselves:

1. **The SQL password** — the `COE_SQL_PASSWORD` secret described above.
2. **Package visibility** — images push privately on the first run. *Packages → `<name>` →
   Package settings → Change visibility → Public* is what makes `docker pull` work for someone
   who has not signed in to `ghcr.io`.
3. **Pages** — *Settings → Pages → Source: GitHub Actions*. A workflow cannot do this for
   itself: `GITHUB_TOKEN` may deploy to a Pages site but not create one. Until the setting is
   flipped, the `pages` workflow builds and tests the screen, says so with a warning, and stops
   short of publishing.

## What hosting this does not give you

The platform still has **no authentication**: endpoints read `HttpContext.User` for the audit
stamp and work unauthenticated. Publish it behind an identity provider, and do not expose the
API — or the SQL Server port the compose file maps — to a network you do not control.

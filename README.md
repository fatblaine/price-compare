# PriceCompare (Price Peer): Grocery Price Tracker for Coles & Woolworths

PriceCompare is a full-stack app that scrapes, stores and compares grocery prices from Coles and Woolworths (Australia). Users can browse and search products, compare an item against its precomputed match at the other supermarket, view price history, track favourites with price-drop email alerts, and upload receipts for OCR parsing.

- Production: <https://www.price-peer.com>

---

## Features

- **Product browsing** – server-side paginated list with name/shop/category filters, latest price and promo text (latest per-shop scrape only; responses cached for 5 minutes).
- **Cross-shop comparison** – compare dialog backed by precomputed `productmatch` rows (`same_product` / `comparable`), with a purchase recommendation on each product card ("X is cheaper! (save $Y)").
- **Price history chart** – per-product price trend (Recharts).
- **AI description search** – find products from a free-text description (OpenRouter embeddings, rate-limited per user/IP).
- **Favourites & price alerts** – weekly job compares the latest price against last week's; all drops for a user go into one digest email, sorted by % drop.
- **Receipts** – upload a receipt image → S3 → AWS Rekognition OCR → parsed line items matched to products; items can be edited afterwards.
- **Admin console** (`/admin`, allow-listed verified emails only) – job schedules and run history/health, match-job runner, LLM match review, and a manual match review/override grid.
- **Onboarding tour** – guided walkthrough built with driver.js.

---

## Architecture

```
Scrapers (local, Quartz) ──► IngestionService ──► PostgreSQL (Supabase)
        │                      upsert product +        ▲
        └─► optional SQL export  write pricehistory     │
             (exports/) ──► tools/run-sql-imports.ps1 ──┘

Match job (exact → vector → fallback, optional LLM verify) ──► productmatch
Compare endpoints read productmatch (no realtime matching on the hot path)

React SPA (S3) ──► API Gateway (HTTP API) ──► PriceCompareApi Lambda (ASP.NET Core)
EventBridge Scheduler ──► FavoritePriceTrackingJob Lambda ──► SMTP digest email
```

| Project | Role |
|---|---|
| `src/PriceCompareWeb` | ASP.NET Core Web API: controllers, DI wiring and Quartz schedule (`Program.cs`), Lambda job entry points (`JobsLambda/`) |
| `src/PriceCompareCore` | Business logic: scrapers, ingestion, matching, favourites, receipts, Quartz jobs |
| `src/PriceCompareData` | EF Core `AppDbContext`, entities, DTOs, constants (`ShopType`, `OfferType`) |
| `client/web` | React 19 + TypeScript SPA |
| `tests/PriceCompareTests` | xUnit tests |
| `tools/` | Deployment / import PowerShell scripts, `KeywordMiner` (category keyword mining utility) |

---

## Tech Stack

- **Backend:** .NET 10 (LTS, SDK pinned in `global.json`), ASP.NET Core, EF Core 10 + Npgsql, Quartz.NET, Polly, Swagger, `Amazon.Lambda.AspNetCoreServer.Hosting`
- **Database:** PostgreSQL (Supabase in dev/prod)
- **Cache:** Redis via `IDistributedCache` (Upstash in the cloud), falls back to in-memory when unset
- **Scraping:** HtmlAgilityPack + JSON parsing (Coles category/Down Down pages, Woolworths promo pages); Playwright for the legacy Coles/Woolworths On-Special scrapers
- **AI:** OpenRouter – embeddings (`openai/text-embedding-3-small`) and LLM match verification (`deepseek/deepseek-chat-v3.1` by default)
- **Auth:** AWS Cognito (OIDC in the SPA via `react-oidc-context`; the API validates the Cognito ID token)
- **Frontend:** React 19, TypeScript, MUI 7 + MUI X DataGrid, Recharts, Axios, React Router 6, Biome
- **AWS:** SAM, Lambda (`dotnet10`), API Gateway HTTP API, S3, Rekognition, EventBridge Scheduler, Secrets Manager
- **CI:** GitHub Actions

---

## Prerequisites

- .NET SDK 10.0.x
- Node.js 20 and npm
- PostgreSQL 14+ (or a Supabase project)
- An AWS Cognito user pool + app client (required – the API refuses to start without Cognito config)
- Optional: Redis, AWS credentials (S3/Rekognition for receipts), SMTP account (alert emails), OpenRouter API key (matching / description search)
- Deploy only: AWS CLI + AWS SAM CLI

---

## Quick Start (Local)

### 1. Configure the backend

Create `src/PriceCompareWeb/appsettings.Development.json` (gitignored) or use environment variables (`__` as the section separator). Minimum settings:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=pricecompare;Username=postgres;Password=postgres"
  },
  "Cognito": {
    "Region": "ap-southeast-2",
    "UserPoolId": "<user-pool-id>",
    "AppClientId": "<app-client-id>"
  },
  "Admin": { "Emails": [ "you@example.com" ] },
  "Scraping": { "EnableQuartz": false, "ExposeHttpEndpoints": false },
  "OpenRouter": { "ApiKey": "" }
}
```

> The schema is managed outside EF migrations. The migrations under `src/PriceCompareData/Migrations` are legacy SQL Server migrations and do not match the current PostgreSQL schema – point the app at an existing database (or a copy of one).

### 2. Run the API

```bash
cd src/PriceCompareWeb
dotnet run
```

The API listens on `http://localhost:5005`; Swagger UI is at `http://localhost:5005/swagger` in `Development`. Paste a Cognito **ID token** (without the `Bearer ` prefix) to call authenticated endpoints.

### 3. Run the frontend

Create `client/web/.env.local` from `client/web/.env.example` and fill in the Cognito values. Leave `REACT_APP_API_BASE` unset locally so requests go through the CRA proxy to `http://localhost:5005`.

```bash
cd client/web
npm install
npm start          # or, from the repo root: ./tools/start-frontend-local.ps1
```

The app opens at `http://localhost:3000`.

---

## Data Pipeline

Scrapers **run locally**, not in AWS (Coles sits behind Imperva bot protection, so scraping is kept on a local machine).

1. Start the API locally with `Scraping:EnableQuartz=true`. Quartz runs the weekly scrape jobs every **Wednesday (Australia/Sydney)**, one at a time behind a shared lock:
   - 00:05–03:15 – 20 Coles jobs (Down Down + 19 categories), every 10 min
   - 03:45–06:15 – 5 Woolworths jobs (Lower Shelf, Everyday Low Price, Half Price, Buy More Save More, seasonal price)
   - 06:55 – favourite price tracking (local copy of the Lambda job)
   - Quarterly (first Tuesday of every 3rd month, 06:00) – price history cleanup
2. Each run upserts the `product` table (matched by `SourceId`, then name) and appends to `pricehistory`.
3. With `ScrapeExport:Enabled=true`, results are also exported to `src/PriceCompareWeb/exports/`. Generate import SQL (`POST /api/admin/scrape-import/generate-all-sql`, local only) and apply it to the target database with:

   ```powershell
   $env:SUPABASE_CONN = "postgresql://user:pass@host:5432/postgres"
   ./tools/run-sql-imports.ps1            # -DryRun to preview, -Filter woolworths to narrow
   ```
4. Run a match job from the admin console (or `POST /api/Match/run`) to refresh `productmatch`.

Manual scrape endpoints under `/api/Scraping/*` exist but are disabled unless `Scraping:ExposeHttpEndpoints=true`.

---

## API Overview

| Area | Endpoints | Auth |
|---|---|---|
| Products | `GET /api/Products` (`page`, `pageSize` ≤ 200, `name`, `shopType`, `categoryId`, `includePrice`)<br>`GET /api/Products/priceHistory` (`name`, `shopType`, `offerType?`)<br>`POST /api/Products/search-by-description` | public |
| Compare | `GET /api/compare-cached` (`keyword`, `sourceShop`, `topN`)<br>`GET /api/compare-cached/by-product` (`sourceProductId`, `topN`)<br>`GET /api/Compare` (legacy realtime compare) | public |
| Favourites | `GET /api/Favorites`, `POST/PUT/DELETE /api/Favorites/{productId}`, `POST /api/Favorites/price-check` | user |
| Receipts | `GET/POST /api/Receipts`, `GET/DELETE /api/Receipts/{id}`, `POST /api/Receipts/{id}/upload`, `PUT /api/Receipts/{id}/items`, `POST /api/Receipts/upload-and-parse` | user |
| Match | `POST /api/Match/run`, `POST /api/Match/llm-review/run`, `GET /api/Match/status/{jobId}`, `GET /api/Match/jobs`, `GET /api/Match/productmatches/review`, `POST /api/Match/productmatches/update` | admin |
| Admin | `GET /api/admin/whoami`, `GET /api/admin/health`, `GET /api/admin/schedules`, `GET /api/admin/schedules/{jobName}/runs`, `GET /api/admin/schedules/{jobName}/stats`, `POST /api/admin/pricehistory/cleanup`, `POST /api/admin/scrape-import/*` (local only) | admin |

Constants: `shopType` – `0` Coles, `1` Woolworths. `offerType` – see `src/PriceCompareData/Common/OfferType.cs`.

**Admin policy (`AdminOnly`)** requires a Cognito token with `email_verified = true` and an email in the allow-list (`Admin:Emails` or the comma-separated `AdminEmails` env var). An empty allow-list denies everyone. The frontend admin route guard is UX only.

> In production every route must also be declared as an `Events` entry on `PriceCompareApi` in `template.yaml`, otherwise API Gateway returns 404.

---

## Configuration

| Key | Description |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `Cognito:Region` / `UserPoolId` / `AppClientId` | Cognito settings for JWT validation (required) |
| `Admin:Emails` / `AdminEmails` | Admin allow-list (array in config / comma-separated env var) |
| `Cors:AllowedOrigins` | Allowed origins (required; startup fails if empty) |
| `Redis:ConnectionString` | Optional Redis; in-memory cache when blank |
| `Scraping:EnableQuartz` | Run the in-process Quartz schedule |
| `Scraping:ExposeHttpEndpoints` | Enable `/api/Scraping/*` |
| `ScrapeExport:Enabled` / `ExportDir` / `BatchSize` | Export scrape results for SQL import |
| `OpenRouter:ApiKey` / `Model` / `EmbeddingModel` | OpenRouter AI settings (Secrets Manager in AWS) |
| `Aws:Region` / `Aws:ReceiptBucket` | Receipt image storage |
| `Rekognition:MinConfidence` | Minimum OCR line confidence |
| `Email:*` | SMTP settings for alert emails |
| `FavoriteAlerts:BaseUrl` / `FavoritesPath` / `MinDropPercent` | Links in alert emails; optional minimum % drop to alert |
| `TARGET_JOB` | Lambda job selector (`FAVORITE_TRACK`, `COLES_SPECIAL`, `WWS_SPECIAL`) |

Frontend (`client/web/.env.local`): `REACT_APP_API_BASE`, `REACT_APP_COGNITO_REGION`, `REACT_APP_COGNITO_USER_POOL_ID`, `REACT_APP_COGNITO_APP_CLIENT_ID`, `REACT_APP_COGNITO_DOMAIN`.

Never commit real secrets – keep them in `appsettings.Development.json`, environment variables, `samconfig.toml` (all gitignored) or AWS Secrets Manager.

---

## Testing & Linting

```bash
# Backend
dotnet build PriceCompareSolution.sln
dotnet test tests/PriceCompareTests --settings coverage.runsettings

# Frontend (client/web)
npm test
npx tsc --noEmit
npm run check      # Biome lint + format check
```

---

## Deployment

### Backend (AWS SAM)

`template.yaml` defines:

- `AppHttpApi` – API Gateway HTTP API
- `PriceCompareApi` – ZIP Lambda (`dotnet10`) serving the Web API
- `FavoritePriceTrackingJob` – ZIP Lambda triggered by EventBridge Scheduler, Wednesdays 06:55 Australia/Sydney
- `FrontendBucket` / `ReceiptsBucket` – S3 buckets (`pricecompare-frontend-{env}`, `pricecompare-receipts-{env}`)

```bash
sam build
sam deploy --config-env dev     # or: --config-env prod
```

Parameters (DB connection string, SMTP, Cognito IDs, admin emails, Redis, Secrets Manager IDs) come from `samconfig.toml`. The Playwright scrapers (`Dockerfile.ColesSpecial`, `Dockerfile.WwsSpecial`) are for local container builds only and are not deployed.

### Frontend (S3)

```powershell
./tools/deploy-frontend-dev.ps1     # builds with the dev API base and syncs to pricecompare-frontend-dev
./tools/deploy-frontend-prod.ps1    # same for prod
```

### GitHub Actions

- `Deploy Backend (AWS SAM)` and `Deploy Frontend (S3)` – manual (`workflow_dispatch`) deploys
- `Jira Key Check` – requires a Jira key (e.g. `BTS-123`) in PR titles and in commit messages pushed to `main` / `dev`

---

## Known Limitations

- EF migrations are legacy (SQL Server) and do not reflect the PostgreSQL schema.
- `pricehistory` has no FK to `product`; prices are joined by product name.
- Coles uses Imperva bot protection, which can silently block scrapes (HTTP 200 challenge page → 0 products).
- Match / LLM review jobs run as fire-and-forget background tasks, which a Lambda freeze can interrupt – run large jobs locally.
- The favourites job runs weekly, so mid-week price dips that recover are not detected.

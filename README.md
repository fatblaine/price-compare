# PriceCompare: Grocery Price Tracker for Woolworths & Coles

PriceCompare is a full‑stack app that tracks and compares grocery prices from Woolworths and Coles. It supports searching, basic product listing with filters, price comparison across shops, and 7‑day price history visualisation.

The backend is an ASP.NET Core Web API (net8.0) with EF Core and scheduled jobs; the frontend is React + TypeScript using MUI DataGrid. Scrapers ingest “Down Down” and “On Special” items and write both a product base table and price history.

---

## Features

- List and filter products by name and shop (server‑side pagination)
- Compare a selected product with similar items from the other shop
- 7‑day price trend chart (Coles and Woolworths)
- Scraping endpoints to ingest Coles/Woolworths specials and “Down Down” items
- Redis caching for scraped results (optional)
- Swagger for API exploration (Development)

---

## Tech Stack

- Backend: .NET 8, ASP.NET Core Web API, EF Core, Quartz, Swagger
- Database: PostgreSQL (Npgsql) at runtime; legacy migrations were created for SQL Server
- Caching: Redis (optional, falls back to in‑memory for jobs)
- Scraping: HtmlAgilityPack (Coles Down Down), Playwright (Coles On Special), custom JSON parsing
- Frontend: React 19, TypeScript, MUI (+ X DataGrid), Axios, Recharts
- CI: GitHub Actions
- IaC/Deploy: AWS SAM; Lambda ZIP + container image jobs

---

## Prerequisites

- .NET SDK 8.0+
- Node.js 18+ and npm
- PostgreSQL 14+ (or a PostgreSQL instance such as Supabase)
- Redis (optional, for caching) 
- Docker (optional, for Lambda container jobs)

---

## Quick Start (Local)

1) Configure database connection

- Set `ConnectionStrings__DefaultConnection` to a PostgreSQL connection string.
  - Option A (environment variable):
    - Windows (PowerShell): `setx ConnectionStrings__DefaultConnection "Host=localhost;Port=5432;Database=pricecompare;Username=postgres;Password=postgres"`
    - macOS/Linux: `export ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=pricecompare;Username=postgres;Password=postgres'`
  - Option B (file): edit `src/PriceCompareWeb/appsettings.Development.json`.

Important: Existing EF Core migrations under `src/PriceCompareData/Migrations` were generated for SQL Server, not PostgreSQL. If you use PostgreSQL locally, ensure the schema exists (manually or by regenerating migrations for Npgsql). Alternatively, switch back to SQL Server in `Program.cs` and use the provided migrations.

2) (Optional) Enable Redis caching

- Set `Redis__ConnectionString` (e.g., `localhost:6379`). Some Lambda jobs read `USE_REDIS=true` and `Redis__ConnectionString` to decide between Redis and in‑memory cache.

3) Run the backend API

- `cd src/PriceCompareWeb`
- `dotnet restore`
- `dotnet run`

The API listens on `http://localhost:5005` (see `Properties/launchSettings.json`). Swagger is available at `http://localhost:5005/swagger` in Development.

4) Run the frontend (React)

- `cd client/web`
- `npm install`
- `npm start`

The app opens at `http://localhost:3000` and proxies API requests to `http://localhost:5005` (see `client/web/package.json` → `proxy`).

5) Ingest sample data (via API)

Call any of these to seed products + price history:

- `GET /api/Scraping/coles/on-special/all`
- `GET /api/Scraping/woolworths/on-special/all`
- `GET /api/Scraping/coles/down-down/all`

These endpoints persist price history and upsert the products table. After ingestion, use the UI’s search and compare features.

---

## API Overview

- Products
  - `GET /api/Products`
    - Query: `page` (1‑based), `pageSize`, `name?`, `shopType?`, `categoryId?`
    - Returns: `{ Page, PageSize, Count, Products }`

- Compare
  - `GET /api/Compare?keyword={name}&sourceShop={0|1}`
    - `sourceShop`: 0 = Coles, 1 = Woolworths
    - Returns: `{ matches: [ { source, targets[] } ] }` where each product includes fields like `name`, `shopType`, `size`, `price?`, `pricePerUnit?` (if available in history)

- Price History
  - `GET /api/Scraping/priceHistory?name={name}&offerType={0|1}&shopType={0|1}`
    - `offerType`: 0 = Down Down, 1 = On Special
    - `shopType`: 0 = Coles, 1 = Woolworths

- Scraping (ingestion)
  - `GET /api/Scraping/coles/down-down/all`
    - Query: `Name?`, `MinPrice?`, `MaxPrice?`, `IsSponsored?`
  - `GET /api/Scraping/coles/on-special/all`
    - Query: `Name?`, `MinPrice?`, `MaxPrice?`, `IsSponsored?`
  - `GET /api/Scraping/woolworths/on-special/all`
    - Query: `Name?`, `MinPrice?`, `MaxPrice?`, `IsOnSpecial?`

Notes

- The product list (`/api/Products`) returns the product base table; the “price” field in the table is derived from price history and may be empty if not yet scraped.
- The frontend’s compare dialog fetches current history to display a weekly trend.

---

## Background Jobs

Quartz schedules weekly scraping and quarterly cleanup (see `src/PriceCompareWeb/Program.cs`):

- Coles Down Down: Wednesdays 02:00 UTC
- Coles On Special: Wednesdays 03:00 UTC
- Woolworths On Special: Wednesdays 04:00 UTC
- Clean old history: Quarterly (first Thursday, 01:00 UTC)

AWS Lambda jobs are available for “On Special” scrapers and cleanup (see `template.yaml` and `src/PriceCompareWeb/JobsLambda/*`). Container‑based jobs use Playwright and ship Chromium in the image (`Dockerfile.ColesSpecial`, `Dockerfile.WwsSpecial`).

---

## Configuration

Environment variables and settings

- `ConnectionStrings__DefaultConnection`: PostgreSQL connection string
- `Redis__ConnectionString` (optional): Redis endpoint for API caching
- `USE_REDIS` (optional, jobs): set to `true` to force Redis in Lambda jobs
- `ASPNETCORE_ENVIRONMENT`: set `Development` to enable Swagger
- `TARGET_JOB` (jobs only): `COLES_SPECIAL` or `WWS_SPECIAL` for container Lambda entry

Ports

- API: `http://localhost:5005`
- UI: `http://localhost:3000` (proxies to 5005)

---

## Testing

- Backend tests: `dotnet test -s coverage.runsettings` (see `tests/PriceCompareTests`)
- Frontend tests: `npm test` in `client/web`

---

## Deploy (AWS SAM)

The `template.yaml` defines:

- `PriceCompareApi` (ZIP Lambda + API Gateway)
- Weekly `ColesRefreshJob` and `WwsRefreshSpecialJob` (container images)
- `CleanPriceHistoryJob` (ZIP Lambda)

Typical steps:

1) `sam build`
2) `sam deploy --guided --parameter-overrides DbConnectionString="<postgres-connection>"`

Provide the PostgreSQL connection string via the `DbConnectionString` parameter.

---

## Notes & Limitations

- Migrations provider: EF migrations under `src/PriceCompareData/Migrations` target SQL Server. Runtime currently uses Npgsql (PostgreSQL). For a clean PostgreSQL setup, regenerate migrations for Npgsql or use an existing compatible database.
- Secrets: don’t commit real connection strings in `appsettings*.json`. Prefer environment variables or user‑secrets.
- Scrapers: Coles “On Special” uses Playwright with Chromium. Local API usage does not require Playwright; the container job images include it.

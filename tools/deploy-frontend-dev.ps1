param(
    [string]$ApiBase = "https://x7bs7yzbt3.execute-api.ap-southeast-2.amazonaws.com",
    [string]$Bucket = "pricecompare-frontend-dev"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$client = Join-Path $root "client\\web"
if (-not (Test-Path $client)) {
    throw "client/web not found: $client"
}

$env:REACT_APP_API_BASE = $ApiBase

Push-Location $client
try {
    npm run build
    aws s3 sync build "s3://$Bucket" --delete --exclude "index.html"
    aws s3 cp build/index.html "s3://$Bucket/index.html" --cache-control "no-cache, no-store, must-revalidate" --content-type "text/html"
}
finally {
    Pop-Location
}

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$client = Join-Path $root "client\\web"
if (-not (Test-Path $client)) {
    throw "client/web not found: $client"
}

if (Test-Path Env:REACT_APP_API_BASE) {
    Remove-Item Env:REACT_APP_API_BASE
}

Push-Location $client
try {
    npm start
}
finally {
    Pop-Location
}

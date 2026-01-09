param(
  [Parameter(Mandatory = $true)][string]$ApiId,
  [string]$Region = "ap-southeast-2",
  [string]$IntegrationId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$integrations = aws apigatewayv2 get-integrations --api-id $ApiId --region $Region --output json | ConvertFrom-Json
if (-not $integrations.Items -or $integrations.Items.Count -eq 0) {
  throw "No integrations found for API $ApiId"
}

if (-not $IntegrationId) {
  $candidate = $integrations.Items | Where-Object { $_.IntegrationType -eq "AWS_PROXY" } | Select-Object -First 1
  if (-not $candidate) { $candidate = $integrations.Items | Select-Object -First 1 }
  $IntegrationId = $candidate.IntegrationId
}

Write-Host "Using integration: $IntegrationId"

$routes = aws apigatewayv2 get-routes --api-id $ApiId --region $Region --output json | ConvertFrom-Json
foreach ($r in $routes.Items) {
  aws apigatewayv2 update-route --api-id $ApiId --route-id $r.RouteId --target ("integrations/" + $IntegrationId) --region $Region | Out-Null
  Write-Host ("Updated " + $r.RouteKey)
}

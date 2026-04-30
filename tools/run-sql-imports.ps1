<#
.SYNOPSIS
    批量执行 exports/ 下所有 import.sql 文件到 Supabase PostgreSQL

.PARAMETER ConnectionString
    psql 连接字符串，格式：postgresql://user:pass@host:port/db
    默认读取环境变量 SUPABASE_CONN，未设置时报错

.PARAMETER ExportsDir
    exports 根目录，默认为脚本同级的 ../src/PriceCompareWeb/exports

.PARAMETER DryRun
    仅打印将要执行的命令，不实际执行

.PARAMETER Filter
    只处理目录名包含此字符串的文件夹（可选）

.EXAMPLE
    # 设置连接串后执行（推荐，避免密码暴露在命令历史）
    $env:SUPABASE_CONN = "postgresql://user:pass@host:5432/postgres"
    .\run-sql-imports.ps1

    # 直接传参
    .\run-sql-imports.ps1 -ConnectionString "postgresql://user:pass@host:5432/postgres"

    # 只处理 woolworths 相关目录
    .\run-sql-imports.ps1 -Filter "woolworths"

    # 预览将执行什么，不实际写库
    .\run-sql-imports.ps1 -DryRun
#>

param(
    [string]$ConnectionString = $env:SUPABASE_CONN,
    [string]$ExportsDir = "",
    [switch]$DryRun,
    [string]$Filter = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

# ── 路径解析 ────────────────────────────────────────────────────────────────
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ExportsDir) {
    $ExportsDir = Join-Path $scriptDir "..\src\PriceCompareWeb\exports"
}
$ExportsDir = (Resolve-Path $ExportsDir).Path

# ── 前置检查 ────────────────────────────────────────────────────────────────
if (-not $ConnectionString) {
    Write-Error "未提供连接字符串。请用 -ConnectionString 参数传入，或设置环境变量 SUPABASE_CONN。"
    exit 1
}

if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    Write-Error "找不到 psql。请确认 PostgreSQL 客户端工具已安装并在 PATH 中。"
    exit 1
}

if (-not (Test-Path $ExportsDir)) {
    Write-Error "exports 目录不存在：$ExportsDir"
    exit 1
}

# ── 收集 SQL 文件 ───────────────────────────────────────────────────────────
$sqlFiles = Get-ChildItem -Path $ExportsDir -Recurse -Filter "import.sql" |
    Where-Object { -not $Filter -or $_.DirectoryName -match [regex]::Escape($Filter) } |
    Sort-Object FullName

if ($sqlFiles.Count -eq 0) {
    Write-Host "没有找到匹配的 import.sql 文件。ExportsDir=$ExportsDir  Filter='$Filter'" -ForegroundColor Yellow
    exit 0
}

# ── 执行 ─────────────────────────────────────────────────────────────────────
$total   = $sqlFiles.Count
$success = 0
$failed  = 0
$results = @()

Write-Host ""
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan
if ($DryRun) {
    Write-Host "  DRY RUN — 不会实际写库" -ForegroundColor Yellow
}
Write-Host "  共 $total 个 import.sql 待执行" -ForegroundColor Cyan
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

$i = 0
foreach ($file in $sqlFiles) {
    $i++
    $relPath  = $file.FullName.Replace($ExportsDir, "").TrimStart('\','/')
    $dirName  = Split-Path -Leaf $file.DirectoryName

    Write-Host "[$i/$total] $dirName" -NoNewline

    if ($DryRun) {
        Write-Host "  → (dry-run 跳过)" -ForegroundColor DarkGray
        $results += [PSCustomObject]@{ Dir = $dirName; Status = "dry-run"; Error = "" }
        continue
    }

    $startTime = Get-Date
    # psql 遇到错误时仍会返回 0；用 ON_ERROR_STOP 让它返回非 0
    $output = psql $ConnectionString `
                   --set ON_ERROR_STOP=1 `
                   -f $file.FullName 2>&1
    $exitCode = $LASTEXITCODE
    $elapsed  = [int](New-TimeSpan -Start $startTime -End (Get-Date)).TotalSeconds

    if ($exitCode -eq 0) {
        $success++
        Write-Host "  ✓ ${elapsed}s" -ForegroundColor Green
        $results += [PSCustomObject]@{ Dir = $dirName; Status = "OK"; Error = "" }
    } else {
        $failed++
        $errMsg = ($output | Where-Object { $_ -match "ERROR|FATAL" }) -join "; "
        Write-Host "  ✗ 失败 (exit $exitCode)" -ForegroundColor Red
        Write-Host "    $errMsg" -ForegroundColor DarkRed
        $results += [PSCustomObject]@{ Dir = $dirName; Status = "FAIL"; Error = $errMsg }
    }
}

# ── 汇总 ─────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  汇总：成功 $success  失败 $failed  共 $total" -ForegroundColor Cyan
Write-Host "══════════════════════════════════════════════" -ForegroundColor Cyan

if ($failed -gt 0) {
    Write-Host ""
    Write-Host "失败明细：" -ForegroundColor Red
    $results | Where-Object { $_.Status -eq "FAIL" } | ForEach-Object {
        Write-Host "  - $($_.Dir)" -ForegroundColor Red
        if ($_.Error) { Write-Host "    $($_.Error)" -ForegroundColor DarkRed }
    }
    exit 1
}

exit 0

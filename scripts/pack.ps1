#Requires -Version 7
<#
.SYNOPSIS
    LabelPrintClient 一键打包脚本

.DESCRIPTION
    自动完成：restore → build → test → publish → vpk pack → zip
    版本号从 LabelPrintClient.csproj 自动读取。

.PARAMETER SkipTests
    跳过测试步骤。

.PARAMETER SkipBuild
    跳过 restore / build，直接从 publish 开始（适合重复打包同一版本）。

.EXAMPLE
    .\scripts\pack.ps1
    .\scripts\pack.ps1 -SkipTests
    .\scripts\pack.ps1 -SkipBuild -SkipTests
#>
[CmdletBinding()]
param(
    [switch] $SkipTests,
    [switch] $SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── 路径常量 ────────────────────────────────────────────────────────────────
$Root        = Resolve-Path "$PSScriptRoot\.."
$Sln         = "$Root\LabelPrintClient.sln"
$ClientCsproj= "$Root\src\LabelPrintClient\LabelPrintClient.csproj"
$PublishDir  = "$Root\artifacts\publish\LabelPrintClient"
$ReleasesDir = "$Root\artifacts\releases"

# ── 工具函数 ────────────────────────────────────────────────────────────────
function Write-Step([string]$msg) {
    Write-Host "`n▶  $msg" -ForegroundColor Cyan
}

function Write-Ok([string]$msg) {
    Write-Host "✔  $msg" -ForegroundColor Green
}

function Invoke-Cmd([string]$desc, [scriptblock]$cmd) {
    Write-Step $desc
    & $cmd
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✘  失败（exit $LASTEXITCODE）：$desc" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Ok $desc
}

# ── 读取版本号 ───────────────────────────────────────────────────────────────
Write-Step "读取版本号"
[xml]$csproj = Get-Content $ClientCsproj -Raw
$Version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $Version) {
    Write-Host "✘  无法从 csproj 读取 <Version>" -ForegroundColor Red
    exit 1
}
Write-Ok "版本：$Version"

$ReleaseDir = "$ReleasesDir\LabelPrintClient-$Version"
$ZipPath    = "$ReleasesDir\LabelPrintClient-$Version.zip"

# ── 检查 vpk ─────────────────────────────────────────────────────────────────
Write-Step "检查 vpk CLI"
$vpkHelp = vpk --help 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "✘  未找到 vpk，请先安装：dotnet tool install --global vpk --version 1.0.1" -ForegroundColor Red
    exit 1
}
$vpkInfo = $vpkHelp | Select-Object -First 1
if (-not $vpkInfo) {
    $vpkInfo = "vpk CLI found"
}
Write-Ok $vpkInfo

# ── Restore / Build ──────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Invoke-Cmd "dotnet restore" {
        dotnet restore $Sln
    }

    Invoke-Cmd "dotnet build (Release)" {
        dotnet build $Sln -c Release --no-restore
    }
}

# ── Tests ────────────────────────────────────────────────────────────────────
if (-not $SkipTests) {
    Invoke-Cmd "LicenseServer 测试" {
        dotnet test "$Root\tests\LicenseServer.Tests\LicenseServer.Tests.csproj" `
            -c Release --no-build --logger "console;verbosity=minimal"
    }

    Invoke-Cmd "LabelPrintClient 更新相关测试" {
        dotnet test "$Root\tests\LabelPrintClient.Tests\LabelPrintClient.Tests.csproj" `
            -c Release --no-build --filter "FullyQualifiedName~Update" `
            --logger "console;verbosity=minimal"
    }
}

# ── Publish ──────────────────────────────────────────────────────────────────
Invoke-Cmd "dotnet publish (win-x64, self-contained)" {
    dotnet publish $ClientCsproj `
        -c Release -r win-x64 --self-contained true `
        -o $PublishDir
}

# ── vpk pack ─────────────────────────────────────────────────────────────────
if (Test-Path $ReleaseDir) {
    Write-Step "清理旧 release 目录：$ReleaseDir"
    Remove-Item $ReleaseDir -Recurse -Force
}

Invoke-Cmd "vpk pack" {
    vpk pack `
        --packId      LabelPrintClient `
        --packVersion $Version `
        --channel     stable `
        --packDir     $PublishDir `
        --mainExe     LabelPrintClient.exe `
        --runtime     win-x64 `
        --outputDir   $ReleaseDir
}

# ── 验证 release 产物 ─────────────────────────────────────────────────────────
Write-Step "验证 release 产物"
$requiredPatterns = @('*.nupkg', '*Setup.exe')
foreach ($pattern in $requiredPatterns) {
    $found = Get-ChildItem $ReleaseDir -Filter $pattern -ErrorAction SilentlyContinue
    if (-not $found) {
        Write-Host "✘  缺少必要文件：$pattern" -ForegroundColor Red
        exit 1
    }
}
$hasFeed = (Test-Path "$ReleaseDir\RELEASES") -or
           (Test-Path "$ReleaseDir\releases.stable.json")
if (-not $hasFeed) {
    Write-Host "✘  缺少 Velopack feed 文件（RELEASES 或 releases.stable.json）" -ForegroundColor Red
    exit 1
}
Write-Ok "产物验证通过"

# ── 压缩 zip ──────────────────────────────────────────────────────────────────
if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Invoke-Cmd "压缩为 zip" {
    Compress-Archive -Path "$ReleaseDir\*" -DestinationPath $ZipPath -Force
}

# ── 完成摘要 ──────────────────────────────────────────────────────────────────
$zipSize = [math]::Round((Get-Item $ZipPath).Length / 1MB, 2)

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
Write-Host "  打包完成" -ForegroundColor Green
Write-Host "  版本：$Version" -ForegroundColor Green
Write-Host "  zip ：$ZipPath  ($zipSize MB)" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
Write-Host ""
Write-Host "下一步：在 LicenseServer 后台 /Updates 页面上传此 zip" -ForegroundColor Yellow
Write-Host "  Product Code : LABEL_PRINT_CLIENT"
Write-Host "  Channel      : stable"
Write-Host "  Version      : $Version"

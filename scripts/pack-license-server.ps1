#Requires -Version 7
<#
.SYNOPSIS
    LicenseServer 一键发布脚本

.DESCRIPTION
    自动完成：restore → build → test → publish → zip
    默认发布为 win-x64 self-contained，并保留 appsettings.json 供部署后修改。

.PARAMETER SkipTests
    跳过测试步骤。

.PARAMETER SkipBuild
    跳过 restore / build，直接从 publish 开始。

.PARAMETER Runtime
    目标运行时，默认 win-x64。

.PARAMETER FrameworkDependent
    发布为 framework-dependent，而不是 self-contained。

.PARAMETER Version
    手动覆盖版本号；默认从 LicenseServer.csproj 的 <Version> 读取。

.PARAMETER SkipZip
    跳过 zip 压缩，仅保留 publish 目录。

.EXAMPLE
    .\scripts\pack-license-server.ps1
    .\scripts\pack-license-server.ps1 -SkipTests
    .\scripts\pack-license-server.ps1 -Runtime 'linux-x64' -FrameworkDependent
#>
[CmdletBinding()]
param(
    [switch] $SkipTests,
    [switch] $SkipBuild,
    [string] $Runtime = 'win-x64',
    [switch] $FrameworkDependent,
    [string] $Version,
    [switch] $SkipZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Resolve-Path "$PSScriptRoot\.."
$Sln = "$Root\LabelPrintClient.sln"
$ServerCsproj = "$Root\src\LicenseServer\LicenseServer.csproj"
$ReleasesDir = "$Root\artifacts\releases\LicenseServer"
$IsSelfContained = -not $FrameworkDependent
$PublishKind = if ($IsSelfContained) { 'self-contained' } else { 'framework-dependent' }

function Write-Step([string]$Message) {
    Write-Host "`n▶  $Message" -ForegroundColor Cyan
}

function Write-Ok([string]$Message) {
    Write-Host "✔  $Message" -ForegroundColor Green
}

function Invoke-Cmd([string]$Description, [scriptblock]$Command) {
    Write-Step $Description
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✘  失败（exit $LASTEXITCODE）：$Description" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Ok $Description
}

function Remove-DirectorySafely([string]$TargetPath, [string]$AllowedRootPath) {
    if (-not (Test-Path -LiteralPath $TargetPath)) {
        return
    }

    $targetFull = [System.IO.Path]::GetFullPath($TargetPath)
    $allowedRootFull = [System.IO.Path]::GetFullPath($AllowedRootPath)
    $allowedPrefix = $allowedRootFull.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $targetFull.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "✘  目标目录不在允许范围内：$targetFull" -ForegroundColor Red
        exit 1
    }

    Remove-Item -LiteralPath $TargetPath -Recurse -Force
}

Write-Step "读取版本号"
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$serverProjectXml = Get-Content $ServerCsproj -Raw
    $Version = $serverProjectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "✘  无法确定版本号，请在 LicenseServer.csproj 中设置 <Version> 或传入 -Version。" -ForegroundColor Red
    exit 1
}
Write-Ok "版本：$Version"

$ReleaseName = "LicenseServer-$Version-$Runtime"
if (-not $IsSelfContained) {
    $ReleaseName += '-fd'
}
$PublishDir = "$Root\artifacts\publish\LicenseServer\$Version\$Runtime"
$ZipPath = "$ReleasesDir\$ReleaseName.zip"
New-Item -ItemType Directory -Force -Path $ReleasesDir | Out-Null

if (-not $SkipBuild) {
    Invoke-Cmd "dotnet restore" {
        dotnet restore $Sln
    }

    Invoke-Cmd "dotnet build (Release)" {
        dotnet build $Sln -c Release --no-restore
    }
}

if (-not $SkipTests) {
    Invoke-Cmd "LicenseServer 测试" {
        dotnet test "$Root\tests\LicenseServer.Tests\LicenseServer.Tests.csproj" `
            -c Release --no-build --logger "console;verbosity=minimal"
    }
}

Write-Step "清理旧 publish 目录：$PublishDir"
Remove-DirectorySafely $PublishDir "$Root\artifacts\publish"
Write-Ok "publish 目录已清理"

$publishArguments = @(
    'publish'
    $ServerCsproj
    '-c'
    'Release'
    '-r'
    $Runtime
    '--self-contained'
    ($IsSelfContained ? 'true' : 'false')
    '-o'
    $PublishDir
)

Invoke-Cmd "dotnet publish ($Runtime, $PublishKind)" {
    dotnet @publishArguments
}

Write-Step "验证 publish 产物"
$requiredFiles = @(
    "$PublishDir\appsettings.json",
    "$PublishDir\wwwroot"
)
foreach ($requiredPath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        Write-Host "✘  缺少必要产物：$requiredPath" -ForegroundColor Red
        exit 1
    }
}

$entryDll = "$PublishDir\LicenseServer.dll"
$entryExe = "$PublishDir\LicenseServer.exe"
if (-not (Test-Path -LiteralPath $entryDll) -and -not (Test-Path -LiteralPath $entryExe)) {
    Write-Host "✘  缺少入口文件：LicenseServer.dll / LicenseServer.exe" -ForegroundColor Red
    exit 1
}
Write-Ok "publish 产物验证通过"

if (-not $SkipZip) {
    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    Invoke-Cmd "压缩发布目录为 zip" {
        Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -Force
    }
}

$publishItems = Get-ChildItem -LiteralPath $PublishDir
$publishSizeMb = [math]::Round((($publishItems | Measure-Object -Property Length -Sum).Sum) / 1MB, 2)

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
Write-Host "  LicenseServer 发布完成" -ForegroundColor Green
Write-Host "  版本：$Version" -ForegroundColor Green
Write-Host "  Runtime：$Runtime" -ForegroundColor Green
Write-Host "  类型：$PublishKind" -ForegroundColor Green
Write-Host "  publish：$PublishDir  ($publishSizeMb MB)" -ForegroundColor Green
if (-not $SkipZip) {
    $zipSizeMb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 2)
    Write-Host "  zip：$ZipPath  ($zipSizeMb MB)" -ForegroundColor Green
}
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Green
Write-Host ""
Write-Host "部署说明：" -ForegroundColor Yellow
Write-Host "  1. 修改 publish 目录中的 appsettings.json 或通过环境变量覆盖 LicenseServer 配置。"
Write-Host "  2. 公司端请配置 LICENSE_SERVER_MASTER_KEY；客户端模式不要配置该变量。"
Write-Host "  3. 启动命令示例：dotnet LicenseServer.dll"

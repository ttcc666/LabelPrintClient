#Requires -Version 7
<#
.SYNOPSIS
    快速启动 LicenseServer 公司端 / 客户端两种模式。

.DESCRIPTION
    Company 模式会配置 LICENSE_SERVER_MASTER_KEY，启用私钥导入和许可证签发能力。
    Client 模式会清空 LICENSE_SERVER_MASTER_KEY，进入客户现场局域网托管模式。

    两种模式默认使用不同的 endpoint、数据库文件和更新包目录，避免本地联调时互相污染。

.PARAMETER Mode
    启动模式：Company、Client 或 All。

.PARAMETER Stop
    停止脚本之前启动的对应模式进程。

.PARAMETER SkipBuild
    跳过启动前构建。已有 LicenseServer 实例运行时可使用，避免构建输出被运行中进程锁定。

.EXAMPLE
    .\scripts\start-license-server.ps1 -Mode 'Company'
    .\scripts\start-license-server.ps1 -Mode 'Client'
    .\scripts\start-license-server.ps1 -Mode 'All'
    .\scripts\start-license-server.ps1 -Mode 'All' -Stop
    .\scripts\start-license-server.ps1 -Mode 'Client' -SkipBuild
#>
[CmdletBinding()]
param(
    [ValidateSet('Company', 'Client', 'All')]
    [string] $Mode = 'All',

    [switch] $Stop,

    [switch] $SkipBuild,

    [string] $CompanyUrl = 'http://127.0.0.1:5051',

    [string] $ClientUrl = 'http://127.0.0.1:5052',

    [ValidateSet('LocalSqlite', 'PostgreSql')]
    [string] $CompanyDatabase = 'LocalSqlite',

    [ValidateSet('LocalSqlite', 'PostgreSql')]
    [string] $ClientDatabase = 'LocalSqlite',

    [string] $CompanySqliteConnection = 'DataSource=Data/company/license_server.db',

    [string] $ClientSqliteConnection = 'DataSource=Data/client/license_server.db',

    [string] $CompanyPostgreSqlConnection = 'Host=127.0.0.1;Port=5432;Username=postgres;Database=license_server_company;',

    [string] $ClientPostgreSqlConnection = 'Host=127.0.0.1;Port=5432;Username=postgres;Database=license_server_client;',

    [string] $CompanyMasterKey = 'dev-company-license-server-master-key',

    [string] $CompanyUpdatePackageRoot = 'Data/company/updates',

    [string] $ClientUpdatePackageRoot = 'Data/client/updates',

    [switch] $Foreground
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Resolve-Path "$PSScriptRoot\.."
$Project = "$Root\src\LicenseServer\LicenseServer.csproj"
$RunStateRoot = "$Root\artifacts\license-server"
$LogRoot = "$RunStateRoot\logs"

function Write-Step([string] $Message) {
    Write-Host "`n▶  $Message" -ForegroundColor Cyan
}

function Write-Ok([string] $Message) {
    Write-Host "✔  $Message" -ForegroundColor Green
}

function Write-Warn([string] $Message) {
    Write-Host "⚠  $Message" -ForegroundColor Yellow
}

function Invoke-Cmd([string] $Description, [scriptblock] $Command) {
    Write-Step $Description
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✘  失败（exit $LASTEXITCODE）：$Description" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Ok $Description
}

function Get-SelectedModes {
    if ($Mode -eq 'All') {
        return @('Company', 'Client')
    }

    return @($Mode)
}

function Get-PidPath([string] $SelectedMode) {
    return "$RunStateRoot\license-server-$($SelectedMode.ToLowerInvariant()).pid"
}

function Get-LogPath([string] $SelectedMode) {
    return "$LogRoot\license-server-$($SelectedMode.ToLowerInvariant()).log"
}

function Get-ErrorLogPath([string] $SelectedMode) {
    return "$LogRoot\license-server-$($SelectedMode.ToLowerInvariant()).err.log"
}

function Test-TrackedProcessRunning {
    foreach ($candidateMode in @('Company', 'Client')) {
        $pidPath = Get-PidPath $candidateMode
        if (-not (Test-Path -LiteralPath $pidPath)) {
            continue
        }

        $processIdText = (Get-Content -LiteralPath $pidPath -Raw).Trim()
        $parsedProcessId = 0
        if (-not [int]::TryParse($processIdText, [ref] $parsedProcessId)) {
            continue
        }

        if ((Get-Process -Id $parsedProcessId -ErrorAction SilentlyContinue) -ne $null) {
            return $true
        }
    }

    return $false
}

function Get-ModeSettings([string] $SelectedMode) {
    if ($SelectedMode -eq 'Company') {
        return [ordered]@{
            DisplayName = '公司端模式'
            Url = $CompanyUrl
            DatabaseMode = $CompanyDatabase
            SqliteConnection = $CompanySqliteConnection
            PostgreSqlConnection = $CompanyPostgreSqlConnection
            MasterKey = $CompanyMasterKey
            UpdatePackageRoot = $CompanyUpdatePackageRoot
        }
    }

    return [ordered]@{
        DisplayName = '客户端模式'
        Url = $ClientUrl
        DatabaseMode = $ClientDatabase
        SqliteConnection = $ClientSqliteConnection
        PostgreSqlConnection = $ClientPostgreSqlConnection
        MasterKey = $null
        UpdatePackageRoot = $ClientUpdatePackageRoot
    }
}

function Stop-LicenseServerMode([string] $SelectedMode) {
    $pidPath = Get-PidPath $SelectedMode
    if (-not (Test-Path -LiteralPath $pidPath)) {
        Write-Warn "$SelectedMode 未找到 PID 文件，跳过。"
        return
    }

    $processIdText = (Get-Content -LiteralPath $pidPath -Raw).Trim()
    $parsedProcessId = 0
    if (-not [int]::TryParse($processIdText, [ref] $parsedProcessId)) {
        Remove-Item -LiteralPath $pidPath -Force
        Write-Warn "$SelectedMode PID 文件无效，已删除。"
        return
    }

    $processId = $parsedProcessId
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($process -eq $null) {
        Remove-Item -LiteralPath $pidPath -Force
        Write-Warn "$SelectedMode 进程 $processId 不存在，已清理 PID 文件。"
        return
    }

    Write-Step "停止 $SelectedMode 进程：$processId"
    Stop-Process -Id $processId
    Remove-Item -LiteralPath $pidPath -Force
    Write-Ok "$SelectedMode 已停止。"
}

function New-Environment($Settings) {
    $environment = @{
        'ASPNETCORE_ENVIRONMENT' = 'Development'
        'ASPNETCORE_URLS' = $Settings.Url
        'LicenseServer__RunMode' = $Settings.DatabaseMode
        'LicenseServer__SqliteConnection' = $Settings.SqliteConnection
        'LicenseServer__PostgreSqlConnection' = $Settings.PostgreSqlConnection
        'LicenseServer__UpdatePackageRoot' = $Settings.UpdatePackageRoot
        'LicenseServer__MasterKey' = ''
        'LICENSE_SERVER_MASTER_KEY' = ''
    }

    if (-not [string]::IsNullOrWhiteSpace($Settings.MasterKey)) {
        $environment['LICENSE_SERVER_MASTER_KEY'] = $Settings.MasterKey
    }

    return $environment
}

function Start-LicenseServerMode([string] $SelectedMode) {
    $settings = Get-ModeSettings $SelectedMode
    $pidPath = Get-PidPath $SelectedMode
    $logPath = Get-LogPath $SelectedMode
    $errorLogPath = Get-ErrorLogPath $SelectedMode
    $existingPid = $null

    if (Test-Path -LiteralPath $pidPath) {
        $existingPidText = (Get-Content -LiteralPath $pidPath -Raw).Trim()
        $parsedExistingPid = 0
        if ([int]::TryParse($existingPidText, [ref] $parsedExistingPid)) {
            $existing = Get-Process -Id $parsedExistingPid -ErrorAction SilentlyContinue
            if ($existing -ne $null) {
                Write-Warn "$SelectedMode 已在运行：PID $parsedExistingPid。先执行 -Stop 或换端口。"
                return
            }
        }

        Remove-Item -LiteralPath $pidPath -Force
    }

    New-Item -ItemType Directory -Force -Path $RunStateRoot, $LogRoot | Out-Null
    if (Test-Path -LiteralPath $logPath) {
        Remove-Item -LiteralPath $logPath -Force
    }
    if (Test-Path -LiteralPath $errorLogPath) {
        Remove-Item -LiteralPath $errorLogPath -Force
    }

    $environment = New-Environment $settings
    $arguments = @(
        'run'
        '--project'
        $Project
        '--no-launch-profile'
        '--no-build'
    )

    Write-Step "启动 $($settings.DisplayName)：$($settings.Url)"
    Write-Host "   Database : $($settings.DatabaseMode)"
    Write-Host "   SQLite   : $($settings.SqliteConnection)"
    Write-Host "   PostgreSQL: $($settings.PostgreSqlConnection)"
    Write-Host "   Updates  : $($settings.UpdatePackageRoot)"
    Write-Host "   Log      : $logPath"
    Write-Host "   ErrorLog : $errorLogPath"

    if ($Foreground) {
        $previousEnvironment = @{}
        foreach ($key in $environment.Keys) {
            $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            [Environment]::SetEnvironmentVariable($key, $environment[$key], 'Process')
        }

        try {
            & dotnet @arguments
            if ($LASTEXITCODE -ne 0) {
                exit $LASTEXITCODE
            }
        }
        finally {
            foreach ($key in $previousEnvironment.Keys) {
                [Environment]::SetEnvironmentVariable($key, $previousEnvironment[$key], 'Process')
            }
        }

        return
    }

    $process = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList $arguments `
        -WorkingDirectory $Root `
        -Environment $environment `
        -RedirectStandardOutput $logPath `
        -RedirectStandardError $errorLogPath `
        -WindowStyle Hidden `
        -PassThru

    Set-Content -LiteralPath $pidPath -Value $process.Id
    Write-Ok "$SelectedMode 已启动：PID $($process.Id)，URL $($settings.Url)"
}

if (-not (Test-Path -LiteralPath $Project)) {
    throw "找不到 LicenseServer 项目：$Project"
}

$selectedModes = Get-SelectedModes

if ($Stop) {
    foreach ($selectedMode in $selectedModes) {
        Stop-LicenseServerMode $selectedMode
    }

    exit 0
}

if ($Foreground -and $selectedModes.Count -gt 1) {
    throw 'Foreground 模式一次只能启动一个服务，请使用 -Mode Company 或 -Mode Client。'
}

if (-not $SkipBuild) {
    if (Test-TrackedProcessRunning) {
        Write-Warn '检测到已有脚本启动的 LicenseServer 进程，跳过 build，避免运行中 DLL 被锁定。'
    }
    else {
        Invoke-Cmd 'dotnet build LicenseServer' {
            dotnet build $Project
        }
    }
}

foreach ($selectedMode in $selectedModes) {
    Start-LicenseServerMode $selectedMode
}

if (-not $Foreground) {
    Write-Host ''
    Write-Host '常用命令：'
    Write-Host "  查看日志：Get-Content -LiteralPath '$LogRoot\license-server-company.log' -Wait"
    Write-Host "  查看错误：Get-Content -LiteralPath '$LogRoot\license-server-company.err.log' -Wait"
    Write-Host "  停止全部：.\scripts\start-license-server.ps1 -Mode 'All' -Stop"
}

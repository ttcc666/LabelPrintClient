using System.Reflection;
using System.Net.Http;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using LabelPrintClient.Config;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Services;
using Velopack;
using Velopack.Locators;

namespace LabelPrintClient.Modules.Update.Services;

public static class ClientUpdateService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string CurrentVersion
    {
        get
        {
            var assembly = typeof(App).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Split('+')[0];

            return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        }
    }

    public static async Task<UpdateCheckOutcome> CheckAndPromptAsync(AppSettings settings, bool manual, CancellationToken cancellationToken = default)
    {
        if (!manual && !settings.AutoCheckUpdates)
            return UpdateCheckOutcome.Skipped;

        if (!await Gate.WaitAsync(0, cancellationToken))
        {
            if (manual)
                AppMessageBox.Warning(AppLanguageService.GetString("Update.AlreadyRunning"), AppLanguageService.GetString("Update.Title"));
            return UpdateCheckOutcome.Skipped;
        }

        try
        {
            var latest = await GetLatestAsync(settings, cancellationToken);
            if (latest == null || !latest.HasUpdate)
            {
                if (manual)
                    AppMessageBox.Info(AppLanguageService.Format("Update.NoUpdate", CurrentVersion), AppLanguageService.GetString("Update.Title"));
                return UpdateCheckOutcome.NoUpdate;
            }

            return await PromptAndInstallAsync(settings, latest, manual, cancellationToken);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("检查更新失败", ex);
            if (manual)
                AppMessageBox.Warning(AppLanguageService.Format("Update.CheckFailed", ex.Message), AppLanguageService.GetString("Update.Title"));
            return UpdateCheckOutcome.Failed;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<UpdateLatestResponse?> GetLatestAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.UpdateServerUrl))
            return null;

        if (!Uri.TryCreate(settings.UpdateServerUrl.Trim(), UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(AppLanguageService.GetString("Update.ServerUrlInvalid"));
        }

        var productCode = Uri.EscapeDataString(settings.ProductCode);
        var channel = Uri.EscapeDataString(string.IsNullOrWhiteSpace(settings.UpdateChannel) ? "stable" : settings.UpdateChannel.Trim());
        var currentVersion = Uri.EscapeDataString(CurrentVersion);
        var latestUri = new Uri(baseUri, $"api/update/latest?productCode={productCode}&channel={channel}&currentVersion={currentVersion}");

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        using var response = await http.GetAsync(latestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<UpdateLatestResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<UpdateCheckOutcome> PromptAndInstallAsync(
        AppSettings settings,
        UpdateLatestResponse latest,
        bool manual,
        CancellationToken cancellationToken)
    {
        var releaseNotes = string.IsNullOrWhiteSpace(latest.ReleaseNotes)
            ? AppLanguageService.GetString("Update.NoReleaseNotes")
            : latest.ReleaseNotes.Trim();
        var promptKey = latest.IsMandatory ? "Update.MandatoryPrompt" : "Update.AvailablePrompt";
        var prompt = AppLanguageService.Format(promptKey, latest.Version, releaseNotes);
        var confirm = AppMessageBox.Show(
            prompt,
            AppLanguageService.GetString("Update.Title"),
            MessageBoxButton.YesNo,
            latest.IsMandatory ? MessageBoxImage.Warning : MessageBoxImage.Information);

        if (confirm != MessageBoxResult.Yes)
        {
            if (latest.IsMandatory)
                System.Windows.Application.Current.Shutdown();
            return UpdateCheckOutcome.Skipped;
        }

        if (string.IsNullOrWhiteSpace(latest.VelopackBaseUrl))
            throw new InvalidOperationException("服务端未返回 Velopack 更新源。");

        var manager = new UpdateManager(latest.VelopackBaseUrl, new UpdateOptions
        {
            ExplicitChannel = string.IsNullOrWhiteSpace(settings.UpdateChannel) ? "stable" : settings.UpdateChannel.Trim()
        }, null);

        if (!manager.IsInstalled)
        {
            var message = AppLanguageService.GetString("Update.NotVelopackInstalled");
            AppMessageBox.Warning(message, AppLanguageService.GetString("Update.Title"));
            if (latest.IsMandatory)
                System.Windows.Application.Current.Shutdown();
            return UpdateCheckOutcome.NotInstalled;
        }

        var pendingUpdate = manager.UpdatePendingRestart;
        if (pendingUpdate != null)
        {
            AppLogger.LogInfo($"检测到已下载待应用更新：{pendingUpdate.Version}");
            return PromptRestartAndApply(pendingUpdate, latest.IsMandatory);
        }

        AppMessageBox.Info(AppLanguageService.Format("Update.Downloading", latest.Version), AppLanguageService.GetString("Update.Title"));
        var updateInfo = await manager.CheckForUpdatesAsync();
        if (updateInfo == null)
        {
            if (manual)
                AppMessageBox.Info(AppLanguageService.GetString("Update.VelopackNoUpdate"), AppLanguageService.GetString("Update.Title"));
            return UpdateCheckOutcome.NoUpdate;
        }

        await manager.DownloadUpdatesAsync(updateInfo, progress =>
        {
            if (progress is 0 or 100 || progress % 25 == 0)
                AppLogger.LogInfo($"更新下载进度：{progress}%");
        }, cancellationToken);

        return PromptRestartAndApply(updateInfo.TargetFullRelease, latest.IsMandatory);
    }

    private static UpdateCheckOutcome PromptRestartAndApply(VelopackAsset targetRelease, bool isMandatory)
    {
        var restart = AppMessageBox.Show(
            AppLanguageService.Format("Update.ReadyRestart", targetRelease.Version),
            AppLanguageService.GetString("Update.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (restart != MessageBoxResult.Yes)
        {
            if (isMandatory)
                System.Windows.Application.Current.Shutdown();
            return UpdateCheckOutcome.Skipped;
        }

        AppLogger.LogInfo($"准备应用更新并重启：{targetRelease.Version}");
        StartUpdaterAfterCurrentProcessExits(targetRelease);
        System.Windows.Application.Current.Shutdown();
        return UpdateCheckOutcome.UpdateStarted;
    }

    private static void StartUpdaterAfterCurrentProcessExits(VelopackAsset targetRelease)
    {
        var locator = VelopackLocator.Current;
        if (string.IsNullOrWhiteSpace(locator.PackagesDir) ||
            string.IsNullOrWhiteSpace(locator.UpdateExePath) ||
            string.IsNullOrWhiteSpace(locator.RootAppDir))
        {
            throw new InvalidOperationException("Velopack 安装路径信息不完整，无法应用更新。");
        }

        var packagePath = Path.Combine(locator.PackagesDir, targetRelease.FileName);
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("已下载的更新包不存在。", packagePath);

        var helperDirectory = Path.Combine(locator.PackagesDir, "VelopackTemp");
        Directory.CreateDirectory(helperDirectory);

        var helperScriptPath = Path.Combine(helperDirectory, $"apply-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(helperScriptPath, BuildApplyHelperScript());

        var process = Process.GetCurrentProcess();
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(helperScriptPath);
        startInfo.ArgumentList.Add("-ParentPid");
        startInfo.ArgumentList.Add(process.Id.ToString());
        startInfo.ArgumentList.Add("-WaitTimeoutSeconds");
        startInfo.ArgumentList.Add("120");
        startInfo.ArgumentList.Add("-UpdateExePath");
        startInfo.ArgumentList.Add(locator.UpdateExePath);
        startInfo.ArgumentList.Add("-PackagePath");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add("-RootAppDir");
        startInfo.ArgumentList.Add(locator.RootAppDir);
        startInfo.ArgumentList.Add("-PackagesDir");
        startInfo.ArgumentList.Add(locator.PackagesDir);

        Process.Start(startInfo);
    }

    private static string BuildApplyHelperScript()
    {
        return """
param(
    [Parameter(Mandatory=$true)][int]$ParentPid,
    [Parameter(Mandatory=$true)][int]$WaitTimeoutSeconds,
    [Parameter(Mandatory=$true)][string]$UpdateExePath,
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [Parameter(Mandatory=$true)][string]$RootAppDir,
    [Parameter(Mandatory=$true)][string]$PackagesDir
)

$ErrorActionPreference = 'Stop'
$deadline = (Get-Date).AddSeconds($WaitTimeoutSeconds)

do {
    $parent = Get-Process -Id $ParentPid -ErrorAction SilentlyContinue
    if ($null -eq $parent) {
        break
    }

    Start-Sleep -Milliseconds 500
} while ((Get-Date) -lt $deadline)

if ($null -ne (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue)) {
    exit 1460
}

& $UpdateExePath apply --package $PackagePath --rootDir $RootAppDir --packageDir $PackagesDir
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    exit $exitCode
}

try {
    Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
} catch {
}
""";
    }
}

public enum UpdateCheckOutcome
{
    Skipped,
    NoUpdate,
    NotInstalled,
    UpdateStarted,
    Failed
}

public sealed record UpdateLatestResponse(
    bool HasUpdate,
    string? ProductCode,
    string? Channel,
    string? Version,
    string? ReleaseNotes,
    bool IsMandatory,
    string? Sha256,
    long FileSizeBytes,
    string? VelopackBaseUrl,
    string? VelopackFeedUrl,
    string? SetupDownloadUrl);

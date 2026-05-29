using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LicenseServer.Config;
using LicenseServer.Infrastructure;
using LicenseServer.Models;
using Microsoft.Extensions.Options;

namespace LicenseServer.Services;

public sealed class UpdateReleaseService
{
    private static readonly Regex SafeSegmentRegex = new("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
    private readonly LicenseDb _db;
    private readonly LicenseServerOptions _options;

    public UpdateReleaseService(LicenseDb db, IOptions<LicenseServerOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<List<UpdateRelease>> GetAllAsync()
    {
        return await _db.Db.Queryable<UpdateRelease>()
            .OrderByDescending(x => x.CreateTime)
            .ToListAsync();
    }

    public async Task<UpdateRelease?> GetLatestAsync(string productCode, string channel, string? currentVersion = null)
    {
        var normalizedProduct = NormalizeSegment(productCode, "产品编码");
        var normalizedChannel = NormalizeSegment(channel, "更新通道");
        var releases = await _db.Db.Queryable<UpdateRelease>()
            .Where(x => x.ProductCode == normalizedProduct && x.Channel == normalizedChannel && x.IsEnabled)
            .ToListAsync();

        var candidates = releases
            .Where(x => string.IsNullOrWhiteSpace(currentVersion) || IsNewerVersion(x.Version, currentVersion))
            .OrderByDescending(x => ParseVersionOrDefault(x.Version))
            .ThenByDescending(x => x.CreateTime)
            .ToList();

        return candidates.FirstOrDefault();
    }

    public async Task<UpdateRelease> UploadAsync(UpdateReleaseUploadRequest request, Stream releaseZip, CancellationToken cancellationToken = default)
    {
        var productCode = NormalizeSegment(request.ProductCode, "产品编码");
        var channel = NormalizeSegment(request.Channel, "更新通道");
        var version = NormalizeVersion(request.Version);

        if (releaseZip.CanSeek && releaseZip.Length > _options.MaxUpdateUploadBytes)
            throw new InvalidOperationException($"更新包超过允许大小：{_options.MaxUpdateUploadBytes / 1024 / 1024} MB。");

        var root = ResolvePackageRoot();
        Directory.CreateDirectory(root);
        var targetRelative = ToRelativePath(Path.Combine(productCode, channel, version));
        var targetDirectory = EnsureUnderRoot(root, Path.Combine(root, targetRelative));
        var incomingRoot = EnsureUnderRoot(root, Path.Combine(root, ".incoming"));
        Directory.CreateDirectory(incomingRoot);
        var tempDirectory = EnsureUnderRoot(root, Path.Combine(incomingRoot, IdHelper.NewId().ToString()));

        try
        {
            Directory.CreateDirectory(tempDirectory);
            ExtractZipSafely(releaseZip, tempDirectory);

            var releaseDirectory = FindVelopackReleaseDirectory(tempDirectory, channel);
            var feedPath = FindFeedFile(releaseDirectory, channel)
                ?? throw new InvalidOperationException("更新包缺少 Velopack feed 文件：RELEASES 或 releases.{channel}.json。");
            var packageFiles = Directory.EnumerateFiles(releaseDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (packageFiles.Count == 0)
                throw new InvalidOperationException("更新包缺少 Velopack .nupkg 文件。");

            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);
            Directory.CreateDirectory(targetDirectory);
            CopyDirectory(releaseDirectory, targetDirectory);

            var copiedFiles = Directory.EnumerateFiles(targetDirectory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var copiedFeed = Path.Combine(targetDirectory, Path.GetFileName(feedPath));
            var primaryPackage = copiedFiles.FirstOrDefault(x => x.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                ?? copiedFeed;
            var setupFile = copiedFiles
                .Where(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => Path.GetFileName(x).Contains("Setup", StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            var existing = await _db.Db.Queryable<UpdateRelease>()
                .FirstAsync(x => x.ProductCode == productCode && x.Channel == channel && x.Version == version);

            var now = DateTime.Now;
            var release = new UpdateRelease
            {
                Id = existing?.Id ?? IdHelper.NewId(),
                ProductCode = productCode,
                Channel = channel,
                Version = version,
                ReleaseNotes = request.ReleaseNotes?.Trim(),
                IsMandatory = request.IsMandatory,
                IsEnabled = true,
                PackageDirectory = targetRelative,
                FeedFileName = Path.GetFileName(copiedFeed),
                SetupFileName = setupFile == null ? null : Path.GetFileName(setupFile),
                Sha256 = await ComputeSha256Async(primaryPackage, cancellationToken),
                FileSizeBytes = copiedFiles.Sum(x => new FileInfo(x).Length),
                CreateTime = existing?.CreateTime ?? now,
                UpdateTime = now
            };

            if (existing == null)
                await _db.Db.Insertable(release).ExecuteCommandAsync();
            else
                await _db.Db.Updateable(release).ExecuteCommandAsync();

            return release;
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    public async Task SetEnabledAsync(long id, bool isEnabled)
    {
        var release = await GetByIdAsync(id);
        release.IsEnabled = isEnabled;
        release.UpdateTime = DateTime.Now;
        await _db.Db.Updateable(release).ExecuteCommandAsync();
    }

    public async Task SetMandatoryAsync(long id, bool isMandatory)
    {
        var release = await GetByIdAsync(id);
        release.IsMandatory = isMandatory;
        release.UpdateTime = DateTime.Now;
        await _db.Db.Updateable(release).ExecuteCommandAsync();
    }

    public async Task DeleteAsync(long id, bool deleteFiles)
    {
        var release = await GetByIdAsync(id);
        await _db.Db.Deleteable<UpdateRelease>().Where(x => x.Id == id).ExecuteCommandAsync();

        if (!deleteFiles)
            return;

        var root = ResolvePackageRoot();
        var directory = EnsureUnderRoot(root, Path.Combine(root, release.PackageDirectory));
        TryDeleteDirectory(directory);
    }

    public async Task<UpdateReleaseFile?> ResolveVelopackFileAsync(string productCode, string channel, string fileName)
    {
        var release = await GetLatestAsync(productCode, channel);
        if (release == null)
            return null;

        return ResolveFile(release, fileName);
    }

    public async Task<UpdateReleaseFile?> ResolveReleaseFileAsync(long id, string fileName)
    {
        var release = await GetByIdAsync(id);
        return ResolveFile(release, fileName);
    }

    public string GetVelopackBaseUrl(HttpRequest request, UpdateRelease release)
    {
        return $"{request.Scheme}://{request.Host}/api/update/velopack/{Uri.EscapeDataString(release.ProductCode)}/{Uri.EscapeDataString(release.Channel)}";
    }

    public string GetDownloadUrl(HttpRequest request, UpdateRelease release, string fileName)
    {
        return $"{request.Scheme}://{request.Host}/api/update/download/{release.Id}/{Uri.EscapeDataString(fileName)}";
    }

    public static bool IsNewerVersion(string candidateVersion, string currentVersion)
    {
        return ParseVersionOrDefault(candidateVersion) > ParseVersionOrDefault(currentVersion);
    }

    private async Task<UpdateRelease> GetByIdAsync(long id)
    {
        var release = await _db.Db.Queryable<UpdateRelease>().FirstAsync(x => x.Id == id);
        return release ?? throw new InvalidOperationException("更新版本不存在。");
    }

    private UpdateReleaseFile? ResolveFile(UpdateRelease release, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            fileName.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var actualFileName = IsFeedRequest(fileName, release.Channel)
            ? release.FeedFileName
            : fileName;
        var root = ResolvePackageRoot();
        var directory = EnsureUnderRoot(root, Path.Combine(root, release.PackageDirectory));
        var path = EnsureUnderRoot(root, Path.Combine(directory, actualFileName));

        if (!File.Exists(path))
            return null;

        return new UpdateReleaseFile(release, path, Path.GetFileName(path), GetContentType(path));
    }

    private static bool IsFeedRequest(string fileName, string channel)
    {
        return string.Equals(fileName, "RELEASES", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "releases.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, $"releases.{channel}.json", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolvePackageRoot()
    {
        var root = string.IsNullOrWhiteSpace(_options.UpdatePackageRoot)
            ? "Data/updates"
            : _options.UpdatePackageRoot;
        return Path.GetFullPath(Path.IsPathRooted(root)
            ? root
            : Path.Combine(AppContext.BaseDirectory, root));
    }

    private static string NormalizeSegment(string value, string displayName)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException($"{displayName}不能为空。");
        if (!SafeSegmentRegex.IsMatch(normalized))
            throw new InvalidOperationException($"{displayName}只能包含字母、数字、点、下划线和中横线。");

        return normalized;
    }

    private static string NormalizeVersion(string version)
    {
        var normalized = version.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(normalized, out _))
            throw new InvalidOperationException("版本号必须是有效格式，例如 1.2.0。");

        return normalized;
    }

    private static Version ParseVersionOrDefault(string version)
    {
        var normalized = (version ?? string.Empty).Trim().TrimStart('v', 'V');
        var dashIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex >= 0)
            normalized = normalized[..dashIndex];

        return Version.TryParse(normalized, out var parsed)
            ? parsed
            : new Version(0, 0, 0);
    }

    private static void ExtractZipSafely(Stream releaseZip, string destinationDirectory)
    {
        using var archive = new ZipArchive(releaseZip, ZipArchiveMode.Read, leaveOpen: true);
        var fileEntries = archive.Entries.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
        if (fileEntries.Count == 0)
            throw new InvalidOperationException("上传的 zip 中没有文件。");

        foreach (var entry in archive.Entries)
        {
            var normalizedEntryName = entry.FullName.Replace('\\', '/');
            if (IsUnsafeZipEntryName(normalizedEntryName))
                throw new InvalidOperationException($"zip 包含非法路径：{entry.FullName}");

            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, normalizedEntryName));
            if (!IsUnderDirectory(destinationDirectory, destinationPath))
                throw new InvalidOperationException($"zip 包含越界路径：{entry.FullName}");

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static bool IsUnsafeZipEntryName(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
            return true;
        if (entryName.StartsWith("/", StringComparison.Ordinal) ||
            entryName.StartsWith("\\", StringComparison.Ordinal))
        {
            return true;
        }

        return entryName.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(x => x == "." || x == "..");
    }

    private static string FindVelopackReleaseDirectory(string tempDirectory, string channel)
    {
        var feeds = Directory.EnumerateFiles(tempDirectory, "*", SearchOption.AllDirectories)
            .Where(x => IsSupportedFeedFile(Path.GetFileName(x), channel))
            .OrderByDescending(x => string.Equals(Path.GetFileName(x), "RELEASES", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (feeds.Count == 0)
            throw new InvalidOperationException("更新包缺少 Velopack feed 文件：RELEASES 或 releases.{channel}.json。");

        return Path.GetDirectoryName(feeds[0]) ?? tempDirectory;
    }

    private static string? FindFeedFile(string releaseDirectory, string channel)
    {
        return Directory.EnumerateFiles(releaseDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(x => IsSupportedFeedFile(Path.GetFileName(x), channel))
            .OrderByDescending(x => string.Equals(Path.GetFileName(x), "RELEASES", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool IsSupportedFeedFile(string fileName, string channel)
    {
        return string.Equals(fileName, "RELEASES", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "releases.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, $"releases.{channel}.json", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var target = Path.Combine(destinationDirectory, relative);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string EnsureUnderRoot(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        if (!IsUnderDirectory(fullRoot, fullPath) && !string.Equals(fullRoot, fullPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("解析后的文件路径越界。");

        return fullPath;
    }

    private static bool IsUnderDirectory(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToRelativePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".nupkg" => "application/octet-stream",
            ".exe" => "application/octet-stream",
            ".zip" => "application/zip",
            _ when string.Equals(Path.GetFileName(path), "RELEASES", StringComparison.OrdinalIgnoreCase) => "text/plain",
            _ => "application/octet-stream"
        };
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }
}

public sealed record UpdateReleaseUploadRequest(
    string ProductCode,
    string Channel,
    string Version,
    bool IsMandatory,
    string? ReleaseNotes);

public sealed record UpdateReleaseFile(
    UpdateRelease Release,
    string PhysicalPath,
    string FileName,
    string ContentType);

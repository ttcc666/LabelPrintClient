using System.IO.Compression;
using LicenseServer.Config;
using LicenseServer.Infrastructure;
using LicenseServer.Models;
using LicenseServer.Services;
using Microsoft.Extensions.Options;

namespace LicenseServer.Tests;

public class UpdateReleaseServiceTests
{
    [Fact]
    public async Task UploadAsync_RejectsZipTraversal()
    {
        using var fixture = UpdateFixture.Create();
        await using var zip = CreateZip(("../evil.txt", "bad"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.UploadAsync(new UpdateReleaseUploadRequest("LABEL_PRINT_CLIENT", "stable", "1.0.1", false, null), zip));

        Assert.Contains("非法路径", ex.Message);
    }

    [Fact]
    public async Task UploadAsync_SavesMetadataAndPackageFiles()
    {
        using var fixture = UpdateFixture.Create();
        await using var zip = CreateZip(("release/RELEASES", "feed"), ("release/LabelPrintClient-1.0.1-full.nupkg", "package"));

        var release = await fixture.Service.UploadAsync(new UpdateReleaseUploadRequest("LABEL_PRINT_CLIENT", "stable", "1.0.1", true, "notes"), zip);
        var latest = await fixture.Service.GetLatestAsync("LABEL_PRINT_CLIENT", "stable", "1.0.0");

        Assert.Equal("1.0.1", release.Version);
        Assert.True(release.IsMandatory);
        Assert.Equal(release.Id, latest?.Id);
        Assert.True(File.Exists(Path.Combine(fixture.UpdateRoot, release.PackageDirectory, "RELEASES")));
    }

    [Fact]
    public async Task GetLatestAsync_UsesSemanticOrderingAndIgnoresDisabledReleases()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.InsertReleaseAsync("1.2.0", enabled: true);
        await fixture.InsertReleaseAsync("1.10.0", enabled: true);
        await fixture.InsertReleaseAsync("2.0.0", enabled: false);

        var latest = await fixture.Service.GetLatestAsync("LABEL_PRINT_CLIENT", "stable", "1.0.0");

        Assert.NotNull(latest);
        Assert.Equal("1.10.0", latest!.Version);
    }

    private static MemoryStream CreateZip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class UpdateFixture : IDisposable
    {
        private readonly string _root;

        private UpdateFixture(string root, LicenseDb db, UpdateReleaseService service)
        {
            _root = root;
            Db = db;
            Service = service;
            UpdateRoot = Path.Combine(root, "updates");
        }

        public LicenseDb Db { get; }

        public UpdateReleaseService Service { get; }

        public string UpdateRoot { get; }

        public static UpdateFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "LicenseServer.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var options = Options.Create(new LicenseServerOptions
            {
                RunMode = "LocalSqlite",
                SqliteConnection = $"DataSource={Path.Combine(root, "license_server_test.db")}",
                UpdatePackageRoot = Path.Combine(root, "updates")
            });
            var db = new LicenseDb(options);
            DbInitializer.InitTables(db);
            var service = new UpdateReleaseService(db, options);
            return new UpdateFixture(root, db, service);
        }

        public async Task InsertReleaseAsync(string version, bool enabled)
        {
            var release = new UpdateRelease
            {
                Id = IdHelper.NewId(),
                ProductCode = "LABEL_PRINT_CLIENT",
                Channel = "stable",
                Version = version,
                IsEnabled = enabled,
                PackageDirectory = $"LABEL_PRINT_CLIENT/stable/{version}",
                FeedFileName = "RELEASES",
                Sha256 = new string('a', 64),
                FileSizeBytes = 1,
                CreateTime = DateTime.Now,
                UpdateTime = DateTime.Now
            };
            await Db.Db.Insertable(release).ExecuteCommandAsync();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }
    }
}

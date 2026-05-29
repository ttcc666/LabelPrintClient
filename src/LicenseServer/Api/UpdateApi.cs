using LicenseServer.Services;
using Microsoft.AspNetCore.Authorization;

namespace LicenseServer.Api;

public static class UpdateApi
{
    public static IEndpointRouteBuilder MapUpdateApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/update");

        group.MapGet("/latest", async Task<IResult> (
            string productCode,
            string? channel,
            string? currentVersion,
            UpdateReleaseService service,
            HttpRequest request) =>
        {
            var release = await service.GetLatestAsync(productCode, string.IsNullOrWhiteSpace(channel) ? "stable" : channel, currentVersion);
            if (release == null)
                return Results.Ok(UpdateLatestResponse.NoUpdate());

            return Results.Ok(new UpdateLatestResponse(
                HasUpdate: true,
                ProductCode: release.ProductCode,
                Channel: release.Channel,
                Version: release.Version,
                ReleaseNotes: release.ReleaseNotes,
                IsMandatory: release.IsMandatory,
                Sha256: release.Sha256,
                FileSizeBytes: release.FileSizeBytes,
                VelopackBaseUrl: service.GetVelopackBaseUrl(request, release),
                VelopackFeedUrl: $"{service.GetVelopackBaseUrl(request, release)}/RELEASES",
                SetupDownloadUrl: string.IsNullOrWhiteSpace(release.SetupFileName)
                    ? null
                    : service.GetDownloadUrl(request, release, release.SetupFileName)));
        }).AllowAnonymous();

        group.MapGet("/velopack/{productCode}/{channel}/RELEASES", async Task<IResult> (
            string productCode,
            string channel,
            UpdateReleaseService service,
            HttpRequest request) =>
        {
            var requestedPackage = GetRequestedPackageName(request);
            var file = await service.ResolveVelopackFileAsync(productCode, channel, requestedPackage ?? "RELEASES");
            return ToFileResult(file);
        }).AllowAnonymous();

        group.MapGet("/velopack/{productCode}/{channel}/releases.json", async Task<IResult> (
            string productCode,
            string channel,
            UpdateReleaseService service) =>
        {
            var file = await service.ResolveVelopackFileAsync(productCode, channel, "releases.json");
            return ToFileResult(file);
        }).AllowAnonymous();

        group.MapGet("/velopack/{productCode}/{channel}/releases.{feedChannel}.json", async Task<IResult> (
            string productCode,
            string channel,
            string feedChannel,
            UpdateReleaseService service) =>
        {
            var file = await service.ResolveVelopackFileAsync(productCode, channel, $"releases.{feedChannel}.json");
            return ToFileResult(file);
        }).AllowAnonymous();

        group.MapGet("/velopack/{productCode}/{channel}/{fileName}", async Task<IResult> (
            string productCode,
            string channel,
            string fileName,
            UpdateReleaseService service) =>
        {
            var file = await service.ResolveVelopackFileAsync(productCode, channel, fileName);
            return ToFileResult(file);
        }).AllowAnonymous();

        group.MapGet("/download/{id:long}/{fileName}", async Task<IResult> (
            long id,
            string fileName,
            UpdateReleaseService service) =>
        {
            var file = await service.ResolveReleaseFileAsync(id, fileName);
            return ToFileResult(file);
        }).AllowAnonymous();

        return endpoints;
    }

    private static IResult ToFileResult(UpdateReleaseFile? file)
    {
        return file == null
            ? Results.NotFound()
            : Results.File(file.PhysicalPath, file.ContentType, file.FileName, enableRangeProcessing: true);
    }

    private static string? GetRequestedPackageName(HttpRequest request)
    {
        foreach (var key in new[] { "fileName", "filename", "package", "packageName", "name" })
        {
            var value = request.Query[key].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
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
    string? SetupDownloadUrl)
{
    public static UpdateLatestResponse NoUpdate()
    {
        return new UpdateLatestResponse(false, null, null, null, null, false, null, 0, null, null, null);
    }
}

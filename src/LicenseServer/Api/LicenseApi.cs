using LicenseServer.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LicenseServer.Api;

public static class LicenseApi
{
    public static IEndpointRouteBuilder MapLicenseApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/license");

        group.MapPost("/acquire", async Task<IResult> (LicenseAcquireRequest request, FloatingLicenseService service) =>
        {
            var result = await service.AcquireAsync(request);
            return ToResult(result);
        });

        group.MapPost("/heartbeat", async Task<IResult> (LicenseHeartbeatRequest request, FloatingLicenseService service) =>
        {
            var result = await service.HeartbeatAsync(request.Token);
            return ToResult(result);
        });

        group.MapPost("/release", async Task<Ok> (LicenseHeartbeatRequest request, FloatingLicenseService service) =>
        {
            await service.ReleaseAsync(request.Token);
            return TypedResults.Ok();
        });

        return endpoints;
    }

    private static IResult ToResult(FloatingLicenseResponse result)
    {
        return result.StatusCode switch
        {
            200 => TypedResults.Ok(result),
            400 => TypedResults.BadRequest(result),
            409 => TypedResults.Conflict(result),
            404 => TypedResults.NotFound(result),
            _ => TypedResults.Json(result, statusCode: result.StatusCode)
        };
    }
}

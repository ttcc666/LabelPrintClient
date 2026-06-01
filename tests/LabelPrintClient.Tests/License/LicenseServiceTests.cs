using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LabelPrintClient.Config;
using LabelPrintClient.Modules.License.Models;
using LabelPrintClient.Modules.License.Services;

namespace LabelPrintClient.Tests.License;

public class LicenseServiceTests
{
    private const string PrivateKeyPem = """
-----BEGIN PRIVATE KEY-----
MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQCIcV41GbecYR73
n4zqFDi0fKLnxHFANZLc1rW23u/zM4JSCCzyxyMzx/dcuRaENenk0fd9RnFyFQq4
s8yVdSF0/GwAnuhY32/SpI87uw1SqO51rGrHwdA7YBMLi0cco93EpshxIldc+V1B
ehw75VBBE1Vo8VKeblYG1uI5SNKEJ3fEdmKRJP2hoWJQqyRXnoo3i97x4ScCr4sg
D34S2QeyLr4q6SWEVU34U03+KS3xNJ/fLHRjW0tLXXc1yDdyFgtF1/jBd+FEjJfs
ZtM0UZJvc8h//KdBgbjxPI4kNuY734PFTuqwyvx4TlM6yMdEaxZciAnx1WLEXZ7f
LACHMonHAgMBAAECggEADPUgj7q+AQKzg1ZVIQ5uO4G8oiBklZxLiAp3SQMkrCbU
9lUNbmRIAmJiqkfE4iW77ShIMHml9hdrzxJQLaoT1Sglr5dnvwzqcd7WisMe/Eh2
a5lwretziuKlsbUhOeXmqTF3m/rkFhHJGpnt9X2NC2kPyulkFw9TvyQyvrhHjxeM
RMrq97pFHdrpLnUElj8+hYO0w9K0mdixXG02uDdElUJtCL8e1ozH1zgTwECidLjZ
w4/zhewoio/5y9K0AxokAQ62CBf8SG2h1Z4VishuAxWGmTzom4kALf7yU4/NYqVv
eC2Co+Fnl2f8/mmAzUxPM5xCQOm3A/eNP22Gh0DSKQKBgQC/k7EGFOz7o09SnUex
VEDLYnadPDWzd4AkO6fyONugFPIkcyLaOvLqbLToeHS7v3ZgbPTUETh8OQqS9FyW
MrP7R2EKdaGOMsE0WrP5fm1T7ntG45FpyCOzp4YEY10JcwiWc21ZHB9N5QFMoamA
F08y8+zEWWAQSkjtfQ4aLwLeeQKBgQC2U1d8hzD5Rm5YVrrfvDilmA3Vo/Mnnkyv
tjzDRfEdTsiU9AGfsleMU/agWu6ctdjhqxZhjSq8NBCx9YnT46wqJVwkrJfZiMQa
xoJGCaNOCZi+NV5khkrDDvGR87ZqXtzp8v8vOzTMG/dZ++v/eGrcttd0MPbPLqfC
VmIEOrGaPwKBgA0ngvw765nLuOKfUhDnDBvrAuIBBF7yUaYrQnjrVolDZu0Byt7Z
NVzLYhCkVL+fge1VDeqR1CMTd5pnlQPrL1iNqighs5oj+ggyQjFbcP5WXbicX5u3
1lu7oQQkHntLnsdV3ahEuhGLK++rGgxljVaeUR+aU3JK538HGzTJDZVhAoGAS+QO
41+ma+v8HDsll+FRtuO+xnFy0cfbZbw2OJXRUgCsDwwt7NogBOIiIwcWkRZES1Ka
g0puQl5toJVypEb9L6HTY9SPdFWwQvDj4uE6H05xTKMMQk1/qwd6V+UYxdfsnliu
DWvYgykU4ViyF+l4mZxlvBBxezWRUJwCOn5v1KMCgYEAgt+wKz6wIt1d3r8xFR1m
cRTwWdAfC5+A4G/GxvXHJuR+0tzh/v2WK9sjSEzynIHFQOAHWWKdOb2Km+yXgv1o
3luw4yJjsK/kFSmGm9tTV/ileEqPBbArix8l/ci0c37BkGZ7lUoeAxlUgedb+554
+cszQke8+EZnMwwx+RuUMZ0=
-----END PRIVATE KEY-----
""";

    [Fact]
    public async Task StandaloneLicenseVerifier_ValidLicense_ReturnsValid()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "license.json");
        await WriteLicenseAsync(path, MachineCodeService.GetMachineCode(), DateTime.Now.AddDays(30));
        var settings = new AppSettings { StandaloneLicenseFilePath = path, ProductCode = "LABEL_PRINT_CLIENT" };

        var result = await new StandaloneLicenseVerifier().VerifyAsync(settings);

        Assert.True(result.IsValid);
        Assert.Equal(LicenseStatus.Valid, result.Status);
    }

    [Fact]
    public async Task StandaloneLicenseVerifier_ExpiredLicense_ReturnsExpired()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "license.json");
        await WriteLicenseAsync(path, MachineCodeService.GetMachineCode(), DateTime.Now.AddDays(-1));
        var settings = new AppSettings { StandaloneLicenseFilePath = path, ProductCode = "LABEL_PRINT_CLIENT" };

        var result = await new StandaloneLicenseVerifier().VerifyAsync(settings);

        Assert.False(result.IsValid);
        Assert.Equal(LicenseStatus.Expired, result.Status);
    }

    [Fact]
    public async Task StandaloneLicenseVerifier_MachineMismatch_ReturnsMachineMismatch()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "license.json");
        await WriteLicenseAsync(path, "OTHER-MACHINE", DateTime.Now.AddDays(30));
        var settings = new AppSettings { StandaloneLicenseFilePath = path, ProductCode = "LABEL_PRINT_CLIENT" };

        var result = await new StandaloneLicenseVerifier().VerifyAsync(settings);

        Assert.False(result.IsValid);
        Assert.Equal(LicenseStatus.MachineMismatch, result.Status);
    }

    [Fact]
    public async Task StandaloneLicenseVerifier_InvalidSignature_ReturnsInvalidSignature()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "license.json");
        await WriteLicenseAsync(path, MachineCodeService.GetMachineCode(), DateTime.Now.AddDays(30), "bad-signature");
        var settings = new AppSettings { StandaloneLicenseFilePath = path, ProductCode = "LABEL_PRINT_CLIENT" };

        var result = await new StandaloneLicenseVerifier().VerifyAsync(settings);

        Assert.False(result.IsValid);
        Assert.Equal(LicenseStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public async Task FloatingLicenseClient_AcquireHeartbeatRelease_UsesExpectedEndpoints()
    {
        var handler = new FakeHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (path == "/api/license/acquire")
            {
                var json = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
                Assert.Contains("accessKey", json);
                Assert.Contains("client-access-key", json);
                return JsonResponse(HttpStatusCode.OK, new { Success = true, Token = "token-1", Message = "ok", HeartbeatIntervalSeconds = 15 });
            }

            if (path == "/api/license/heartbeat")
            {
                return JsonResponse(HttpStatusCode.OK, new { Success = true, Token = "token-1", Message = "alive", HeartbeatIntervalSeconds = 15 });
            }

            if (path == "/api/license/release")
            {
                return JsonResponse(HttpStatusCode.OK, new { Success = true });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = new FloatingLicenseClient(new HttpClient(handler));
        var settings = new AppSettings { LicenseMode = LicenseMode.Floating, LicenseServerUrl = "https://license.local/", LicenseAccessKey = "client-access-key" };

        var acquire = await client.AcquireAsync(settings);
        var heartbeat = await client.HeartbeatAsync("token-1", settings);
        await client.ReleaseAsync("token-1", settings);

        Assert.True(acquire.IsValid);
        Assert.Equal("token-1", acquire.Token);
        Assert.True(heartbeat.IsValid);
        Assert.Contains("/api/license/acquire", handler.Paths);
        Assert.Contains("/api/license/heartbeat", handler.Paths);
        Assert.Contains("/api/license/release", handler.Paths);
    }

    [Fact]
    public async Task LicenseService_ValidateConfigurationAsync_ForFloatingUsesValidateEndpointOnly()
    {
        var handler = new FakeHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath;
            return path == "/api/license/validate"
                ? JsonResponse(HttpStatusCode.OK, new { Success = true, Message = "ok", HeartbeatIntervalSeconds = 15 })
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = new FloatingLicenseClient(new HttpClient(handler));
        var settings = new AppSettings { LicenseMode = LicenseMode.Floating, LicenseServerUrl = "https://license.local/", LicenseAccessKey = "client-access-key" };
        var service = new LicenseService(settings, floatingClient: client);

        var result = await service.ValidateConfigurationAsync();

        Assert.True(result.IsValid);
        Assert.Null(result.Token);
        Assert.Contains("/api/license/validate", handler.Paths);
        Assert.DoesNotContain("/api/license/acquire", handler.Paths);
        Assert.DoesNotContain("/api/license/release", handler.Paths);
    }

    [Fact]
    public async Task FloatingLicenseClient_SeatLimit_ReturnsSeatLimitExceeded()
    {
        var handler = new FakeHandler((_, _) =>
            JsonResponse(HttpStatusCode.Conflict, new { Success = false, Message = "seat limit" }));
        var client = new FloatingLicenseClient(new HttpClient(handler));
        var settings = new AppSettings { LicenseMode = LicenseMode.Floating, LicenseServerUrl = "https://license.local/", LicenseAccessKey = "client-access-key" };

        var result = await client.AcquireAsync(settings);

        Assert.False(result.IsValid);
        Assert.Equal(LicenseStatus.SeatLimitExceeded, result.Status);
    }

    private static async Task WriteLicenseAsync(string path, string machineCode, DateTime expireTime, string? signatureOverride = null)
    {
        var document = new LicenseDocument
        {
            ProductCode = "LABEL_PRINT_CLIENT",
            LicenseMode = LicenseMode.Standalone,
            MachineCode = machineCode,
            TotalCount = 1,
            ExpireTime = expireTime,
            IssuedTo = "Test"
        };

        document.Signature = signatureOverride ?? Sign(document);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
    }

    private static string Sign(LicenseDocument document)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(PrivateKeyPem);
        var payload = Encoding.UTF8.GetBytes(StandaloneLicenseVerifier.CreateSignedPayload(document));
        return Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
            : this((request, token) => Task.FromResult(handler(request, token)))
        {
        }

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public List<string> Paths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            return _handler(request, cancellationToken);
        }
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LabelPrintClient.Tests", Guid.NewGuid().ToString("N"));

        public TempFolder()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}

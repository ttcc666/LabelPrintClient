using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LicenseServer.Config;
using LicenseServer.Infrastructure;
using LicenseServer.Models;
using LicenseServer.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace LicenseServer.Tests;

public class LicenseServerFunctionalTests
{
    private const string MasterKeyName = "LICENSE_SERVER_MASTER_KEY";
    private const string MasterKeyValue = "test-master-key-for-license-server";

    private const string ClientPublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAiHFeNRm3nGEe95+M6hQ4
tHyi58RxQDWS3Na1tt7v8zOCUggs8scjM8f3XLkWhDXp5NH3fUZxchUKuLPMlXUh
dPxsAJ7oWN9v0qSPO7sNUqjudaxqx8HQO2ATC4tHHKPdxKbIcSJXXPldQXocO+VQ
QRNVaPFSnm5WBtbiOUjShCd3xHZikST9oaFiUKskV56KN4ve8eEnAq+LIA9+EtkH
si6+KuklhFVN+FNN/ikt8TSf3yx0Y1tLS113Ncg3chYLRdf4wXfhRIyX7GbTNFGS
b3PIf/ynQYG48TyOJDbmO9+DxU7qsMr8eE5TOsjHRGsWXIgJ8dVixF2e3ywAhzKJ
xwIDAQAB
-----END PUBLIC KEY-----
""";

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
    public async Task SetupPage_WhenNoAdmin_IsReachable()
    {
        using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/setup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SigningKeyImport_WithoutMasterKey_ThrowsReadableError()
    {
        using var fixture = TestFixture.Create(setMasterKey: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.SigningKeys.ImportAsync(PrivateKeyPem));

        Assert.Contains(MasterKeyName, ex.Message);
    }

    [Fact]
    public async Task StandaloneLicenseGenerator_GeneratesClientVerifiableLicense()
    {
        using var fixture = TestFixture.Create();
        await fixture.SigningKeys.ImportAsync(PrivateKeyPem);
        var customer = await fixture.InsertCustomerAsync();
        var license = await fixture.InsertLicenseAsync(customer.Id, LicenseMode.Standalone, machineCode: "MACHINE-001");

        var json = await fixture.Generator.GenerateAsync(license);
        var document = JsonSerializer.Deserialize<LicenseDocument>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(document);
        Assert.Equal("MACHINE-001", document!.MachineCode);
        Assert.True(VerifyClientSignature(document));
    }

    [Fact]
    public async Task FloatingLicense_AcquireHeartbeatRelease_ControlsSessionLifecycle()
    {
        using var fixture = TestFixture.Create();
        var customer = await fixture.InsertCustomerAsync();
        await fixture.InsertLicenseAsync(customer.Id, LicenseMode.Floating, accessKey: "access-key", totalCount: 1);

        var acquire = await fixture.Floating.AcquireAsync(new LicenseAcquireRequest("LABEL_PRINT_CLIENT", "access-key", "M1", "PC-1"));
        var heartbeat = await fixture.Floating.HeartbeatAsync(acquire.Token!);
        await fixture.Floating.ReleaseAsync(acquire.Token!);
        var afterRelease = await fixture.Floating.HeartbeatAsync(acquire.Token!);

        Assert.True(acquire.Success);
        Assert.True(heartbeat.Success);
        Assert.False(afterRelease.Success);
        Assert.Equal(404, afterRelease.StatusCode);
    }

    [Fact]
    public async Task FloatingLicense_WhenSeatFull_ReturnsConflictButSameMachineReusesSeat()
    {
        using var fixture = TestFixture.Create();
        var customer = await fixture.InsertCustomerAsync();
        await fixture.InsertLicenseAsync(customer.Id, LicenseMode.Floating, accessKey: "access-key", totalCount: 1);

        var first = await fixture.Floating.AcquireAsync(new LicenseAcquireRequest("LABEL_PRINT_CLIENT", "access-key", "M1", "PC-1"));
        var sameMachine = await fixture.Floating.AcquireAsync(new LicenseAcquireRequest("LABEL_PRINT_CLIENT", "access-key", "M1", "PC-1"));
        var secondMachine = await fixture.Floating.AcquireAsync(new LicenseAcquireRequest("LABEL_PRINT_CLIENT", "access-key", "M2", "PC-2"));

        Assert.True(first.Success);
        Assert.True(sameMachine.Success);
        Assert.False(secondMachine.Success);
        Assert.Equal(409, secondMachine.StatusCode);
    }

    [Fact]
    public async Task FloatingLicense_InvalidAccessKeyOrExpiredLicense_IsRejected()
    {
        using var fixture = TestFixture.Create();
        var customer = await fixture.InsertCustomerAsync();
        await fixture.InsertLicenseAsync(customer.Id, LicenseMode.Floating, accessKey: "access-key", expireTime: DateTime.Now.AddDays(-1));

        var expired = await fixture.Floating.AcquireAsync(new LicenseAcquireRequest("LABEL_PRINT_CLIENT", "access-key", "M1", "PC-1"));
        var invalidKey = await fixture.Floating.AcquireAsync(new LicenseAcquireRequest("LABEL_PRINT_CLIENT", "bad-key", "M1", "PC-1"));

        Assert.False(expired.Success);
        Assert.False(invalidKey.Success);
        Assert.Equal(401, invalidKey.StatusCode);
    }

    private static bool VerifyClientSignature(LicenseDocument document)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(ClientPublicKeyPem);
        var payload = Encoding.UTF8.GetBytes(StandaloneLicenseGenerator.CreateSignedPayload(document));
        return rsa.VerifyData(payload, Convert.FromBase64String(document.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private sealed class TestFixture : IDisposable
    {
        private readonly string _root;
        private readonly string? _previousMasterKey;

        private TestFixture(string root, string? previousMasterKey, LicenseDb db, SigningKeyService signingKeys, StandaloneLicenseGenerator generator, FloatingLicenseService floating)
        {
            _root = root;
            _previousMasterKey = previousMasterKey;
            Db = db;
            SigningKeys = signingKeys;
            Generator = generator;
            Floating = floating;
        }

        public LicenseDb Db { get; }
        public SigningKeyService SigningKeys { get; }
        public StandaloneLicenseGenerator Generator { get; }
        public FloatingLicenseService Floating { get; }

        public static TestFixture Create(bool setMasterKey = true)
        {
            var root = Path.Combine(Path.GetTempPath(), "LicenseServer.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var previous = Environment.GetEnvironmentVariable(MasterKeyName);
            Environment.SetEnvironmentVariable(MasterKeyName, setMasterKey ? MasterKeyValue : null);
            var options = Options.Create(new LicenseServerOptions
            {
                RunMode = "LocalSqlite",
                SqliteConnection = $"DataSource={Path.Combine(root, "license_server_test.db")}",
                HeartbeatIntervalSeconds = 30,
                SessionTimeoutSeconds = 120,
                MasterKeyEnvironmentName = MasterKeyName
            });
            var db = new LicenseDb(options);
            DbInitializer.InitTables(db);
            var protector = new PrivateKeyProtector(options);
            var signingKeys = new SigningKeyService(db, protector);
            var generator = new StandaloneLicenseGenerator(signingKeys);
            var floating = new FloatingLicenseService(db, options);
            return new TestFixture(root, previous, db, signingKeys, generator, floating);
        }

        public async Task<Customer> InsertCustomerAsync()
        {
            var customer = new Customer { Id = IdHelper.NewId(), Name = "ACME", IsEnabled = true, CreateTime = DateTime.Now };
            await Db.Db.Insertable(customer).ExecuteCommandAsync();
            return customer;
        }

        public async Task<AppLicense> InsertLicenseAsync(
            long customerId,
            LicenseMode mode,
            string? machineCode = null,
            string? accessKey = null,
            int totalCount = 1,
            DateTime? expireTime = null)
        {
            var license = new AppLicense
            {
                Id = IdHelper.NewId(),
                CustomerId = customerId,
                ProductCode = "LABEL_PRINT_CLIENT",
                LicenseMode = mode,
                ExpireTime = expireTime ?? DateTime.Now.AddDays(30),
                IsEnabled = true,
                MachineCode = machineCode,
                TotalCount = totalCount,
                AccessKeyHash = accessKey == null ? null : HashService.Sha256(accessKey),
                IssuedTo = "ACME",
                CreateTime = DateTime.Now
            };
            await Db.Db.Insertable(license).ExecuteCommandAsync();
            return license;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(MasterKeyName, _previousMasterKey);
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

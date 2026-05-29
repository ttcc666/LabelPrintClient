using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LicenseServer.Config;
using LicenseServer.Infrastructure;
using LicenseServer.Models;
using LicenseServer.Pages;
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

    [Fact]
    public async Task FloatingLicense_EndToEndMixedAuthFlow_Succeeds()
    {
        // 1. 公司端：导入私钥并创建许可证
        using var company = TestFixture.Create();
        await company.SigningKeys.ImportAsync(PrivateKeyPem);
        
        var companyCustomer = await company.InsertCustomerAsync();
        var companyLicense = await company.InsertLicenseAsync(
            companyCustomer.Id, 
            LicenseMode.Floating, 
            accessKey: "my-custom-access-key", 
            totalCount: 2);

        // 在公司端，使用 Generator 生成浮动许可证密文
        var signedLicenseJson = await company.Generator.GenerateAsync(companyLicense, "my-custom-access-key");

        // 2. 局域网端：充当部署在客户局域网的只读服务器（无私钥）
        // 并且我们在配置里设置 PublicKeyPem 从而模拟只读公钥校验
        using var lan = TestFixture.Create(setMasterKey: false); // 无 MasterKey
        
        var validator = new LicenseValidator(Options.Create(new LicenseServerOptions
        {
            PublicKeyPem = ClientPublicKeyPem // 设置为公司端的公钥
        }));
        
        var model = new LicensesModel(lan.Db, lan.Generator, lan.SigningKeys, validator, lan.Protector);
        model.ImportLicenseContent = signedLicenseJson;

        await model.OnPostImportAsync();

        // 验证导入成功提示
        Assert.Contains("成功", model.ErrorMessage);

        // 检查局域网端的数据库是否自动建好了客户记录和许可证记录
        var importedLicense = await lan.Db.Db.Queryable<AppLicense>().FirstAsync(x => x.Id == companyLicense.Id);
        Assert.NotNull(importedLicense);
        Assert.Equal(LicenseMode.Floating, importedLicense.LicenseMode);
        Assert.Equal(2, importedLicense.TotalCount);
        Assert.Equal(HashService.Sha256("my-custom-access-key"), importedLicense.AccessKeyHash);

        var importedCustomer = await lan.Db.Db.Queryable<Customer>().FirstAsync(x => x.Id == importedLicense.CustomerId);
        Assert.NotNull(importedCustomer);
        Assert.Equal("ACME", importedCustomer.Name); // 自动创建了公司端的 "ACME" 客户

        // 3. 客户端：测试能否成功通过局域网端获取浮动授权
        var acquire = await lan.Floating.AcquireAsync(new LicenseAcquireRequest("LABEL_PRINT_CLIENT", "my-custom-access-key", "MACHINE-1", "PC-1"));
        Assert.True(acquire.Success);
        Assert.NotNull(acquire.Token);

        // 席位限制测试：席位是 2
        var second = await lan.Floating.AcquireAsync(new LicenseAcquireRequest("LABEL_PRINT_CLIENT", "my-custom-access-key", "MACHINE-2", "PC-2"));
        var third = await lan.Floating.AcquireAsync(new LicenseAcquireRequest("LABEL_PRINT_CLIENT", "my-custom-access-key", "MACHINE-3", "PC-3"));
        
        Assert.True(second.Success);
        Assert.False(third.Success); // 席位满，超限失败
        Assert.Equal(409, third.StatusCode);
    }

    [Fact]
    public async Task FloatingLicense_ImportInvalidLicense_IsRejected()
    {
        using var lan = TestFixture.Create(setMasterKey: false);
        var validator = new LicenseValidator(Options.Create(new LicenseServerOptions
        {
            PublicKeyPem = ClientPublicKeyPem
        }));
        var model = new LicensesModel(lan.Db, lan.Generator, lan.SigningKeys, validator, lan.Protector);

        // 1. 无效签名
        model.ImportLicenseContent = "{\"ProductCode\":\"LABEL_PRINT_CLIENT\",\"Signature\":\"bad-sig\"}";
        await model.OnPostImportAsync();
        Assert.Contains("验证失败", model.ErrorMessage);

        // 2. 过期证书
        using var company = TestFixture.Create();
        await company.SigningKeys.ImportAsync(PrivateKeyPem);
        var companyCustomer = await company.InsertCustomerAsync();
        var expiredLicense = await company.InsertLicenseAsync(
            companyCustomer.Id, 
            LicenseMode.Floating, 
            accessKey: "key", 
            expireTime: DateTime.Now.AddDays(-1));
        var expiredJson = await company.Generator.GenerateAsync(expiredLicense, "key");

        model.ImportLicenseContent = expiredJson;
        await model.OnPostImportAsync();
        Assert.Contains("已过期", model.ErrorMessage);

        // 3. 局域网端导入单机版证书（应该拒绝）
        var standaloneLicense = await company.InsertLicenseAsync(
            companyCustomer.Id,
            LicenseMode.Standalone,
            machineCode: "M1");
        var standaloneJson = await company.Generator.GenerateAsync(standaloneLicense);

        model.ImportLicenseContent = standaloneJson;
        await model.OnPostImportAsync();
        Assert.Contains("仅支持导入浮动授权", model.ErrorMessage);
    }

    [Fact]
    public async Task LegacyStandaloneLicense_IsBackwardCompatible()
    {
        var doc = new LicenseDocument
        {
            ProductCode = "LABEL_PRINT_CLIENT",
            LicenseMode = LicenseMode.Standalone,
            MachineCode = "MACHINE-001",
            TotalCount = 1,
            ExpireTime = DateTime.Now.AddDays(30),
            IssuedTo = "ACME"
        };

        using var rsa = RSA.Create();
        rsa.ImportFromPem(PrivateKeyPem);
        
        var newPayloadJson = StandaloneLicenseGenerator.CreateSignedPayload(doc);
        var payloadBytes = Encoding.UTF8.GetBytes(newPayloadJson);
        doc.Signature = Convert.ToBase64String(rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        var validator = new LicenseValidator(Options.Create(new LicenseServerOptions
        {
            PublicKeyPem = ClientPublicKeyPem
        }));
        Assert.True(validator.Verify(doc));
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

        private TestFixture(string root, string? previousMasterKey, LicenseDb db, SigningKeyService signingKeys, StandaloneLicenseGenerator generator, FloatingLicenseService floating, PrivateKeyProtector protector)
        {
            _root = root;
            _previousMasterKey = previousMasterKey;
            Db = db;
            SigningKeys = signingKeys;
            Generator = generator;
            Floating = floating;
            Protector = protector;
        }

        public LicenseDb Db { get; }
        public SigningKeyService SigningKeys { get; }
        public StandaloneLicenseGenerator Generator { get; }
        public FloatingLicenseService Floating { get; }
        public PrivateKeyProtector Protector { get; }

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
                MasterKeyEnvironmentName = MasterKeyName,
                MasterKey = setMasterKey ? MasterKeyValue : null
            });
            var db = new LicenseDb(options);
            DbInitializer.InitTables(db);
            var protector = new PrivateKeyProtector(options);
            var signingKeys = new SigningKeyService(db, protector);
            var generator = new StandaloneLicenseGenerator(signingKeys);
            var floating = new FloatingLicenseService(db, options);
            return new TestFixture(root, previous, db, signingKeys, generator, floating, protector);
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

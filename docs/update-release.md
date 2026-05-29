# 客户端打包 / 发布 / 更新教程

本文档说明 `LabelPrintClient` 的 Windows 客户端如何打包、发布到 `LicenseServer`，以及如何验证自动更新。

## 1. 更新机制概览

当前更新链路：

- 客户端：`LabelPrintClient`
- 更新引擎：Velopack
- 更新中心：`LicenseServer`
- 产品标识：`LABEL_PRINT_CLIENT`
- 默认渠道：`stable`
- 目标平台：`win-x64`
- 更新包存储：`LicenseServer` 本地文件系统
- 更新元数据：`LicenseServer` 数据库表 `UpdateReleases`

客户端启动进入主窗口后会自动检查更新；也可以在 Settings 页面手动检查。强制更新包会要求用户更新，拒绝或失败会退出客户端。

## 2. 前置条件

发布机器需要：

- Windows
- .NET 8 SDK
- Velopack CLI：`vpk`
- 能访问 `LicenseServer` 管理后台的账号
- 已部署并可访问的 `LicenseServer`

安装 Velopack CLI。建议 CLI 版本与客户端项目里的 `Velopack` NuGet 包版本保持一致：

```powershell
dotnet tool install --global 'vpk' --version '1.0.1'
```

确认命令可用：

```powershell
vpk --help
```

注意：自动更新只对 Velopack 安装出来的客户端生效。直接从 Visual Studio、`dotnet run`、或者 publish 目录双击 exe 运行，通常只能验证“检查更新 API”，不能完整验证“下载、安装、重启”。

## 3. 发布前改版本号

每次发布新客户端，先修改：

```xml
<!-- src/LabelPrintClient/LabelPrintClient.csproj -->
<Version>1.0.1</Version>
```

版本号必须递增，例如：

- `1.0.0` -> `1.0.1`
- `1.0.1` -> `1.0.2`
- `1.0.2` -> `1.1.0`

不要用同一个版本号重复发布不同内容。客户端和 Velopack 都依赖版本号判断是否有更新。

版本号建议使用 SemVer 格式，例如 `1.0.1`、`1.1.0`、`2.0.0-beta.1`。不要使用四段版本号，例如 `1.0.0.0`。

## 4. 本地验证构建

在仓库根目录执行：

```powershell
dotnet restore 'D:\Demo\LabelPrintClient\LabelPrintClient.sln'
dotnet build 'D:\Demo\LabelPrintClient\LabelPrintClient.sln' -c 'Release'
```

建议发布前跑核心测试：

```powershell
dotnet test 'D:\Demo\LabelPrintClient\tests\LicenseServer.Tests\LicenseServer.Tests.csproj' -c 'Release'
dotnet test 'D:\Demo\LabelPrintClient\tests\LabelPrintClient.Tests\LabelPrintClient.Tests.csproj' -c 'Release' --filter 'FullyQualifiedName~Update'
```

如果要跑全量测试：

```powershell
dotnet test 'D:\Demo\LabelPrintClient\LabelPrintClient.sln' -c 'Release'
```

## 5. 发布客户端文件

生成 win-x64 publish 输出：

```powershell
dotnet publish 'D:\Demo\LabelPrintClient\src\LabelPrintClient\LabelPrintClient.csproj' -c 'Release' -r 'win-x64' --self-contained 'true' -o 'D:\Demo\LabelPrintClient\artifacts\publish\LabelPrintClient'
```

确认输出目录存在：

```powershell
Get-ChildItem 'D:\Demo\LabelPrintClient\artifacts\publish\LabelPrintClient'
```

## 6. 生成 Velopack 更新包

使用 `vpk pack` 把 publish 目录打成 Velopack release：

```powershell
vpk pack `
  --packId 'LabelPrintClient' `
  --packVersion '1.0.1' `
  --channel 'stable' `
  --packDir 'D:\Demo\LabelPrintClient\artifacts\publish\LabelPrintClient' `
  --mainExe 'LabelPrintClient.exe' `
  --runtime 'win-x64' `
  --outputDir 'D:\Demo\LabelPrintClient\artifacts\releases\LabelPrintClient-1.0.1'
```

这里有两个容易混淆的 ID：

- `LABEL_PRINT_CLIENT`：LicenseServer 更新接口使用的产品标识。
- `LabelPrintClient`：Velopack 包 ID，发布时建议保持稳定，不要随版本变化。

这里也有两个渠道概念需要保持一致：

- `--channel 'stable'`：Velopack release 的渠道。
- `UpdateChannel = stable`：客户端和 LicenseServer 使用的渠道。

生成后，release 目录里应该能看到类似文件：

- `RELEASES` 或 `releases.stable.json`
- `RELEASES-stable`、`assets.stable.json` 等 Velopack 辅助清单文件
- `LabelPrintClient-1.0.1-full.nupkg`
- `LabelPrintClient-stable-Portable.zip`
- `*-Setup.exe`

检查命令：

```powershell
Get-ChildItem 'D:\Demo\LabelPrintClient\artifacts\releases\LabelPrintClient-1.0.1'
```

## 7. 压缩上传包

`LicenseServer` 后台上传的是一个 zip。zip 里需要包含 Velopack release 输出，至少包含：

- Velopack feed 文件：`RELEASES` 或 `releases.stable.json`
- `.nupkg`
- 首次安装用的 `Setup.exe`

如果目录里还有 `RELEASES-stable`、`assets.stable.json`、portable zip，也一起压进去。

推荐把 release 目录内容压缩成一个 zip：

```powershell
Compress-Archive `
  -Path 'D:\Demo\LabelPrintClient\artifacts\releases\LabelPrintClient-1.0.1\*' `
  -DestinationPath 'D:\Demo\LabelPrintClient\artifacts\releases\LabelPrintClient-1.0.1.zip' `
  -Force
```

确认 zip 已生成：

```powershell
Get-Item 'D:\Demo\LabelPrintClient\artifacts\releases\LabelPrintClient-1.0.1.zip'
```

## 8. 在 LicenseServer 发布

启动或打开 `LicenseServer` 管理后台。

本地开发环境可以执行：

```powershell
dotnet run --project 'D:\Demo\LabelPrintClient\src\LicenseServer\LicenseServer.csproj' --urls 'http://127.0.0.1:5051'
```

打开：

```text
http://127.0.0.1:5051/Updates
```

在 Updates 页面填写：

- Product Code：`LABEL_PRINT_CLIENT`
- Channel：`stable`
- Version：本次版本，例如 `1.0.1`
- Title：发布标题，例如 `LabelPrintClient 1.0.1`
- Release Notes：发布说明
- Package：选择 `LabelPrintClient-1.0.1.zip`
- Mandatory：是否强制更新
- Active：启用

保存后，后台会解压 zip，读取 Velopack feed 文件（`RELEASES` 或 `releases.stable.json`），并保存更新元数据。

生产环境必须使用 HTTPS 的 `LicenseServer` 地址。客户端配置里也应该使用 HTTPS。

## 9. 验证服务端发布结果

设置服务端地址：

```powershell
$server = 'http://127.0.0.1:5051'
```

检查最新版本 API：

```powershell
Invoke-RestMethod ('{0}/api/update/latest?productCode=LABEL_PRINT_CLIENT&channel=stable&currentVersion=1.0.0' -f $server)
```

预期能看到 `hasUpdate` 为 `true`，并返回 `version`、`isMandatory`、`velopackBaseUrl`、`velopackFeedUrl`、`setupDownloadUrl` 等字段。

检查 Velopack feed：

```powershell
Invoke-WebRequest ('{0}/api/update/velopack/LABEL_PRINT_CLIENT/stable/RELEASES' -f $server)
```

也可以检查新格式 feed：

```powershell
Invoke-WebRequest ('{0}/api/update/velopack/LABEL_PRINT_CLIENT/stable/releases.stable.json' -f $server)
```

预期 HTTP 状态是 `200`，响应内容包含当前版本的 package 记录。

检查安装包下载：

```powershell
$latest = Invoke-RestMethod ('{0}/api/update/latest?productCode=LABEL_PRINT_CLIENT&channel=stable&currentVersion=1.0.0' -f $server)
Invoke-WebRequest $latest.setupDownloadUrl -OutFile 'D:\Demo\LabelPrintClient\artifacts\releases\LabelPrintClient-1.0.1-Setup.exe'
```

预期能下载出首次安装用的 `Setup.exe`。

## 10. 客户端配置

客户端通过 `appsettings.json` 配置更新中心：

```json
{
  "AppSettings": {
    "UpdateServerUrl": "http://127.0.0.1:5051/",
    "UpdateChannel": "stable",
    "AutoCheckUpdates": true
  }
}
```

生产环境示例：

```json
{
  "AppSettings": {
    "UpdateServerUrl": "https://license.example.com/",
    "UpdateChannel": "stable",
    "AutoCheckUpdates": true
  }
}
```

也可以在客户端 Settings 页面修改：

- Update Server URL
- Update Channel
- Auto Check Updates
- Check for Updates

## 11. 首次安装

第一次安装不要直接发 publish 目录。应该把 Velopack 生成的 `Setup.exe` 发给用户：

```text
*-Setup.exe
```

用户运行 setup 后，客户端会以 Velopack 应用方式安装。后续才能自动更新。

## 12. 验证客户端自动更新

推荐完整验证流程：

1. 先安装旧版本，例如 `1.0.0`。
2. 在 `LicenseServer` 上传并启用新版本，例如 `1.0.1`。
3. 打开旧版本客户端。
4. 登录并进入主窗口。
5. 客户端自动检查更新。
6. 普通更新：确认后下载、安装、重启。
7. 强制更新：必须更新；拒绝或失败会退出客户端。

也可以手动验证：

1. 打开客户端 Settings。
2. 点击 Check for Updates。
3. 确认提示、下载、重启行为是否符合预期。

## 13. 回滚策略

如果新版本发布后发现问题：

1. 进入 `LicenseServer` 的 Updates 页面。
2. 禁用问题版本的 Active。
3. 确认 `/api/update/latest` 不再返回问题版本。
4. 发布一个更高版本号的修复包，例如 `1.0.2`。

注意：已经升级到问题版本的客户端通常不会自动“降级”到旧版本。要修复已升级用户，应该发布更高版本号的新包。

不建议直接删除服务器上的 release 文件。先禁用 Active，确认没有客户端继续拉取后，再清理文件。

## 14. 常见问题

### 客户端提示没有更新

检查：

- 新版本号是否大于客户端当前版本。
- Product Code 是否是 `LABEL_PRINT_CLIENT`。
- Channel 是否一致，例如都是 `stable`。
- 后台 release 是否 Active。
- 客户端 `UpdateServerUrl` 是否正确。
- 服务端 `/api/update/latest` 是否返回 `hasUpdate: true`。

### Velopack 更新没有执行

如果客户端是从 Visual Studio、`dotnet run`、publish 目录直接启动，Velopack 可能不能安装更新。请用 Velopack 生成的 `Setup.exe` 安装后再验证。

### `/RELEASES` 或 `releases.stable.json` 返回 404

检查上传 zip 中是否包含 Velopack feed 文件。可以重新生成并压缩：

```powershell
Get-ChildItem 'D:\Demo\LabelPrintClient\artifacts\releases\LabelPrintClient-1.0.1' -Recurse
```

### 上传失败

检查：

- zip 文件大小是否超过服务器配置 `MaxUpdateUploadBytes`。
- zip 内是否包含非法路径，例如 `..` 或绝对路径。
- `LicenseServer` 是否有权限写入 `UpdatePackageRoot`。

### 本地 HTTP 可以，生产不行

检查：

- 生产 `UpdateServerUrl` 是否使用 HTTPS。
- 证书是否可信。
- 反向代理是否允许下载 `.nupkg`、`.exe`、`RELEASES`、`releases.stable.json`。
- 代理是否限制上传文件大小。

## 15. 发布检查清单

发布前：

- [ ] `LabelPrintClient.csproj` 版本号已递增。
- [ ] Release build 通过。
- [ ] 核心测试通过。
- [ ] `dotnet publish` 成功。
- [ ] `vpk pack` 成功。
- [ ] zip 包包含 Velopack feed 文件、`.nupkg`、`Setup.exe`。

发布后：

- [ ] `LicenseServer` Updates 页面能看到新版本。
- [ ] `/api/update/latest` 返回 `hasUpdate: true`。
- [ ] `/api/update/velopack/LABEL_PRINT_CLIENT/stable/RELEASES` 或 `/api/update/velopack/LABEL_PRINT_CLIENT/stable/releases.stable.json` 返回 200。
- [ ] 旧版本客户端能检测到更新。
- [ ] 普通更新或强制更新流程符合预期。
- [ ] 新版本启动后显示当前版本正确。

## 16. 参考链接

- Velopack: https://velopack.io/
- Velopack CLI: https://docs.velopack.io/

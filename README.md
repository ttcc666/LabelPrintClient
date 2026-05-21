# LabelPrintClient - WPF 标签打印客户端

这是一个基于 **WPF + SqlSugarCore + ClosedXML + Stimulsoft Reports.NET / WinForms designer-viewer** 的标签打印客户端示例项目。

项目按你的业务逻辑设计：

- 模板维护阶段只维护分类、标签模板、字段、STI 模板。
- 用户打印阶段选择模板、下载 Excel、导入 Excel 数据。
- Excel 导入数据必须入库。
- 用户从已导入批次中勾选数据行，再预览或打印。
- 支持两种运行模式：
  - 本地模式：SQLite + 本地文件夹保存 `.mrt` 模板。
  - 局域网模式：PostgreSQL + 数据库保存 `.mrt` 模板二进制内容。

## 环境要求

- Windows 10/11
- Visual Studio 2022
- .NET 8 SDK
- Stimulsoft Reports.NET 授权或试用授权

> WPF 是 Windows 桌面技术，所以项目需要在 Windows 上编译运行。

## 主要依赖

项目引用：

- SqlSugarCore
- ClosedXML
- Stimulsoft.Reports.Net
- Npgsql
- System.Data.SQLite.Core

## 快速运行

1. 解压项目。
2. 使用 Visual Studio 2022 打开：

   ```text
   src/LabelPrintClient/LabelPrintClient.csproj
   ```

3. 还原 NuGet 包。
4. 确认 `appsettings.json`：

   ```json
   {
     "RunMode": "LocalSqlite",
     "SqliteConnection": "DataSource=Data/label_print.db",
     "PostgreSqlConnection": "Host=127.0.0.1;Port=5432;Username=postgres;Database=label_print;",
     "LocalTemplateFolder": "Templates",
     "OperatorName": "admin"
   }
   ```

5. 启动程序。
6. 进入「模板维护」，点击「初始化示例数据」。
7. 给示例模板上传或设计 `.mrt` 文件。
8. 进入「标签打印中心」，选择模板，下载 Excel 模板，填写后导入。
9. 勾选有效行，点击预览或打印。

## PostgreSQL 模式

把 `appsettings.json` 改成：

```json
{
  "RunMode": "LanPostgreSql",
  "SqliteConnection": "DataSource=Data/label_print.db",
  "PostgreSqlConnection": "Host=192.168.1.100;Port=5432;Username=postgres;Database=label_print;",
  "LocalTemplateFolder": "Templates",
  "OperatorName": "admin"
}
```

在 PostgreSQL 中创建空数据库 `label_print`，启动程序后会自动 CodeFirst 建表。
如果 PostgreSQL 需要密码，请在本机运行配置中追加 `Password=你的密码;`，不要把真实密码提交到仓库。

## 业务流程

```text
管理员：
维护分类 -> 维护模板 -> 维护字段 -> 上传/设计 STI 模板

普通用户：
选择分类 -> 选择模板 -> 下载 Excel 模板 -> 填写 Excel -> 导入入库 -> 勾选数据行 -> 预览/打印
```

## 数据库核心表

- `label_category`：标签分类
- `label_template`：标签模板，兼容本地文件和数据库二进制两种存储
- `label_template_field`：模板字段，也是 Excel 表头和 STI 数据源字段的来源
- `label_import_batch`：Excel 导入批次
- `label_import_row`：Excel 导入行数据，动态字段用 JSON 存储
- `label_print_job`：打印任务
- `label_print_job_row`：打印任务明细，用于追溯当时打印了哪些数据

## STI 模板设计规范

- 数据源名称默认：`LabelData`
- 字段名使用 `label_template_field.FieldCode`
- 例如字段：

```text
ProductName
Barcode
Spec
Qty
ProduceDate
```

STI 文本组件可以绑定：

```text
{LabelData.ProductName}
{LabelData.Barcode}
```

批量标签建议使用 DataBand 绑定 `LabelData`，每一行数据打印一张标签。

## 说明

当前应用主体仍是 WPF，Stimulsoft 预览和设计器使用 WinForms 独立窗口打开。Stimulsoft 调用集中在 `Services/Stimulsoft` 和打印服务中；如果你的本地 Stimulsoft 版本 API 有微小差异，只需要调整这一层。

Stimulsoft 设计器和预览默认加载 `Localization/zh-CHS.xml` 简体中文语言文件；该文件在构建时从 `Stimulsoft.Reports.Engine` NuGet 包复制到输出目录。

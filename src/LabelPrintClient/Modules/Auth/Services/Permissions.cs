using LabelPrintClient.Modules.Auth.Models;

namespace LabelPrintClient.Modules.Auth.Services;

public static class Permissions
{
    public const string MenuPrintCenter = "Menu.PrintCenter";
    public const string MenuPrintHistory = "Menu.PrintHistory";
    public const string MenuTaskCenter = "Menu.TaskCenter";
    public const string MenuTemplateManage = "Menu.TemplateManage";
    public const string MenuSettings = "Menu.Settings";
    public const string MenuAccountPermission = "Menu.AccountPermission";

    public const string PrintCenterRefresh = "Button.PrintCenter.Refresh";
    public const string PrintCenterDownloadExcel = "Button.PrintCenter.DownloadExcel";
    public const string PrintCenterImportExcel = "Button.PrintCenter.ImportExcel";
    public const string PrintCenterBatchSearch = "Button.PrintCenter.BatchSearch";
    public const string PrintCenterLoadBatches = "Button.PrintCenter.LoadBatches";
    public const string PrintCenterVoidBatch = "Button.PrintCenter.VoidBatch";
    public const string PrintCenterRowSearch = "Button.PrintCenter.RowSearch";
    public const string PrintCenterSelectRows = "Button.PrintCenter.SelectRows";
    public const string PrintCenterPreview = "Button.PrintCenter.Preview";
    public const string PrintCenterPrint = "Button.PrintCenter.Print";
    public const string PrintCenterReprint = "Button.PrintCenter.Reprint";

    public const string PrintHistoryRefresh = "Button.PrintHistory.Refresh";
    public const string PrintHistorySearch = "Button.PrintHistory.Search";
    public const string PrintHistoryRetry = "Button.PrintHistory.Retry";
    public const string PrintHistoryReprint = "Button.PrintHistory.Reprint";

    public const string TaskCenterSearch = "Button.TaskCenter.Search";
    public const string TaskCenterClearCompleted = "Button.TaskCenter.ClearCompleted";

    public const string TemplateRefresh = "Button.Template.Refresh";
    public const string TemplateSeedDemo = "Button.Template.SeedDemo";
    public const string TemplateCategoryCreate = "Button.Template.Category.Create";
    public const string TemplateCategoryEdit = "Button.Template.Category.Edit";
    public const string TemplateCategoryDelete = "Button.Template.Category.Delete";
    public const string TemplateCategorySearch = "Button.Template.Category.Search";
    public const string TemplateCreate = "Button.Template.Create";
    public const string TemplateEdit = "Button.Template.Edit";
    public const string TemplateDelete = "Button.Template.Delete";
    public const string TemplateUpload = "Button.Template.Upload";
    public const string TemplateDesign = "Button.Template.Design";
    public const string TemplateSearch = "Button.Template.Search";
    public const string TemplateFieldCreate = "Button.Template.Field.Create";
    public const string TemplateFieldEdit = "Button.Template.Field.Edit";
    public const string TemplateFieldDelete = "Button.Template.Field.Delete";
    public const string TemplateFieldHistory = "Button.Template.Field.History";
    public const string TemplateFieldSort = "Button.Template.Field.Sort";
    public const string TemplateFieldSearch = "Button.Template.Field.Search";
    public const string TemplateFieldRestore = "Button.Template.Field.Restore";

    public const string SettingsReload = "Button.Settings.Reload";
    public const string SettingsSave = "Button.Settings.Save";
    public const string SettingsBrowseTemplateFolder = "Button.Settings.BrowseTemplateFolder";
    public const string SettingsTestConnection = "Button.Settings.TestConnection";
    public const string SettingsBackup = "Button.Settings.Backup";
    public const string SettingsRestore = "Button.Settings.Restore";

    public const string AccountUserCreate = "Button.Account.User.Create";
    public const string AccountUserEdit = "Button.Account.User.Edit";
    public const string AccountUserDisable = "Button.Account.User.Disable";
    public const string AccountUserResetPassword = "Button.Account.User.ResetPassword";
    public const string AccountRoleCreate = "Button.Account.Role.Create";
    public const string AccountRoleEdit = "Button.Account.Role.Edit";
    public const string AccountRoleDelete = "Button.Account.Role.Delete";
    public const string AccountRolePermissions = "Button.Account.Role.Permissions";

    public static IReadOnlyList<PermissionDefinition> All { get; } = new List<PermissionDefinition>
    {
        Menu(MenuPrintCenter, "标签打印中心", 10),
        Menu(MenuPrintHistory, "打印记录", 20),
        Menu(MenuTaskCenter, "任务中心", 30),
        Menu(MenuTemplateManage, "模板维护", 40),
        Menu(MenuSettings, "系统配置", 50),
        Menu(MenuAccountPermission, "账号权限", 60),

        Button(PrintCenterRefresh, "刷新打印中心", MenuPrintCenter, 1010),
        Button(PrintCenterDownloadExcel, "下载 Excel 模板", MenuPrintCenter, 1020),
        Button(PrintCenterImportExcel, "导入 Excel", MenuPrintCenter, 1030),
        Button(PrintCenterBatchSearch, "查询导入批次", MenuPrintCenter, 1040),
        Button(PrintCenterLoadBatches, "加载导入批次", MenuPrintCenter, 1050),
        Button(PrintCenterVoidBatch, "作废批次", MenuPrintCenter, 1060),
        Button(PrintCenterRowSearch, "查询明细行", MenuPrintCenter, 1070),
        Button(PrintCenterSelectRows, "选择明细行", MenuPrintCenter, 1080),
        Button(PrintCenterPreview, "预览标签", MenuPrintCenter, 1090),
        Button(PrintCenterPrint, "打印标签", MenuPrintCenter, 1100),
        Button(PrintCenterReprint, "补打标签", MenuPrintCenter, 1110),

        Button(PrintHistoryRefresh, "刷新打印记录", MenuPrintHistory, 2010),
        Button(PrintHistorySearch, "查询打印记录", MenuPrintHistory, 2020),
        Button(PrintHistoryRetry, "失败任务重试", MenuPrintHistory, 2030),
        Button(PrintHistoryReprint, "历史补打", MenuPrintHistory, 2040),

        Button(TaskCenterSearch, "查询任务", MenuTaskCenter, 3010),
        Button(TaskCenterClearCompleted, "清理已结束任务", MenuTaskCenter, 3020),

        Button(TemplateRefresh, "刷新模板维护", MenuTemplateManage, 4010),
        Button(TemplateSeedDemo, "初始化示例数据", MenuTemplateManage, 4020),
        Button(TemplateCategoryCreate, "新增分类", MenuTemplateManage, 4030),
        Button(TemplateCategoryEdit, "编辑分类", MenuTemplateManage, 4040),
        Button(TemplateCategoryDelete, "删除分类", MenuTemplateManage, 4050),
        Button(TemplateCategorySearch, "查询分类", MenuTemplateManage, 4060),
        Button(TemplateCreate, "新增模板", MenuTemplateManage, 4070),
        Button(TemplateEdit, "编辑模板", MenuTemplateManage, 4080),
        Button(TemplateDelete, "删除模板", MenuTemplateManage, 4090),
        Button(TemplateUpload, "上传模板", MenuTemplateManage, 4100),
        Button(TemplateDesign, "设计模板", MenuTemplateManage, 4110),
        Button(TemplateSearch, "查询模板", MenuTemplateManage, 4120),
        Button(TemplateFieldCreate, "新增字段", MenuTemplateManage, 4130),
        Button(TemplateFieldEdit, "编辑字段", MenuTemplateManage, 4140),
        Button(TemplateFieldDelete, "删除字段", MenuTemplateManage, 4150),
        Button(TemplateFieldHistory, "字段历史", MenuTemplateManage, 4160),
        Button(TemplateFieldSort, "字段排序", MenuTemplateManage, 4170),
        Button(TemplateFieldSearch, "查询字段", MenuTemplateManage, 4180),
        Button(TemplateFieldRestore, "恢复历史字段", MenuTemplateManage, 4190),

        Button(SettingsReload, "重新加载配置", MenuSettings, 5010),
        Button(SettingsSave, "保存配置", MenuSettings, 5020),
        Button(SettingsBrowseTemplateFolder, "浏览模板目录", MenuSettings, 5030),
        Button(SettingsTestConnection, "测试数据库连接", MenuSettings, 5040),
        Button(SettingsBackup, "一键备份", MenuSettings, 5050),
        Button(SettingsRestore, "恢复备份", MenuSettings, 5060),

        Button(AccountUserCreate, "新增用户", MenuAccountPermission, 6010),
        Button(AccountUserEdit, "编辑用户", MenuAccountPermission, 6020),
        Button(AccountUserDisable, "启停用户", MenuAccountPermission, 6030),
        Button(AccountUserResetPassword, "重置用户密码", MenuAccountPermission, 6040),
        Button(AccountRoleCreate, "新增角色", MenuAccountPermission, 6050),
        Button(AccountRoleEdit, "编辑角色", MenuAccountPermission, 6060),
        Button(AccountRoleDelete, "删除角色", MenuAccountPermission, 6070),
        Button(AccountRolePermissions, "维护角色权限", MenuAccountPermission, 6080)
    };

    private static PermissionDefinition Menu(string key, string name, int sort) =>
        new(key, name, AuthPermissionResourceType.Menu, null, sort);

    private static PermissionDefinition Button(string key, string name, string parentKey, int sort) =>
        new(key, name, AuthPermissionResourceType.Button, parentKey, sort);
}

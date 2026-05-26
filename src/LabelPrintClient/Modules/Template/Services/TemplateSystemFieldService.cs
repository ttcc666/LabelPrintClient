using LabelPrintClient.Database;
using LabelPrintClient.Infrastructure;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Modules.Template.Services;

public static class TemplateSystemFieldService
{
    private const string SystemFieldRemark = "系统自动生成的模板类型固定字段，禁止修改与删除";

    public static async Task EnsureModeFieldsAsync(LabelTemplate template)
    {
        var fields = await AppDb.Db.Queryable<LabelTemplateField>()
            .Where(x => x.TemplateId == template.Id)
            .ToListAsync();

        var targetCode = template.TemplateMode switch
        {
            LabelTemplateMode.Batch => TemplateSystemFields.BatchNo,
            LabelTemplateMode.Serialized => TemplateSystemFields.SerialNo,
            _ => null
        };

        foreach (var field in fields.Where(x => TemplateSystemFields.IsManagedSystemField(x.FieldCode)))
        {
            if (!string.Equals(field.FieldCode, targetCode, StringComparison.OrdinalIgnoreCase) && !field.IsDeleted)
            {
                field.IsDeleted = true;
                await AppDb.Db.Updateable(field).UpdateColumns(x => x.IsDeleted).ExecuteCommandAsync();
            }
        }

        if (targetCode == null)
            return;

        var existing = fields.FirstOrDefault(x => string.Equals(x.FieldCode, targetCode, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            await AppDb.Db.Insertable(NewSystemField(template.Id, targetCode)).ExecuteCommandAsync();
            return;
        }

        existing.FieldName = TemplateSystemFields.GetDisplayName(targetCode);
        existing.FieldType = "string";
        existing.IsRequired = true;
        existing.Sort = existing.Sort <= 0 ? 5 : existing.Sort;
        existing.Remark = SystemFieldRemark;
        existing.IsDeleted = false;
        await AppDb.Db.Updateable(existing).ExecuteCommandAsync();
    }

    private static LabelTemplateField NewSystemField(long templateId, string fieldCode)
    {
        return new LabelTemplateField
        {
            Id = IdHelper.NewId(),
            TemplateId = templateId,
            FieldName = TemplateSystemFields.GetDisplayName(fieldCode),
            FieldCode = fieldCode,
            FieldType = "string",
            IsRequired = true,
            Sort = 5,
            Remark = SystemFieldRemark
        };
    }
}

using System.Data;
using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;

namespace LabelPrintClient.Modules.PrintCenter.Services;

public record PrintContext(
    LabelTemplate Template,
    List<LabelTemplateField> Fields,
    List<LabelImportRow> Rows,
    DataTable DataTable);

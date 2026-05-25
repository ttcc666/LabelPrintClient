using LabelPrintClient.Modules.PrintCenter.Models;
using LabelPrintClient.Modules.Template.Models;
using System.Data;

namespace LabelPrintClient.Modules.PrintCenter.Services;

public record PrintContext(
    LabelTemplate Template,
    LabelImportBatch Batch,
    List<LabelTemplateField> Fields,
    List<LabelImportRow> Rows,
    DataTable DataTable);

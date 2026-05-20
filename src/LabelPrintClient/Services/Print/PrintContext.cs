using System.Data;
using LabelPrintClient.Models;

namespace LabelPrintClient.Services.Print;

public record PrintContext(
    LabelTemplate Template,
    List<LabelTemplateField> Fields,
    List<LabelImportRow> Rows,
    DataTable DataTable);

using LabelPrintClient.Modules.Auth.Models;

namespace LabelPrintClient.Modules.Auth.Services;

public sealed record PermissionDefinition(
    string Key,
    string Name,
    AuthPermissionResourceType ResourceType,
    string? ParentKey,
    int Sort);

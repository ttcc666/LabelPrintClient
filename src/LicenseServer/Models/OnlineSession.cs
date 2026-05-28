using SqlSugar;

namespace LicenseServer.Models;

[SugarTable("license_online_session")]
[SugarIndex("ix_online_session_token_hash", nameof(TokenHash), OrderByType.Asc, true)]
[SugarIndex("ix_online_session_license_machine", nameof(LicenseId), OrderByType.Asc, nameof(MachineCode), OrderByType.Asc)]
public sealed class OnlineSession
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long LicenseId { get; set; }

    [SugarColumn(Length = 120)]
    public string MachineCode { get; set; } = string.Empty;

    [SugarColumn(Length = 120, IsNullable = true)]
    public string? MachineName { get; set; }

    [SugarColumn(Length = 128)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime LoginTime { get; set; } = DateTime.Now;

    public DateTime LastHeartbeat { get; set; } = DateTime.Now;

    public DateTime ExpireTime { get; set; }
}

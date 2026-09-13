namespace SoundReborn.Core.Models;

/// <summary>Un membre d'une zone multi-pièces.</summary>
public sealed class ZoneMember
{
    public string DeviceId { get; init; } = "";
    public string IpAddress { get; init; } = "";
    public string? Role { get; init; }
}

/// <summary>
/// État de GET /getZone. Pour une enceinte seule, le firmware renvoie un
/// &lt;zone /&gt; vide : Master est alors vide et Members est une liste vide.
/// </summary>
public sealed class ZoneState
{
    public string? MasterDeviceId { get; init; }
    public string? SenderIp { get; init; }
    public IReadOnlyList<ZoneMember> Members { get; init; } = Array.Empty<ZoneMember>();

    public bool IsGrouped => Members.Count > 1;

    public static ZoneState Empty { get; } = new();
}

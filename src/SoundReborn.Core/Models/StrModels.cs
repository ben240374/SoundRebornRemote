using System.Text.Json.Serialization;

namespace SoundReborn.Core.Models;

/// <summary>Réponse de GET /api/agent/version : version de l'agent STR installé sur l'enceinte.</summary>
public sealed class AgentInfo
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("build")] public string? Build { get; set; }
    [JsonPropertyName("friendlyName")] public string? FriendlyName { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }

    /// <summary>« v0.9.79 » — préfixé du v si l'agent ne le fait pas lui-même.</summary>
    [JsonIgnore]
    public string DisplayVersion
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Version))
            {
                return "";
            }

            var v = Version!.Trim();
            return v.StartsWith('v') ? v : "v" + v;
        }
    }
}

/// <summary>Réponse de GET /api/box/volume sur l'agent STR.</summary>
public sealed class StrVolume
{
    [JsonPropertyName("value")] public int Value { get; set; }
    [JsonPropertyName("target")] public int Target { get; set; }
    [JsonPropertyName("muted")] public bool Muted { get; set; }
}

/// <summary>Une présélection du magasin STR (GET /api/presets).</summary>
public sealed class StrPreset
{
    [JsonPropertyName("slot")] public int Slot { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("stream_url")] public string? StreamUrl { get; set; }
    [JsonPropertyName("uri")] public string? Uri { get; set; }
    [JsonPropertyName("art")] public string? Art { get; set; }
    [JsonPropertyName("account")] public string? Account { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("homepage")] public string? Homepage { get; set; }
    [JsonPropertyName("bitrate")] public int Bitrate { get; set; }
    [JsonPropertyName("codec")] public string? Codec { get; set; }

    [JsonIgnore] public bool IsSpotify => string.Equals(Type, "spotify", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Présélection {Slot}" : Name!;
}

/// <summary>Enveloppe tolérée par STR : soit un tableau nu, soit {"presets":[...]}.</summary>
public sealed class StrPresetEnvelope
{
    [JsonPropertyName("presets")] public List<StrPreset>? Presets { get; set; }
}

/// <summary>Une entrée de GET /api/recent (30 derniers titres, toutes sources).</summary>
public sealed class RecentEntry
{
    [JsonPropertyName("ts")] public string? Timestamp { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("cardKey")] public string? CardKey { get; set; }
    [JsonPropertyName("cardName")] public string? CardName { get; set; }
    [JsonPropertyName("cardArt")] public string? CardArt { get; set; }
    [JsonPropertyName("cardURL")] public string? CardUrl { get; set; }
    [JsonPropertyName("mime")] public string? Mime { get; set; }
    [JsonPropertyName("track")] public string? Track { get; set; }
    [JsonPropertyName("account")] public string? Account { get; set; }
    [JsonPropertyName("homepage")] public string? Homepage { get; set; }

    [JsonIgnore] public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Track) ? Track! :
        !string.IsNullOrWhiteSpace(CardName) ? CardName! : "(inconnu)";

    [JsonIgnore] public string DisplaySubtitle =>
        string.IsNullOrWhiteSpace(Track) ? (Source ?? "") : $"{CardName} · {Source}";

    /// <summary>Vrai quand l'entrée porte une cible rejouable via POST /api/play.</summary>
    [JsonIgnore] public bool CanReplay =>
        !string.IsNullOrWhiteSpace(CardUrl) &&
        CardUrl!.StartsWith("http", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Un membre de zone tel que l'agent STR le décrit dans /api/box/zone/volume.</summary>
public sealed class StrZoneMemberVolume
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("ip")] public string? Ip { get; set; }
    [JsonPropertyName("deviceID")] public string? DeviceId { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("isSelf")] public bool IsSelf { get; set; }
    [JsonPropertyName("isMaster")] public bool IsMaster { get; set; }

    /// <summary>-1 quand l'enceinte n'a pas répondu.</summary>
    [JsonPropertyName("volume")] public int Volume { get; set; }
    [JsonPropertyName("muted")] public bool Muted { get; set; }

    [JsonIgnore] public bool IsReachable => Volume >= 0;
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? (Ip ?? "?") : Name!;
}

/// <summary>Réponse de GET /api/box/zone/volume.</summary>
public sealed class StrZoneVolume
{
    [JsonPropertyName("grouped")] public bool Grouped { get; set; }
    [JsonPropertyName("stereo")] public bool Stereo { get; set; }
    [JsonPropertyName("average")] public int Average { get; set; }
    [JsonPropertyName("members")] public List<StrZoneMemberVolume> Members { get; set; } = new();
}

/// <summary>Un membre de zone dans le corps de POST /api/box/zone.</summary>
public sealed class StrZoneMemberRef
{
    [JsonPropertyName("deviceID")] public string? DeviceId { get; set; }
    [JsonPropertyName("ip")] public string? Ip { get; set; }
}

/// <summary>Corps de POST /api/box/zone (former un groupe).</summary>
public sealed class StrZoneFormRequest
{
    [JsonPropertyName("master")] public StrZoneMemberRef Master { get; set; } = new();
    [JsonPropertyName("slaves")] public List<StrZoneMemberRef> Slaves { get; set; } = new();

    /// <summary>"native" (diffusion firmware) ou "mirror". Laisser "native".</summary>
    [JsonPropertyName("mode")] public string Mode { get; set; } = "native";

    /// <summary>Groupe permanent : re-formé automatiquement à la prochaine lecture du maître.</summary>
    [JsonPropertyName("permanent")] public bool Permanent { get; set; }
}

/// <summary>Corps de POST /api/play (jouer une URL arbitraire).</summary>
public sealed class StrPlayUrlRequest
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("icon")] public string? Icon { get; set; }
    [JsonPropertyName("mime")] public string? Mime { get; set; }
}

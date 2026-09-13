using System.Text.Json.Serialization;

namespace SoundReborn.Core.Models;

/// <summary>
/// Une station telle que radio-browser.info la décrit.
/// Depuis que l'agent STR ne compile plus le client radio-browser, la recherche
/// se fait côté application : c'est ce modèle qui arrive du service public.
/// </summary>
public sealed class RadioStation
{
    [JsonPropertyName("stationuuid")] public string Uuid { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("url_resolved")] public string? UrlResolved { get; set; }
    [JsonPropertyName("favicon")] public string? Favicon { get; set; }
    [JsonPropertyName("homepage")] public string? Homepage { get; set; }
    [JsonPropertyName("tags")] public string? Tags { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
    [JsonPropertyName("countrycode")] public string? CountryCode { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("codec")] public string? Codec { get; set; }
    [JsonPropertyName("bitrate")] public int Bitrate { get; set; }
    [JsonPropertyName("hls")] public int Hls { get; set; }
    [JsonPropertyName("votes")] public int Votes { get; set; }
    [JsonPropertyName("clickcount")] public int ClickCount { get; set; }
    [JsonPropertyName("lastcheckok")] public int LastCheckOk { get; set; }

    /// <summary>URL de flux à privilégier : celle que radio-browser a résolue.</summary>
    [JsonIgnore]
    public string PlayableUrl => string.IsNullOrWhiteSpace(UrlResolved) ? Url : UrlResolved!;

    [JsonIgnore]
    public bool IsHls => Hls == 1 ||
                         PlayableUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Estime si l'enceinte saura lire ce flux. L'agent fait passer tout le monde
    /// par son proxy (donc TLS n'est plus un souci) et convertit le HLS à la volée.
    /// Restent hors course les codecs que ni la box ni le proxy ne décodent :
    /// Ogg, Opus, FLAC. Un codec inconnu est laissé passer — l'enceinte essaiera.
    /// </summary>
    [JsonIgnore]
    public bool IsBoseCompatible
    {
        get
        {
            if (IsHls)
            {
                return true;
            }

            var codec = (Codec ?? "").ToUpperInvariant();

            if (codec.Length == 0 || codec == "UNKNOWN")
            {
                return true;
            }

            return codec is "MP3" or "AAC" or "AAC+" or "AACP" or "MPEG";
        }
    }

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var parts = new List<string>(3);

            if (Bitrate > 0)
            {
                parts.Add($"{Bitrate} kbit/s");
            }

            if (!string.IsNullOrWhiteSpace(Codec) && !Codec!.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(Codec!);
            }

            if (!string.IsNullOrWhiteSpace(Country))
            {
                parts.Add(Country!);
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Convertit la station en présélection du magasin STR.</summary>
    public StrPreset ToPreset(int slot) => new()
    {
        Slot = slot,
        Name = Name,
        Type = "radio",
        StreamUrl = PlayableUrl,
        Art = Favicon,
        Homepage = Homepage,
        Bitrate = Bitrate,
        Codec = Codec,
    };
}

/// <summary>Critères de recherche envoyés à radio-browser.</summary>
public sealed class RadioSearchOptions
{
    public string? Name { get; set; }

    /// <summary>Code pays ISO à deux lettres, « BE » par exemple. Null = tous.</summary>
    public string? CountryCode { get; set; }

    /// <summary>Nom anglais de la langue en minuscules, « french » par exemple. Null = toutes.</summary>
    public string? Language { get; set; }

    public string? Tag { get; set; }

    public int Limit { get; set; } = 60;

    /// <summary>Masque côté serveur les stations dont le dernier contrôle a échoué.</summary>
    public bool HideBroken { get; set; } = true;

    /// <summary>Ne garde que ce que l'enceinte sait lire (filtrage côté application).</summary>
    public bool BoseCompatibleOnly { get; set; } = true;
}

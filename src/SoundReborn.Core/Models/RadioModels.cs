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
    [JsonPropertyName("clicktrend")] public int ClickTrend { get; set; }
    [JsonPropertyName("lastcheckok")] public int LastCheckOk { get; set; }

    /// <summary>
    /// Date du dernier contrôle du flux par radio-browser. Vide, ou faite de zéros
    /// selon le miroir, pour une station que l'annuaire n'a pas encore contrôlée —
    /// typiquement une station qu'on vient d'y ajouter.
    /// </summary>
    [JsonPropertyName("lastchecktime")] public string? LastCheckTime { get; set; }

    /// <summary>Jamais contrôlée par radio-browser : une entrée toute neuve, pas une station morte.</summary>
    [JsonIgnore]
    public bool IsNeverChecked
    {
        get
        {
            var time = (LastCheckTime ?? "").Trim();
            return time.Length == 0 || time.StartsWith("0000-00-00", StringComparison.Ordinal);
        }
    }

    /// <summary>Contrôlée ET trouvée hors service au dernier passage de radio-browser.</summary>
    [JsonIgnore]
    public bool IsCheckedBroken => LastCheckOk != 1 && !IsNeverChecked;

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

/// <summary>Ordres de tri de la recherche, tels que radio-browser les nomme.</summary>
public static class RadioOrder
{
    /// <summary>Votes des auditeurs : la « popularité » de l'application PC de STR.</summary>
    public const string Votes = "votes";

    /// <summary>Nombre d'écoutes.</summary>
    public const string Clicks = "clickcount";

    /// <summary>Progression des écoutes sur 24 heures.</summary>
    public const string Trend = "clicktrend";

    /// <summary>Ordre alphabétique.</summary>
    public const string Name = "name";

    /// <summary>Débit, le plus haut d'abord.</summary>
    public const string Bitrate = "bitrate";

    /// <summary>Ordre connu, ou les votes pour une valeur inconnue ou vide.</summary>
    public static string Normalize(string? order) => order switch
    {
        Clicks or Trend or Name or Bitrate => order,
        _ => Votes,
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

    /// <summary>Genre (étiquette radio-browser), « jazz » par exemple. Null = tous.</summary>
    public string? Tag { get; set; }

    /// <summary>Ordre de tri, voir <see cref="RadioOrder"/>.</summary>
    public string Order { get; set; } = RadioOrder.Votes;

    public int Limit { get; set; } = 60;

    /// <summary>Position dans la liste de l'annuaire, pour « Voir plus ».</summary>
    public int Offset { get; set; }

    /// <summary>
    /// « En service uniquement » : écarte les stations que radio-browser a
    /// contrôlées et trouvées hors service. Pour une recherche par nom ou par genre,
    /// une station pas encore contrôlée (ajoutée il y a peu) reste visible.
    /// </summary>
    public bool HideBroken { get; set; } = true;

    /// <summary>Ne garde que ce que l'enceinte sait lire (filtrage côté application).</summary>
    public bool BoseCompatibleOnly { get; set; } = true;
}

/// <summary>Une page de résultats de l'annuaire.</summary>
public sealed class RadioSearchPage
{
    public required IReadOnlyList<RadioStation> Stations { get; init; }

    /// <summary>
    /// Vrai quand la recherche par nom n'a trouvé que des stations signalées hors
    /// service et les montre quand même : radio-browser contrôle depuis l'étranger,
    /// et une radio réservée à son pays y paraît morte alors qu'elle joue très bien
    /// chez soi.
    /// </summary>
    public bool Relaxed { get; init; }

    /// <summary>Nombre de stations reçues de l'annuaire, avant tout filtre.</summary>
    public int Fetched { get; init; }

    /// <summary>Position de la page suivante, pour « Voir plus ».</summary>
    public int NextOffset { get; init; }

    /// <summary>L'annuaire a sans doute d'autres stations à donner.</summary>
    public bool HasMore { get; init; }
}

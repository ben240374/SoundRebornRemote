using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoundReborn.Core.Models;

/// <summary>
/// Un favori STR (GET/POST/DELETE /api/favorites) : les stations étoilées que
/// l'application STR sur ordinateur garde, et que l'agent répand sur toutes les
/// enceintes STR. La forme est celle d'une station de radio-browser, avec en plus
/// les champs d'une piste de bibliothèque (upnp) ou d'une station Pandora.
///
/// Sa clé, celle que DELETE attend : le stationuuid, sinon « nom|url ». Une
/// station Pandora n'a pas d'adresse ; sa clé est « pandora:&lt;location&gt; ».
/// L'agent rend la clé exacte quand on ajoute un favori.
/// </summary>
public sealed class StrFavorite
{
    [JsonPropertyName("stationuuid")] public string? Uuid { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("url_resolved")] public string? UrlResolved { get; set; }
    [JsonPropertyName("codec")] public string? Codec { get; set; }

    [JsonPropertyName("bitrate")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? Bitrate { get; set; }

    [JsonPropertyName("favicon")] public string? Favicon { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
    [JsonPropertyName("homepage")] public string? Homepage { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("location")] public string? Location { get; set; }
    [JsonPropertyName("itemType")] public string? ItemType { get; set; }

    /// <summary>La clé du favori pour l'agent.</summary>
    [JsonIgnore]
    public string Key
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Uuid))
            {
                return Uuid!;
            }

            if (IsPandora)
            {
                return "pandora:" + (Location ?? "");
            }

            return (Name ?? "") + "|" + (Url ?? "");
        }
    }

    [JsonIgnore]
    public bool IsPandora => string.Equals(Source, "pandora", StringComparison.OrdinalIgnoreCase);

    /// <summary>L'adresse à jouer : celle que radio-browser a résolue, sinon l'adresse donnée.</summary>
    [JsonIgnore]
    public string PlayableUrl => !string.IsNullOrWhiteSpace(UrlResolved) ? UrlResolved!.Trim() : (Url ?? "").Trim();

    /// <summary>
    /// Vrai quand l'application sait le lancer : une adresse http(s). Une station
    /// Pandora se lance par l'application STR, pas par une adresse.
    /// </summary>
    [JsonIgnore]
    public bool CanPlay => !IsPandora && LocalPresetList.IsStreamUrl(PlayableUrl);

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? PlayableUrl : Name!.Trim();

    /// <summary>La station radio-browser correspondante : pour l'écouter ou la mettre sur une touche.</summary>
    public RadioStation ToStation() => new()
    {
        Uuid = Uuid ?? "",
        Name = DisplayName,
        Url = (Url ?? "").Trim().Length > 0 ? Url!.Trim() : PlayableUrl,
        UrlResolved = string.IsNullOrWhiteSpace(UrlResolved) ? null : UrlResolved!.Trim(),
        Codec = Codec,
        Bitrate = Bitrate ?? 0,
        Favicon = Favicon,
        Country = Country,
        Homepage = Homepage,
        LastCheckOk = 1,
    };

    /// <summary>Le favori d'une station trouvée par la recherche : ses champs de l'annuaire, tels quels.</summary>
    public static StrFavorite FromStation(RadioStation station) => new()
    {
        Uuid = string.IsNullOrWhiteSpace(station.Uuid) ? null : station.Uuid,
        Name = LocalPresetList.CleanName(station.Name),
        Url = station.Url,
        UrlResolved = string.IsNullOrWhiteSpace(station.UrlResolved) ? null : station.UrlResolved,
        Codec = string.IsNullOrWhiteSpace(station.Codec) ? null : station.Codec,
        Bitrate = station.Bitrate > 0 ? station.Bitrate : null,
        Favicon = string.IsNullOrWhiteSpace(station.Favicon) ? null : station.Favicon,
        Country = string.IsNullOrWhiteSpace(station.Country) ? null : station.Country,
        Homepage = string.IsNullOrWhiteSpace(station.Homepage) ? null : station.Homepage,
    };

    /// <summary>Le favori d'une radio gardée dans le téléphone (pas d'identifiant d'annuaire).</summary>
    public static StrFavorite FromLocal(LocalPreset preset) => new()
    {
        Name = LocalPresetList.CleanName(preset.Name),
        Url = preset.Url,
        Codec = string.IsNullOrWhiteSpace(preset.Codec) ? null : preset.Codec,
        Favicon = ArtFirst(preset.Art),
    };

    /// <summary>
    /// Lit la liste de l'agent. Une entrée illisible est passée plutôt que de
    /// perdre toute la liste ; lève <see cref="JsonException"/> si la réponse
    /// n'est pas un tableau JSON.
    /// </summary>
    public static List<StrFavorite> ParseList(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("La liste des favoris n'est pas un tableau JSON.");
        }

        var list = new List<StrFavorite>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            try
            {
                if (element.Deserialize<StrFavorite>() is { } favorite &&
                    (!string.IsNullOrWhiteSpace(favorite.Name) || favorite.PlayableUrl.Length > 0 || favorite.IsPandora))
                {
                    list.Add(favorite);
                }
            }
            catch (JsonException)
            {
                // Entrée d'une forme inattendue : on garde les autres.
            }
        }

        return list;
    }

    private static string? ArtFirst(string? art)
    {
        var first = (art ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }
}

/// <summary>La réponse de POST /api/favorites : {"ok":true,"added":false,"id":"…"}.</summary>
public sealed class StrFavoriteAdded
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }

    /// <summary>Faux quand l'enceinte avait déjà ce favori.</summary>
    [JsonPropertyName("added")] public bool Added { get; set; }

    [JsonPropertyName("id")] public string? Id { get; set; }
}

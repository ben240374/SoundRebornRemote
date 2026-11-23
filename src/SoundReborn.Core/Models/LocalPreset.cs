using System.Text.Json;
using System.Text.Json.Serialization;
using SoundReborn.Core.Recording;

namespace SoundReborn.Core.Models;

/// <summary>
/// Une présélection « Local » : une radio gardée dans le téléphone, pas dans
/// l'enceinte. Elle se lance sur n'importe quelle enceinte qui a l'agent STR
/// (POST /api/play), mais aucune touche physique ne la connaît.
/// </summary>
public sealed class LocalPreset
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>Adresse du flux, http ou https.</summary>
    [JsonPropertyName("url")] public string Url { get; set; } = "";

    /// <summary>Logo : une adresse, ou une chaîne de candidats séparés par « | » comme l'agent les range.</summary>
    [JsonPropertyName("art")] public string? Art { get; set; }

    /// <summary>
    /// Codec tel que l'annuaire le donne (MP3, AAC, AAC+). L'agent en a besoin pour
    /// un flux HE-AAC, qui jouerait du silence sans lui ; inconnu, il reste vide.
    /// </summary>
    [JsonPropertyName("codec")] public string? Codec { get; set; }
}

/// <summary>
/// La liste des présélections « Local » : lecture tolérante, écriture, et les
/// règles qu'une entrée doit suivre. Sans état : l'application garde la liste
/// dans ses préférences.
/// </summary>
public static class LocalPresetList
{
    /// <summary>Au-delà, la liste ne sert plus de raccourci.</summary>
    public const int MaxCount = 100;

    /// <summary>La même limite que l'agent pour le nom d'une touche.</summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// Longueur au plus d'une adresse (flux ou logo). Une entrée lue dans un fichier
    /// de sauvegarde ne doit pas pouvoir garnir les préférences de textes démesurés.
    /// </summary>
    public const int MaxUrlLength = 2048;

    private const int MaxCodecLength = 16;

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Lit la liste enregistrée. Une entrée sans nom ou sans adresse de flux est
    /// écartée, de même qu'une seconde entrée pour la même adresse ; un texte
    /// illisible donne une liste vide, jamais une exception.
    /// </summary>
    public static List<LocalPreset> Parse(string? json)
    {
        var result = new List<LocalPreset>();

        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        List<LocalPreset>? raw;

        try
        {
            raw = JsonSerializer.Deserialize<List<LocalPreset>>(json, Options);
        }
        catch (JsonException)
        {
            return result;
        }

        return Sanitize(raw ?? new List<LocalPreset>());
    }

    /// <summary>
    /// Nettoie une liste venue d'ailleurs (préférences, fichier de sauvegarde) :
    /// entrées sans nom ou sans adresse de flux écartées, doublons d'adresse
    /// écartés, coupée à <see cref="MaxCount"/>. Les entrées rendues sont des copies.
    /// </summary>
    public static List<LocalPreset> Sanitize(IEnumerable<LocalPreset?> presets)
    {
        var result = new List<LocalPreset>();

        foreach (var entry in presets)
        {
            if (entry is null || entry.Url.Length > MaxUrlLength || !IsStreamUrl(entry.Url))
            {
                continue;
            }

            var name = CleanName(entry.Name);

            if (name.Length == 0 || Contains(result, entry.Url))
            {
                continue;
            }

            result.Add(new LocalPreset
            {
                Id = string.IsNullOrWhiteSpace(entry.Id) ? NewId() : entry.Id,
                Name = name,
                Url = entry.Url.Trim(),
                Art = Bounded(entry.Art, MaxUrlLength),
                Codec = Bounded(entry.Codec, MaxCodecLength),
            });

            if (result.Count == MaxCount)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>Un champ facultatif : nettoyé, et écarté s'il est vide ou démesuré.</summary>
    private static string? Bounded(string? value, int max)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > max ? null : text;
    }

    public static string Serialize(IEnumerable<LocalPreset> presets)
        => JsonSerializer.Serialize(presets.ToList(), Options);

    /// <summary>Une adresse absolue en http ou https, avec un hôte.</summary>
    public static bool IsStreamUrl(string? url)
        => !string.IsNullOrWhiteSpace(url) &&
           Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
           uri.Host.Length > 0;

    /// <summary>Espaces en trop retirés, coupé à <see cref="MaxNameLength"/> caractères.</summary>
    public static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        var collapsed = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxNameLength ? collapsed : collapsed[..MaxNameLength].TrimEnd();
    }

    /// <summary>Vrai quand la liste a déjà une entrée pour cette adresse (casse ignorée).</summary>
    public static bool Contains(IEnumerable<LocalPreset> presets, string url)
        => presets.Any(p => string.Equals(p.Url.Trim(), url.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// La radio que l'enceinte joue, prête à être gardée : son nom, l'adresse
    /// d'origine du flux (jamais le relais de l'agent, qui ne mène qu'à une
    /// touche) et son logo. Null quand ce n'est pas une radio dont on connaît
    /// l'adresse : veille, Spotify, Bluetooth, morceau de bibliothèque…
    ///
    /// L'adresse se cherche comme pour l'enregistrement (historique de l'agent,
    /// touches, ce que l'enceinte rapporte). Un flux HLS, que l'enregistrement
    /// refuse, est accepté ici : l'agent le convertit pour l'enceinte.
    ///
    /// En dernier recours, <paramref name="launched"/> : ce que l'application a
    /// elle-même lancé (favori, Local, recherche). L'agent joue ces adresses par son
    /// relais, et l'enceinte ne rapporte alors que le titre et l'adresse du relais.
    /// Elle ne sert que si son titre est celui de la station qui joue.
    /// </summary>
    public static LocalPreset? FromPlaying(
        NowPlaying now,
        IReadOnlyList<RecentEntry> recent,
        IReadOnlyList<StrPreset> presets,
        string speakerHost,
        StrPlayedStream? launched = null)
    {
        var resolution = StreamUrlResolver.Resolve(now, recent, presets, speakerHost);
        var station = CleanName(resolution.Station);
        var last = recent.Count > 0 ? recent[0] : null;
        var lastIsThis = last is not null && !last.IsLibrary &&
                         string.Equals(CleanName(last.CardName), station, StringComparison.OrdinalIgnoreCase);

        var url = resolution.Url;

        if (url is null && resolution.Refusal == RecordingRefusal.Hls && lastIsThis && IsStreamUrl(last!.CardUrl))
        {
            url = last.CardUrl;
        }

        if ((url is null || !IsStreamUrl(url)) &&
            launched is not null && station.Length > 0 &&
            resolution.Refusal is not (RecordingRefusal.NotPlaying or RecordingRefusal.NotRadio) &&
            string.Equals(CleanName(launched.Title), station, StringComparison.OrdinalIgnoreCase) &&
            IsStreamUrl(launched.Url))
        {
            var launchedArt = string.IsNullOrWhiteSpace(launched.Icon) ? now.ArtUrl : launched.Icon;

            return new LocalPreset
            {
                Id = NewId(),
                Name = station,
                Url = launched.Url.Trim(),
                Art = string.IsNullOrWhiteSpace(launchedArt) ? null : launchedArt.Trim(),
                Codec = string.IsNullOrWhiteSpace(launched.Codec) ? null : launched.Codec,
            };
        }

        if (url is null || station.Length == 0 || !IsStreamUrl(url))
        {
            return null;
        }

        var art = lastIsThis && !string.IsNullOrWhiteSpace(last!.CardArt) ? last.CardArt : now.ArtUrl;

        return new LocalPreset
        {
            Id = NewId(),
            Name = station,
            Url = url.Trim(),
            Art = string.IsNullOrWhiteSpace(art) ? null : art.Trim(),
        };
    }
}

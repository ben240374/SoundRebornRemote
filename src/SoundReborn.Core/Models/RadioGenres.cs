namespace SoundReborn.Core.Models;

/// <summary>Une étiquette de genre de radio-browser, avec son nombre de stations.</summary>
public sealed class RadioTag
{
    [System.Text.Json.Serialization.JsonPropertyName("name")] public string Name { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("stationcount")] public int StationCount { get; set; }
}

/// <summary>Une pastille de genre telle que la recherche l'affiche.</summary>
/// <param name="Key">Étiquette envoyée à radio-browser ; null pour « Tous ».</param>
/// <param name="Count">Nombre de stations connu, 0 s'il ne l'est pas.</param>
/// <param name="IsBoost">Genre propre au pays choisi, placé en tête.</param>
public sealed record RadioGenreChip(string? Key, int Count, bool IsBoost);

/// <summary>
/// Les genres proposés en pastilles, repris de l'application PC de STR
/// (desktop-app/frontend/src/localization.js et views/music/taxonomy.js) :
///
///   – une rangée fixe de genres répandus partout (<see cref="Core"/>) ;
///   – deux genres propres au pays choisi, placés devant (<see cref="ByCountry"/>) ;
///   – « Plus de genres » : les étiquettes les plus fréquentes de l'annuaire qui ne
///     figurent pas déjà dans la rangée.
///
/// Les étiquettes de radio-browser sont libres et bruitées (« music », « fm »,
/// « germany »…) : <see cref="Canon"/> écarte le bruit et ramène les doublons
/// (« hip-hop », « hiphop ») à une seule forme.
/// </summary>
public static class RadioGenres
{
    /// <summary>Genres toujours proposés, dans l'ordre d'affichage.</summary>
    public static readonly IReadOnlyList<string> Core = new[]
    {
        "pop", "rock", "hits", "oldies",
        "jazz", "classical", "chillout",
        "dance", "electronic", "house",
        "hip hop", "latin",
        "80s", "90s",
        "news",
    };

    /// <summary>Deux genres mis en avant par pays (code ISO à deux lettres).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ByCountry = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["DE"] = new[] { "schlager", "deutschrap" },
        ["AT"] = new[] { "schlager", "volksmusik" },
        ["CH"] = new[] { "schlager", "volksmusik" },
        ["US"] = new[] { "country", "rnb" },
        ["CA"] = new[] { "country", "rnb" },
        ["AU"] = new[] { "country", "indie" },
        ["GB"] = new[] { "indie", "rnb" },
        ["IE"] = new[] { "folk", "indie" },
        ["FR"] = new[] { "chanson", "variété" },
        ["IT"] = new[] { "italo", "italian" },
        ["ES"] = new[] { "reggaeton", "salsa" },
        ["PT"] = new[] { "fado", "pimba" },
        ["MX"] = new[] { "reggaeton", "salsa" },
        ["AR"] = new[] { "reggaeton", "salsa" },
        ["BR"] = new[] { "sertanejo", "samba" },
        ["JP"] = new[] { "j-pop", "anime" },
        ["NL"] = new[] { "levenslied", "nederlandstalig" },
        ["BE"] = new[] { "chanson", "levenslied" },
        ["TR"] = new[] { "turkish", "halk" },
        ["PL"] = new[] { "disco polo", "polski" },
        ["GR"] = new[] { "greek", "laika" },
        ["SE"] = new[] { "svensk", "nordic" },
        ["NO"] = new[] { "norsk", "nordic" },
        ["DK"] = new[] { "dansk", "nordic" },
        ["FI"] = new[] { "suomi", "nordic" },
    };

    /// <summary>Étiquettes sans intérêt comme genre : bruit, langues, pays.</summary>
    private static readonly HashSet<string> Skip = new(StringComparer.Ordinal)
    {
        "music", "música", "musica", "musik", "sound", "sounds",
        "radio", "radios", "radyo", "station", "estación", "estacion",
        "fm", "am", "ukw",
        "live", "online", "internet", "internet radio", "streaming",
        "24/7", "24 7", "24h", "free", "best", "good", "good music", "best music",
        "mp3", "aac", "flac",
        "español", "spanish", "espanol", "deutsch", "german", "germany",
        "english", "englisch", "français", "francais", "french",
        "italiano", "italian", "português", "portuguese",
        "mexico", "méxico", "brazil", "brasil", "argentina", "chile", "colombia",
        "peru", "venezuela", "usa", "canada", "kanada",
        "norteamérica", "norteamerica", "sudamérica", "sudamerica",
        "latinoamérica", "latinoamerica", "latin america",
        "américa", "america", "europa", "europe", "asia", "africa", "oceania",
    };

    /// <summary>Doublons ramenés à une seule forme.</summary>
    private static readonly Dictionary<string, string> Alias = new(StringComparer.Ordinal)
    {
        ["pop music"] = "pop",
        ["pop musik"] = "pop",
        ["rock music"] = "rock",
        ["rock musik"] = "rock",
        ["classic"] = "classical",
        ["classic music"] = "classical",
        ["classical music"] = "classical",
        ["klassik"] = "classical",
        ["klassische musik"] = "classical",
        ["dance music"] = "dance",
        ["electronic music"] = "electronic",
        ["electronica"] = "electronic",
        ["80s80s"] = "80s",
        ["90s90s"] = "90s",
        ["2000s"] = "00s",
        ["2010s"] = "10s",
        ["top40"] = "top 40",
        ["r&b"] = "rnb",
        ["r and b"] = "rnb",
        ["rhythm and blues"] = "rnb",
        ["hip-hop"] = "hip hop",
        ["hiphop"] = "hip hop",
        ["heavy-metal"] = "heavy metal",
        ["news talk"] = "news",
        ["news radio"] = "news",
        ["nachrichten"] = "news",
        ["sport"] = "sports",
    };

    /// <summary>Nombre maximal de genres ajoutés par « Plus de genres ».</summary>
    public const int MaxTail = 24;

    /// <summary>Longueur maximale d'une étiquette retenue : au-delà, c'est une phrase.</summary>
    private const int MaxTagLength = 32;

    /// <summary>
    /// Forme canonique d'une étiquette, en minuscules ; vide pour une étiquette à
    /// ne pas proposer.
    /// </summary>
    public static string Canon(string? tag)
    {
        var key = (tag ?? "").Trim().ToLowerInvariant();

        if (key.Length == 0 || key.Length > MaxTagLength || Skip.Contains(key))
        {
            return "";
        }

        return Alias.TryGetValue(key, out var canon) ? canon : key;
    }

    /// <summary>
    /// Les pastilles à afficher : « Tous », les genres du pays, la rangée fixe,
    /// puis, si <paramref name="showMore"/>, la longue traîne de l'annuaire.
    ///
    /// Un genre choisi qui ne figurerait nulle part (genre d'un autre pays après un
    /// changement de pays, par exemple) est ajouté juste après « Tous » : la
    /// sélection en cours reste toujours visible, donc toujours annulable.
    /// </summary>
    /// <param name="tailCount">Nombre de genres que « Plus de genres » révélerait.</param>
    public static IReadOnlyList<RadioGenreChip> Chips(
        string? countryCode,
        IEnumerable<RadioTag>? liveTags,
        bool showMore,
        string? selected,
        out int tailCount)
    {
        // Comptes de l'annuaire, regroupés par forme canonique.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var tag in liveTags ?? Enumerable.Empty<RadioTag>())
        {
            var canon = Canon(tag?.Name);

            if (canon.Length > 0)
            {
                counts[canon] = (counts.TryGetValue(canon, out var n) ? n : 0) + Math.Max(0, tag!.StationCount);
            }
        }

        int CountOf(string key) => counts.TryGetValue(key, out var n) ? n : 0;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chips = new List<RadioGenreChip> { new(null, 0, false) };

        if (!string.IsNullOrWhiteSpace(countryCode) && ByCountry.TryGetValue(countryCode!, out var boost))
        {
            foreach (var key in boost)
            {
                if (seen.Add(key))
                {
                    chips.Add(new RadioGenreChip(key, CountOf(key), true));
                }
            }
        }

        foreach (var key in Core)
        {
            if (seen.Add(key))
            {
                chips.Add(new RadioGenreChip(key, CountOf(key), false));
            }
        }

        var tail = counts
            .Where(kv => kv.Value > 0 && !seen.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(MaxTail)
            .ToList();

        tailCount = tail.Count;

        if (showMore)
        {
            foreach (var (key, count) in tail)
            {
                seen.Add(key);
                chips.Add(new RadioGenreChip(key, count, false));
            }
        }

        var chosen = string.IsNullOrWhiteSpace(selected) ? null : selected!.Trim();

        if (chosen is not null && !seen.Contains(chosen))
        {
            chips.Insert(1, new RadioGenreChip(chosen, CountOf(chosen), false));
        }

        return chips;
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using SoundReborn.Core.Models;

namespace SoundReborn.Core;

/// <summary>
/// Client de radio-browser.info, l'annuaire public de webradios.
///
/// L'agent STR ne sert plus la recherche : depuis la v0.8 il ne compile plus le
/// client radio-browser et c'est l'application qui interroge le service. On fait
/// donc pareil ici, depuis le téléphone.
///
/// La logique suit celle de l'application PC de STR (radiobrowser/radiobrowser.go
/// et desktop-app/radio.go) :
///
///   – une recherche par nom ou par genre garde les stations que l'annuaire n'a pas
///     encore contrôlées : une station qu'on vient d'ajouter doit se trouver tout
///     de suite, pas des heures plus tard ;
///   – une recherche par nom en plusieurs mots cherche aussi ces mots dans les
///     genres (« rock belge »), et, si le nom ne donne rien, la même chose sans la
///     ponctuation (« Mi Soul » trouve « Mi-Soul ») ;
///   – une recherche par nom dont tous les résultats sont signalés hors service les
///     montre quand même, en le disant (<see cref="RadioSearchPage.Relaxed"/>).
///
/// Les miroirs sont codés en dur parce que la découverte officielle des serveurs
/// passe elle-même par un miroir : si celui-ci est à terre, la découverte l'est
/// aussi. On bascule sur le suivant à la première erreur.
/// </summary>
public sealed class RadioBrowserClient
{
    private static readonly string[] Mirrors =
    {
        "https://de1.api.radio-browser.info/json",
        "https://all.api.radio-browser.info/json",
    };

    /// <summary>
    /// radio-browser n'a pas de mécanisme de clé : un User-Agent descriptif est
    /// ce qui permet aux mainteneurs de voir d'où vient le trafic. Le service le
    /// demande explicitement, et répond mal à un agent vide.
    /// </summary>
    private const string UserAgent = "SoundRebornRemote/1.9.0 (+https://github.com/ben240374/SoundRebornRemote)";

    /// <summary>Budget d'un appel à l'annuaire de stations.</summary>
    public static TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Taille maximale d'une réponse lue. Une page de 200 stations pèse environ
    /// 200 Ko : au-delà de 4 Mo, ce n'est plus une réponse de l'annuaire.
    /// </summary>
    public const int MaxResponseBytes = 4 * 1024 * 1024;

    /// <summary>Plus grande page demandée à l'annuaire.</summary>
    public const int MaxPage = 200;

    /// <summary>Taille de la page élargie de la recherche sans ponctuation.</summary>
    private const int PunctuationPage = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    /// <summary>Index du miroir qui a répondu en dernier : on repart de celui-là.</summary>
    private int _mirror;

    public RadioBrowserClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    // ================================================================ Recherche

    /// <summary>
    /// Recherche par nom, genre, pays et langue. Sans nom ni genre, c'est la liste
    /// des stations les plus votées (la « Top liste »).
    /// </summary>
    public async Task<RadioSearchPage> SearchAsync(RadioSearchOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var name = Clean(options.Name);
        var tag = Clean(options.Tag);
        var limit = Math.Clamp(options.Limit, 1, MaxPage);
        var offset = Math.Max(0, options.Offset);

        // Recherche « ciblée » (nom ou genre) : le filtre des stations hors service
        // se fait ici, pour garder celles qui n'ont pas encore été contrôlées. Une
        // simple liste par popularité garde le filtre strict de l'annuaire : un
        // classement de milliers de stations n'a pas à remonter des inconnues.
        var targeted = name is not null || tag is not null;
        var filterHere = options.HideBroken && targeted;

        // Ce qui est écarté ici ne doit pas maigrir la page : on en demande plus.
        var fetchLimit = filterHere ? Math.Min(limit * 3, MaxPage) : limit;

        var raw = await FetchAsync(options, name, tag, null, fetchLimit, offset,
            serverFilters: options.HideBroken && !targeted, ct).ConfigureAwait(false);

        var kept = filterHere ? KeepReachableOrUnchecked(raw) : raw;
        var relaxed = false;

        if (filterHere && kept.Count == 0 && raw.Count > 0 && name is not null)
        {
            // Tout ce qui porte ce nom est signalé hors service. Le contrôle de
            // radio-browser se fait depuis l'étranger : mieux vaut montrer ces
            // stations, en le disant, que de laisser croire qu'elles n'existent pas.
            kept = raw;
            relaxed = true;
        }

        // Ordre d'affichage : le nom tapé d'abord, puis le même nom sans
        // ponctuation, puis les mots trouvés dans les genres. Dans chaque groupe,
        // l'ordre de l'annuaire (votes, écoutes…) est gardé ; pour l'ordre
        // alphabétique, il est refait ici sans les signes de tête (« *** Radio »).
        var ranked = kept.Select(s => (Station: s, Rank: 0)).ToList();

        if (offset == 0 && name is not null && tag is null)
        {
            var tokens = Tokenize(name);

            if (tokens.Count >= 2)
            {
                if (kept.Count == 0)
                {
                    var punctuated = await PunctuationFallbackAsync(options, name, tokens, ct).ConfigureAwait(false);
                    ranked.AddRange(punctuated.Select(s => (s, 1)));
                }

                var tagged = await TryFetchAsync(options, null, null, tokens, Math.Min(limit * 2, MaxPage), ct)
                    .ConfigureAwait(false);
                ranked.AddRange((options.HideBroken ? KeepReachableOrUnchecked(tagged) : tagged).Select(s => (s, 2)));
            }
        }

        var stations = Filter(ranked, options);

        return new RadioSearchPage
        {
            Stations = stations,
            Relaxed = relaxed,
            Fetched = raw.Count,
            NextOffset = offset + raw.Count,
            HasMore = raw.Count >= fetchLimit,
        };
    }

    /// <summary>
    /// Les stations dont l'adresse de flux est exactement celle-ci : une adresse
    /// collée dans la recherche retrouve ainsi son nom et son logo.
    /// </summary>
    public async Task<IReadOnlyList<RadioStation>> ByUrlAsync(string streamUrl, CancellationToken ct = default)
    {
        var url = (streamUrl ?? "").Trim();

        if (url.Length == 0)
        {
            return Array.Empty<RadioStation>();
        }

        var stations = await GetAsync<List<RadioStation>>("/stations/byurl?url=" + Uri.EscapeDataString(url), ct)
            .ConfigureAwait(false) ?? new List<RadioStation>();

        return Filter(stations.Select(s => (s, 0)).ToList(), new RadioSearchOptions { BoseCompatibleOnly = false });
    }

    /// <summary>Les genres les plus fréquents de l'annuaire, pour « Plus de genres ».</summary>
    public async Task<IReadOnlyList<RadioTag>> TagsAsync(int limit, CancellationToken ct = default)
    {
        var path = "/tags?order=stationcount&reverse=true&hidebroken=true&limit=" +
                   Math.Clamp(limit, 1, MaxPage).ToString(CultureInfo.InvariantCulture);

        var tags = await GetAsync<List<RadioTag>>(path, ct).ConfigureAwait(false);

        return (tags ?? new List<RadioTag>()).Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Name)).ToList();
    }

    /// <summary>
    /// Signale une écoute à radio-browser. Pure politesse : c'est ce compteur qui
    /// alimente le classement dont tout le monde profite. Les erreurs sont ignorées.
    /// </summary>
    public async Task ReportClickAsync(string stationUuid, CancellationToken ct = default)
    {
        // Un identifiant d'annuaire n'a que des chiffres, des lettres et des tirets :
        // rien d'autre ne part dans le chemin.
        if (string.IsNullOrWhiteSpace(stationUuid) || !stationUuid.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            return;
        }

        try
        {
            await GetAsync<JsonDocument>($"/url/{stationUuid}", ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Sans conséquence pour l'utilisateur.
        }
    }

    // ================================================================ Rouages

    /// <summary>Une requête /stations/search.</summary>
    /// <param name="serverFilters">Demander à l'annuaire de masquer les stations hors service.</param>
    private async Task<List<RadioStation>> FetchAsync(
        RadioSearchOptions options,
        string? name,
        string? tag,
        IReadOnlyList<string>? tagList,
        int limit,
        int offset,
        bool serverFilters,
        CancellationToken ct)
    {
        var order = RadioOrder.Normalize(options.Order);

        var query = new List<string>
        {
            "order=" + order,
            "limit=" + limit.ToString(CultureInfo.InvariantCulture),
        };

        // reverse=true : le plus haut d'abord. Pour le nom, cela donnerait Z → A.
        if (order != RadioOrder.Name)
        {
            query.Add("reverse=true");
        }

        if (offset > 0)
        {
            query.Add("offset=" + offset.ToString(CultureInfo.InvariantCulture));
        }

        if (serverFilters)
        {
            query.Add("hidebroken=true");
        }

        Append(query, "name", name);
        Append(query, "tag", tag);
        Append(query, "countrycode", Clean(options.CountryCode)?.ToUpperInvariant());
        Append(query, "language", Clean(options.Language)?.ToLowerInvariant());

        if (tagList is { Count: > 0 })
        {
            Append(query, "tagList", string.Join(",", tagList));
        }

        return await GetAsync<List<RadioStation>>("/stations/search?" + string.Join("&", query), ct)
            .ConfigureAwait(false) ?? new List<RadioStation>();
    }

    /// <summary>Comme <see cref="FetchAsync"/>, mais un échec donne une liste vide : sert aux recherches d'appoint.</summary>
    private async Task<List<RadioStation>> TryFetchAsync(
        RadioSearchOptions options, string? name, string? tag, IReadOnlyList<string>? tagList, int limit, CancellationToken ct)
    {
        try
        {
            return await FetchAsync(options, name, tag, tagList, limit, 0, serverFilters: false, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // La recherche principale a répondu : celle-ci n'est qu'un complément.
            return new List<RadioStation>();
        }
    }

    /// <summary>
    /// Le nom de la station porte une ponctuation que l'utilisateur n'a pas tapée :
    /// radio-browser compare le nom tel quel, si bien que « Mi Soul » ne trouve pas
    /// « Mi-Soul ». On demande le mot le plus long seul, puis on compare les noms
    /// sans ponctuation, des deux côtés, sur la requête entière (« misoul » est dans
    /// « Mi-Soul » mais pas dans « Missoula »).
    /// </summary>
    private async Task<List<RadioStation>> PunctuationFallbackAsync(
        RadioSearchOptions options, string name, IReadOnlyList<string> tokens, CancellationToken ct)
    {
        var longest = tokens.OrderByDescending(t => t.Length).First();

        // Une ou deux lettres figurent dans le nom de presque toutes les stations.
        if (longest.Length < 3)
        {
            return new List<RadioStation>();
        }

        var wanted = NormalizeName(name);

        if (wanted.Length == 0)
        {
            return new List<RadioStation>();
        }

        var wide = await TryFetchAsync(options, longest, null, null, PunctuationPage, ct).ConfigureAwait(false);
        var matches = wide.Where(s => NormalizeName(s.Name).Contains(wanted, StringComparison.Ordinal)).ToList();

        return options.HideBroken ? KeepReachableOrUnchecked(matches) : matches;
    }

    /// <summary>
    /// Écarte les stations contrôlées ET trouvées hors service ; garde celles qui
    /// répondent et celles que l'annuaire n'a pas encore contrôlées.
    /// </summary>
    public static List<RadioStation> KeepReachableOrUnchecked(IEnumerable<RadioStation> stations)
        => stations.Where(s => s is not null && !s.IsCheckedBroken).ToList();

    /// <summary>Minuscules, lettres et chiffres seulement : « Mi-Soul », « mi soul » et « MI.SOUL » se valent.</summary>
    public static string NormalizeName(string? name)
    {
        var builder = new StringBuilder((name ?? "").Length);

        foreach (var c in (name ?? "").ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Les mots d'une requête, en minuscules, sans virgule (séparateur de tagList).</summary>
    public static IReadOnlyList<string> Tokenize(string? query)
        => (query ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Replace(",", "", StringComparison.Ordinal).Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .ToList();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Append(List<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{key}={Uri.EscapeDataString(value!.Trim())}");
        }
    }

    /// <summary>
    /// Écarte les entrées sans flux et, sur demande, celles que l'enceinte ne sait
    /// pas lire ; ne garde qu'une entrée par flux.
    /// </summary>
    private static List<RadioStation> Filter(List<(RadioStation Station, int Rank)> ranked, RadioSearchOptions options)
    {
        var usable = ranked.Where(r => r.Station is not null && !string.IsNullOrWhiteSpace(r.Station.PlayableUrl));

        if (options.BoseCompatibleOnly)
        {
            usable = usable.Where(r => r.Station.IsBoseCompatible);
        }

        // radio-browser renvoie des doublons quand plusieurs entrées pointent le
        // même flux : on garde la plus populaire de chaque groupe, au meilleur rang.
        // Si elle n'a pas de logo, on lui prête celui d'une sœur : le même flux,
        // c'est la même station (Couleur 3 n'a de logo que sur sa seconde entrée).
        return usable
            .GroupBy(r => r.Station.PlayableUrl.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var best = g.OrderByDescending(r => r.Station.ClickCount).First().Station;
                BorrowLogo(best, g.Select(r => r.Station));
                return (Station: best, Rank: g.Min(r => r.Rank));
            })
            .OrderBy(r => r.Rank)
            .ThenBy(r => RadioOrder.Normalize(options.Order) == RadioOrder.Name ? SortableName(r.Station.Name) : "", NameComparer)
            .Select(r => r.Station)
            .ToList();
    }

    /// <summary>Comparaison des noms pour l'ordre alphabétique : sans casse ni accents.</summary>
    private static readonly StringComparer NameComparer =
        StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    /// <summary>Complète le logo et le site d'une station à partir de ses doublons ; jamais n'écrase.</summary>
    private static void BorrowLogo(RadioStation target, IEnumerable<RadioStation> siblings)
    {
        static bool IsHttps(string? url) => url?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true;

        if (string.IsNullOrWhiteSpace(target.Favicon))
        {
            var donor = siblings
                .Where(s => !string.IsNullOrWhiteSpace(s.Favicon))
                .OrderByDescending(s => IsHttps(s.Favicon))
                .FirstOrDefault();

            if (donor is not null)
            {
                target.Favicon = donor.Favicon;
            }
        }

        if (string.IsNullOrWhiteSpace(target.Homepage))
        {
            target.Homepage = siblings.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Homepage))?.Homepage ?? target.Homepage;
        }
    }

    /// <summary>Nom débarrassé des signes de tête (« *** », « .:: ») pour un tri alphabétique honnête.</summary>
    private static string SortableName(string? name)
    {
        var raw = (name ?? "").Trim();
        var start = 0;

        while (start < raw.Length && !char.IsLetterOrDigit(raw[start]))
        {
            start++;
        }

        return start < raw.Length ? raw[start..] : raw;
    }

    /// <summary>
    /// Interroge le miroir courant, puis les suivants en cas d'échec. Le miroir
    /// qui répond devient le point de départ des appels suivants.
    /// </summary>
    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        Exception? last = null;

        for (var attempt = 0; attempt < Mirrors.Length; attempt++)
        {
            var index = (_mirror + attempt) % Mirrors.Length;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, Mirrors[index] + path);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");

                // L'HttpClient partagé n'impose plus de plafond : l'annuaire a le
                // sien, un miroir lent ne doit pas bloquer la recherche.
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(RequestTimeout);

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    last = new SpeakerException($"radio-browser a répondu {(int)response.StatusCode}.");
                    continue;
                }

                var body = await ReadBoundedAsync(response.Content, cts.Token).ConfigureAwait(false);
                _mirror = index;

                return string.IsNullOrWhiteSpace(body) ? default : JsonSerializer.Deserialize<T>(body, JsonOptions);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new SpeakerException(
            "Aucun miroir radio-browser n'a répondu. Le téléphone a-t-il accès à Internet ?",
            null,
            last);
    }

    /// <summary>Lit le corps sans dépasser <see cref="MaxResponseBytes"/>.</summary>
    private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaxResponseBytes)
        {
            throw new InvalidDataException("Réponse de l'annuaire trop grosse.");
        }

        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaxResponseBytes)
            {
                throw new InvalidDataException("Réponse de l'annuaire trop grosse.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}

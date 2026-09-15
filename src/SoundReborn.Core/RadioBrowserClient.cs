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
    private const string UserAgent = "SoundRebornRemote/0.9.81 (+https://github.com/ben240374/SoundRebornRemote)";

    /// <summary>Budget d'un appel à l'annuaire de stations.</summary>
    public static TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    /// <summary>Index du miroir qui a répondu en dernier : on repart de celui-là.</summary>
    private int _mirror;

    public RadioBrowserClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <summary>Recherche par nom, pays, langue ou étiquette, triée par popularité.</summary>
    public async Task<IReadOnlyList<RadioStation>> SearchAsync(RadioSearchOptions options, CancellationToken ct = default)
    {
        var query = new List<string>
        {
            "order=clickcount",
            "reverse=true",
            $"limit={Math.Clamp(options.Limit, 1, 200)}",
        };

        if (options.HideBroken)
        {
            query.Add("hidebroken=true");
        }

        Append(query, "name", options.Name);
        Append(query, "countrycode", options.CountryCode);
        Append(query, "language", options.Language);
        Append(query, "tag", options.Tag);

        var stations = await GetAsync<List<RadioStation>>("/stations/search?" + string.Join("&", query), ct)
            .ConfigureAwait(false) ?? new List<RadioStation>();

        return Filter(stations, options);
    }

    /// <summary>Les stations les plus écoutées, pour proposer quelque chose avant toute saisie.</summary>
    public async Task<IReadOnlyList<RadioStation>> TopAsync(RadioSearchOptions options, CancellationToken ct = default)
    {
        var query = new List<string>
        {
            "order=clickcount",
            "reverse=true",
            "hidebroken=true",
            $"limit={Math.Clamp(options.Limit, 1, 200)}",
        };

        Append(query, "countrycode", options.CountryCode);
        Append(query, "language", options.Language);

        var stations = await GetAsync<List<RadioStation>>("/stations/search?" + string.Join("&", query), ct)
            .ConfigureAwait(false) ?? new List<RadioStation>();

        return Filter(stations, options);
    }

    /// <summary>
    /// Signale une écoute à radio-browser. Pure politesse : c'est ce compteur qui
    /// alimente le classement dont tout le monde profite. Les erreurs sont ignorées.
    /// </summary>
    public async Task ReportClickAsync(string stationUuid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stationUuid))
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

    private static void Append(List<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{key}={Uri.EscapeDataString(value!.Trim())}");
        }
    }

    private static IReadOnlyList<RadioStation> Filter(List<RadioStation> stations, RadioSearchOptions options)
    {
        IEnumerable<RadioStation> result = stations.Where(s => !string.IsNullOrWhiteSpace(s.PlayableUrl));

        if (options.BoseCompatibleOnly)
        {
            result = result.Where(s => s.IsBoseCompatible);
        }

        // radio-browser renvoie des doublons quand plusieurs entrées pointent le
        // même flux ; on garde la plus populaire de chaque groupe.
        return result
            .GroupBy(s => s.PlayableUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.ClickCount).First())
            .ToList();
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

                using var response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    last = new SpeakerException($"radio-browser a répondu {(int)response.StatusCode}.");
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
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
}

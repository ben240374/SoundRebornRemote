using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SoundReborn.Core.Internal;
using SoundReborn.Core.Models;

namespace SoundReborn.Core;

/// <summary>
/// Client de l'API locale de l'agent STR (SoundTouch Reborn), en JSON.
/// Port 8888 sur châssis sm2 ; 17008 sur les châssis BCO (SoundTouch Portable,
/// certaines ST20), où le pare-feu du chipset ne laisse entrer que ce port.
/// Aucune authentification : la joignabilité sur le LAN est la limite de confiance.
/// </summary>
public sealed class StrApiClient
{
    /// <summary>Ports testés dans l'ordre lors de la détection.</summary>
    public static readonly int[] CandidatePorts = { 17008, 8888 };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;

    public StrApiClient(string host, int port, HttpClient http)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Port = port;
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public string Host { get; }

    public int Port { get; }

    public string BaseUrl => $"http://{Host}:{Port}";

    /// <summary>Budget d'une commande ordinaire : sur le LAN, au-delà c'est perdu.</summary>
    public static TimeSpan NormalTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Budget des commandes qui réveillent l'enceinte — lecture, allumage, formation
    /// d'un groupe. Le réveil à lui seul consomme près de huit secondes, et couper
    /// trop tôt donne un délai dépassé sur une commande qui allait aboutir.
    /// </summary>
    public static TimeSpan WakeTimeout { get; set; } = TimeSpan.FromSeconds(25);

    private static CancellationTokenSource Budget(TimeSpan? timeout, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? NormalTimeout);
        return cts;
    }

    /// <summary>
    /// Cherche l'agent STR sur une enceinte. Renvoie le port qui a répondu et la
    /// version annoncée, ou null.
    ///
    /// On interroge /api/agent/version et on exige le mot « version » dans le corps :
    /// un code 200 ne prouve rien, n'importe quel serveur peut répondre sur un chemin
    /// qui n'est pas le sien. Au passage, la version est lue ici une bonne fois, ce
    /// qui évite un second aller-retour juste après.
    /// </summary>
    public static async Task<(int Port, string Version)?> ProbeAsync(
        string host, HttpClient http, TimeSpan timeout, CancellationToken ct = default)
    {
        foreach (var port in CandidatePorts)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            try
            {
                using var response = await http
                    .GetAsync($"http://{host}:{port}/api/agent/version", cts.Token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                if (body.Contains("version", StringComparison.OrdinalIgnoreCase))
                {
                    return (port, ReadVersion(body));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TaskCanceledException)
            {
                // Port fermé, pas d'agent, ou corps illisible : on essaie le suivant.
            }
        }

        return null;
    }

    /// <summary>Extrait « v0.9.79 » du corps de /api/agent/version, sans échouer.</summary>
    private static string ReadVersion(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            foreach (var name in new[] { "version", "Version" })
            {
                if (document.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString() ?? "";
                }
            }
        }
        catch (JsonException)
        {
            // Agent ancien qui répond en texte brut : la version reste vide.
        }

        return "";
    }

    // ---------------------------------------------------------------- Lecture

    /// <summary>
    /// GET /api/status. Attention : l'agent relaie le XML now_playing de l'enceinte,
    /// il ne le convertit pas en JSON. Le corps est donc du XML, avec un micro-cache
    /// côté agent qui évite de matraquer le firmware.
    /// </summary>
    public async Task<NowPlaying> GetStatusAsync(CancellationToken ct = default)
    {
        var text = await GetStringAsync("/api/status", ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text))
        {
            return NowPlaying.Standby;
        }

        var root = XDocument.Parse(text).Root;
        return root is null ? NowPlaying.Standby : FirmwareXml.ParseNowPlaying(root);
    }

    public Task<StrVolume?> GetVolumeAsync(CancellationToken ct = default)
        => GetJsonAsync<StrVolume>("/api/box/volume", ct);

    /// <summary>Version de l'agent installé sur l'enceinte, pour l'afficher à côté de son nom.</summary>
    public Task<AgentInfo?> GetAgentInfoAsync(CancellationToken ct = default)
        => GetJsonAsync<AgentInfo>("/api/agent/version", ct);

    public async Task<IReadOnlyList<StrPreset>> GetPresetsAsync(CancellationToken ct = default)
    {
        var text = await GetStringAsync("/api/presets", ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<StrPreset>();
        }

        // Le magasin accepte deux formes : un tableau nu, ou {"presets":[...]}.
        var trimmed = text.TrimStart();

        var list = trimmed.StartsWith('[')
            ? JsonSerializer.Deserialize<List<StrPreset>>(text, JsonOptions)
            : JsonSerializer.Deserialize<StrPresetEnvelope>(text, JsonOptions)?.Presets;

        return (list ?? new List<StrPreset>()).OrderBy(p => p.Slot).ToList();
    }

    public async Task<IReadOnlyList<RecentEntry>> GetRecentAsync(CancellationToken ct = default)
        => await GetJsonAsync<List<RecentEntry>>("/api/recent", ct).ConfigureAwait(false)
           ?? (IReadOnlyList<RecentEntry>)Array.Empty<RecentEntry>();

    public Task<StrZoneVolume?> GetZoneVolumeAsync(CancellationToken ct = default)
        => GetJsonAsync<StrZoneVolume>("/api/box/zone/volume", ct);

    /// <summary>
    /// Lit le drapeau « permanent » de la zone courante. L'agent stocke le document
    /// tel qu'il le reçoit : reformer un groupe sans ce drapeau le transforme en
    /// groupe ordinaire, qui cesse de se reformer à la lecture et disparaît à la
    /// première dissolution. Renvoie false si la zone n'existe pas ou ne le dit pas.
    /// </summary>
    public async Task<bool> GetZonePermanentAsync(CancellationToken ct = default)
    {
        try
        {
            using var document = await GetZoneRawAsync(ct).ConfigureAwait(false);

            if (document is null)
            {
                return false;
            }

            foreach (var name in new[] { "permanent", "Permanent", "sticky" })
            {
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty(name, out var value) &&
                    (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
                {
                    return value.GetBoolean();
                }
            }
        }
        catch (Exception)
        {
            // Pas de zone, agent muet, JSON inattendu : on ne prétend rien.
        }

        return false;
    }

    /// <summary>GET /api/box/zone, renvoyé brut (la forme varie selon le châssis).</summary>
    public Task<JsonDocument?> GetZoneRawAsync(CancellationToken ct = default)
        => GetJsonDocumentAsync("/api/box/zone", ct);

    // ---------------------------------------------------------------- Transport

    public Task PauseAsync(CancellationToken ct = default) => PostAsync("/api/pause", null, ct);

    public Task ResumeAsync(CancellationToken ct = default) => PostAsync("/api/resume", null, ct);

    public Task StopAsync(CancellationToken ct = default) => PostAsync("/api/stop", null, ct);

    public Task NextAsync(CancellationToken ct = default) => PostAsync("/api/next", null, ct);

    public Task PreviousAsync(CancellationToken ct = default) => PostAsync("/api/prev", null, ct);

    /// <summary>Lance la présélection STR (magasin de l'agent), 1 à 6.</summary>
    public Task PlaySlotAsync(int slot, CancellationToken ct = default)
        => PostAsync($"/api/play/{slot}", null, ct, WakeTimeout);

    /// <summary>Mime un appui sur la touche matérielle de présélection, 1 à 6.</summary>
    public Task RecallBoxPresetAsync(int slot, CancellationToken ct = default)
        => PostJsonAsync("/api/box/presets/recall", new { slot }, ct);

    /// <summary>
    /// Écrit une présélection dans le magasin de l'agent (PUT /api/presets/{slot}).
    /// L'agent enregistre le flux, le nom et le logo, puis enrôle la touche
    /// matérielle correspondante sur l'enceinte.
    /// </summary>
    public Task SavePresetAsync(StrPreset preset, CancellationToken ct = default)
    {
        if (preset.Slot is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(preset), preset.Slot, "Présélection 1 à 6 uniquement");
        }

        return PutJsonAsync($"/api/presets/{preset.Slot}", preset, ct);
    }

    /// <summary>Vide une présélection du magasin de l'agent.</summary>
    public async Task DeletePresetAsync(int slot, CancellationToken ct = default)
    {
        using var cts = Budget(null, ct);
        using var response = await _http.DeleteAsync($"{BaseUrl}/api/presets/{slot}", cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"/api/presets/{slot}", cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Joue n'importe quel flux. L'agent ajoute les métadonnées DIDL et bascule
    /// une URL HTTPS vers HTTP (le moteur UPnP de l'enceinte refuse TLS).
    /// </summary>
    /// <summary>
    /// Joue une URL. <paramref name="codec"/> et non « mime » : côté agent, mime
    /// signifie « fichier de bibliothèque locale, passe l'URL telle quelle à
    /// l'enceinte », ce qui désactive le relais de flux et le chemin des stations
    /// natives. Le budget est long : c'est une commande qui réveille l'enceinte.
    /// </summary>
    public Task PlayUrlAsync(string url, string? title = null, string? codec = null, string? icon = null, CancellationToken ct = default)
        => PostJsonAsync("/api/play", new StrPlayUrlRequest { Url = url, Title = title, Codec = codec, Icon = icon }, ct, WakeTimeout);

    // ---------------------------------------------------------------- Réglages

    public Task SetVolumeAsync(int value, CancellationToken ct = default)
        => PutJsonAsync("/api/box/volume", new { value = Math.Clamp(value, 0, 100) }, ct);

    public Task SetBassAsync(int value, CancellationToken ct = default)
        => PutJsonAsync("/api/box/bass", new { value }, ct);

    public Task SetPowerAsync(bool on, CancellationToken ct = default)
        => PostJsonAsync("/api/box/power", new { on }, ct, on ? WakeTimeout : null);

    /// <summary>Source physique : "AUX", "BLUETOOTH" ou "STANDBY".</summary>
    public Task SetSourceAsync(string source, CancellationToken ct = default)
        => PutJsonAsync("/api/box/source", new { source }, ct);

    /// <summary>Volume de tout le groupe, ou d'un seul membre si son IP est fournie.</summary>
    public Task SetZoneVolumeAsync(int value, string? memberIp = null, CancellationToken ct = default)
        => PostJsonAsync("/api/box/zone/volume",
            memberIp is null ? new Dictionary<string, object> { ["value"] = value }
                             : new Dictionary<string, object> { ["value"] = value, ["ip"] = memberIp },
            ct);

    public Task FormZoneAsync(StrZoneFormRequest request, CancellationToken ct = default)
        => PostJsonAsync("/api/box/zone", request, ct, WakeTimeout);

    public async Task DissolveZoneAsync(CancellationToken ct = default)
    {
        using var cts = Budget(null, ct);
        using var response = await _http.DeleteAsync(BaseUrl + "/api/box/zone", cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "/api/box/zone", cts.Token).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Plomberie

    private async Task<string> GetStringAsync(string path, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var cts = Budget(timeout, ct);
        using var response = await _http.GetAsync(BaseUrl + path, cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, path, cts.Token).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
    }

    private async Task<T?> GetJsonAsync<T>(string path, CancellationToken ct)
    {
        var text = await GetStringAsync(path, ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, JsonOptions);
    }

    private async Task<JsonDocument?> GetJsonDocumentAsync(string path, CancellationToken ct)
    {
        var text = await GetStringAsync(path, ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text);
    }

    private async Task PostAsync(string path, HttpContent? content, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var cts = Budget(timeout, ct);
        using var response = await _http.PostAsync(BaseUrl + path, content ?? new StringContent(""), cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, path, cts.Token).ConfigureAwait(false);
    }

    private async Task PostJsonAsync<T>(string path, T body, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        await PostAsync(path, content, ct, timeout).ConfigureAwait(false);
    }

    private async Task PutJsonAsync<T>(string path, T body, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var cts = Budget(timeout, ct);
        using var response = await _http.PutAsync(BaseUrl + path, content, cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, path, cts.Token).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string path, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var hint = response.StatusCode == HttpStatusCode.NotFound
            ? " (l'agent STR de cette enceinte ne connaît pas ce point de terminaison — version trop ancienne ?)"
            : "";

        throw new SpeakerException($"{path} a répondu {(int)response.StatusCode}{hint}.", body);
    }
}

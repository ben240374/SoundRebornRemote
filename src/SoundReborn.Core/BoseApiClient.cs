using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using SoundReborn.Core.Internal;
using SoundReborn.Core.Models;

namespace SoundReborn.Core;

/// <summary>
/// Client de l'API REST native de l'enceinte, port 8090, corps en XML.
/// Elle reste disponible après l'arrêt du cloud Bose ; seuls les points de
/// terminaison qui validaient une clé côté serveur (/speaker) sont morts.
/// </summary>
public sealed class BoseApiClient
{
    public const int DefaultPort = 8090;

    private readonly HttpClient _http;

    public BoseApiClient(string host, HttpClient http, int port = DefaultPort)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Adresse de l'enceinte requise", nameof(host));
        }

        Host = host;
        Port = port;
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public string Host { get; }

    public int Port { get; }

    public string BaseUrl => $"http://{Host}:{Port}";

    // ---------------------------------------------------------------- Lecture

    public async Task<DeviceInfo> GetInfoAsync(CancellationToken ct = default)
        => FirmwareXml.ParseInfo(await GetXmlAsync("/info", ct).ConfigureAwait(false));

    public async Task<NowPlaying> GetNowPlayingAsync(CancellationToken ct = default)
        => FirmwareXml.ParseNowPlaying(await GetXmlAsync("/now_playing", ct).ConfigureAwait(false));

    public async Task<VolumeState> GetVolumeAsync(CancellationToken ct = default)
        => FirmwareXml.ParseVolume(await GetXmlAsync("/volume", ct).ConfigureAwait(false));

    public async Task<BassState> GetBassAsync(CancellationToken ct = default)
        => FirmwareXml.ParseBass(await GetXmlAsync("/bass", ct).ConfigureAwait(false));

    public async Task<BassCapabilities> GetBassCapabilitiesAsync(CancellationToken ct = default)
    {
        try
        {
            return FirmwareXml.ParseBassCapabilities(await GetXmlAsync("/bassCapabilities", ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException)
        {
            // Certains modèles ne publient pas ce point de terminaison.
            return BassCapabilities.Unknown;
        }
    }

    public async Task<IReadOnlyList<PresetInfo>> GetPresetsAsync(CancellationToken ct = default)
        => FirmwareXml.ParsePresets(await GetXmlAsync("/presets", ct).ConfigureAwait(false));

    public async Task<IReadOnlyList<SourceItem>> GetSourcesAsync(CancellationToken ct = default)
        => FirmwareXml.ParseSources(await GetXmlAsync("/sources", ct).ConfigureAwait(false));

    public async Task<ZoneState> GetZoneAsync(CancellationToken ct = default)
        => FirmwareXml.ParseZone(await GetXmlAsync("/getZone", ct).ConfigureAwait(false));

    public async Task<string> GetNameAsync(CancellationToken ct = default)
        => (await GetXmlAsync("/name", ct).ConfigureAwait(false)).Value.Trim();

    /// <summary>Liste les chemins que ce modèle accepte réellement (utile pour griser des commandes).</summary>
    public async Task<IReadOnlyList<string>> GetSupportedUrlsAsync(CancellationToken ct = default)
    {
        var root = await GetXmlAsync("/supportedURLs", ct).ConfigureAwait(false);

        return root.Elements("URL")
                   .Select(u => u.Attribute("location")?.Value)
                   .Where(v => !string.IsNullOrWhiteSpace(v))
                   .Select(v => v!)
                   .ToList();
    }

    // ---------------------------------------------------------------- Écriture

    /// <summary>Volume absolu, 0 à 100.</summary>
    public Task SetVolumeAsync(int volume, CancellationToken ct = default)
    {
        volume = Math.Clamp(volume, 0, 100);
        return PostXmlAsync("/volume", $"<volume>{volume.ToString(CultureInfo.InvariantCulture)}</volume>", ct);
    }

    /// <summary>Niveau de graves. La plage dépend du modèle, voir GetBassCapabilitiesAsync.</summary>
    public Task SetBassAsync(int bass, CancellationToken ct = default)
        => PostXmlAsync("/bass", $"<bass>{bass.ToString(CultureInfo.InvariantCulture)}</bass>", ct);

    public Task SetNameAsync(string name, CancellationToken ct = default)
        => PostXmlAsync("/name", $"<name>{Xml.Escape(name)}</name>", ct);

    /// <summary>
    /// Appui complet sur une touche : le firmware attend un « press » puis un « release ».
    /// Envoyer seulement le press laisse la touche enfoncée côté enceinte.
    /// </summary>
    public async Task PressKeyAsync(RemoteKey key, CancellationToken ct = default)
    {
        var wire = key.ToWireName();
        await PostXmlAsync("/key", $"<key state=\"press\" sender=\"Gabbo\">{wire}</key>", ct).ConfigureAwait(false);
        await PostXmlAsync("/key", $"<key state=\"release\" sender=\"Gabbo\">{wire}</key>", ct).ConfigureAwait(false);
    }

    /// <summary>Rappelle une présélection matérielle (1 à 6).</summary>
    public Task RecallPresetAsync(int slot, CancellationToken ct = default)
        => PressKeyAsync(RemoteKeyExtensions.PresetKey(slot), ct);

    /// <summary>Bascule vers une source (Bluetooth, AUX, Spotify, ...).</summary>
    public Task SelectAsync(ContentItem item, CancellationToken ct = default)
        => PostXmlAsync("/select", FirmwareXml.BuildSelectBody(item), ct);

    /// <summary>Crée ou remplace une zone multi-pièces pilotée par cette enceinte.</summary>
    public Task SetZoneAsync(string masterDeviceId, string masterIp, IEnumerable<ZoneMember> slaves, CancellationToken ct = default)
        => PostXmlAsync("/setZone", FirmwareXml.BuildZoneBody(masterDeviceId, masterIp, slaves), ct);

    public Task AddZoneSlaveAsync(string masterDeviceId, string masterIp, IEnumerable<ZoneMember> slaves, CancellationToken ct = default)
        => PostXmlAsync("/addZoneSlave", FirmwareXml.BuildZoneBody(masterDeviceId, masterIp, slaves), ct);

    public Task RemoveZoneSlaveAsync(string masterDeviceId, string masterIp, IEnumerable<ZoneMember> slaves, CancellationToken ct = default)
        => PostXmlAsync("/removeZoneSlave", FirmwareXml.BuildZoneBody(masterDeviceId, masterIp, slaves), ct);

    /// <summary>Dissout la zone en la réduisant au seul maître.</summary>
    public Task DissolveZoneAsync(string masterDeviceId, string masterIp, CancellationToken ct = default)
        => SetZoneAsync(masterDeviceId, masterIp, Array.Empty<ZoneMember>(), ct);

    // ---------------------------------------------------------------- Transport

    /// <summary>
    /// Budget d'un appel au firmware. L'HttpClient partagé n'en impose plus :
    /// c'est ici que la limite est posée pour tout ce qui passe par le port 8090.
    /// </summary>
    public static TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(8);

    private static CancellationTokenSource Budget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallTimeout);
        return cts;
    }

    private async Task<XElement> GetXmlAsync(string path, CancellationToken ct)
    {
        using var cts = Budget(ct);
        using var response = await _http.GetAsync(BaseUrl + path, cts.Token).ConfigureAwait(false);
        return await ReadXmlAsync(response, path, cts.Token).ConfigureAwait(false);
    }

    private async Task<XElement> PostXmlAsync(string path, string body, CancellationToken ct)
    {
        using var cts = Budget(ct);
        using var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };

        using var response = await _http.PostAsync(BaseUrl + path, content, cts.Token).ConfigureAwait(false);
        return await ReadXmlAsync(response, path, cts.Token).ConfigureAwait(false);
    }

    private static async Task<XElement> ReadXmlAsync(HttpResponseMessage response, string path, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new SpeakerException($"{path} a répondu {(int)response.StatusCode} ({response.StatusCode}).", text);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            // Réponse vide légitime : enceinte non appairée sur /getGroup, par exemple.
            return new XElement("empty");
        }

        var root = XDocument.Parse(text).Root ?? new XElement("empty");

        // Le firmware signale ses refus dans le corps, avec un code HTTP 200.
        if (root.Name.LocalName.Equals("errors", StringComparison.OrdinalIgnoreCase) ||
            root.Name.LocalName.Equals("Error", StringComparison.OrdinalIgnoreCase))
        {
            var message = root.Descendants()
                              .Select(e => e.Value)
                              .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "erreur non détaillée";

            throw new SpeakerException($"{path} refusé par l'enceinte : {message.Trim()}", text);
        }

        return root;
    }
}

/// <summary>Erreur renvoyée par l'enceinte ou l'agent, avec le corps brut pour le diagnostic.</summary>
public sealed class SpeakerException : Exception
{
    public SpeakerException(string message, string? responseBody = null, Exception? inner = null)
        : base(message, inner)
    {
        ResponseBody = responseBody;
    }

    public string? ResponseBody { get; }
}

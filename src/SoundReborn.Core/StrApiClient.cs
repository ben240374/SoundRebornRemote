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

    /// <summary>
    /// GET /api/presets tel quel : chaque touche avec TOUS ses champs (compte et
    /// playlist d'une touche Spotify, dossier de bibliothèque…), pour une sauvegarde
    /// qu'on renverra sans rien perdre. Les deux formes du magasin sont acceptées.
    /// </summary>
    public async Task<System.Text.Json.Nodes.JsonArray> GetPresetsRawAsync(CancellationToken ct = default)
    {
        var text = await GetStringAsync("/api/presets", ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new System.Text.Json.Nodes.JsonArray();
        }

        var node = System.Text.Json.Nodes.JsonNode.Parse(text);

        return node switch
        {
            System.Text.Json.Nodes.JsonArray array => array,
            System.Text.Json.Nodes.JsonObject { } root when root["presets"] is System.Text.Json.Nodes.JsonArray inner
                => (System.Text.Json.Nodes.JsonArray)inner.DeepClone(),
            _ => throw new JsonException("Le magasin des touches n'est ni un tableau ni {\"presets\":[...]}."),
        };
    }

    /// <summary>
    /// PUT /api/presets — écrit un jeu de touches d'un coup (12 au plus). Les touches
    /// que le jeu ne nomme pas restent comme elles sont. C'est le chemin d'une
    /// restauration : l'agent ne refuse pas une station déjà sur une autre touche.
    /// </summary>
    public Task PutPresetsAsync(System.Text.Json.Nodes.JsonArray presets, CancellationToken ct = default)
        => PutRawAsync("/api/presets", presets.ToJsonString(), ct);

    public async Task<IReadOnlyList<RecentEntry>> GetRecentAsync(CancellationToken ct = default)
        => await GetJsonAsync<List<RecentEntry>>("/api/recent", ct).ConfigureAwait(false)
           ?? (IReadOnlyList<RecentEntry>)Array.Empty<RecentEntry>();

    public Task<StrZoneVolume?> GetZoneVolumeAsync(CancellationToken ct = default)
        => GetJsonAsync<StrZoneVolume>("/api/box/zone/volume", ct);

    /// <summary>
    /// Lit de la zone courante ce qu'il faut savoir avant de la republier.
    ///
    /// L'agent stocke le document tel qu'il le reçoit : republier sans « permanent »
    /// transforme un groupe durable en groupe ordinaire, qui cesse de se reformer à
    /// la lecture et disparaît à la première dissolution. Idem pour « stereo » et le
    /// nom de la paire.
    ///
    /// Renvoie <c>null</c> quand la lecture a échoué — agent muet, appel expiré,
    /// JSON inattendu. C'est volontairement distinct d'une zone sans drapeau : un
    /// appelant qui prendrait l'échec pour un « permanent : false » rétrograderait
    /// le groupe de l'utilisateur sur un simple hoquet réseau. Il doit renoncer.
    /// </summary>
    public async Task<StrZoneFacts?> GetZoneFactsAsync(CancellationToken ct = default)
    {
        try
        {
            using var document = await GetZoneRawAsync(ct).ConfigureAwait(false);

            if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
            {
                // Pas de zone du tout : il n'y a rien à préserver, et ce n'est pas
                // un échec de lecture.
                return new StrZoneFacts();
            }

            var root = document.RootElement;

            return new StrZoneFacts
            {
                Permanent = ReadFlag(root, "permanent", "Permanent", "sticky") ?? false,
                Stereo = ReadStereoFlag(root),
                StereoName = ReadStereoName(root),
                Remembered = ReadRemembered(root),
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool? ReadFlag(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) &&
                (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                return value.GetBoolean();
            }
        }

        return null;
    }

    /// <summary>
    /// « stereo » est tantôt un booléen, tantôt l'objet qui porte le nom de la
    /// paire — la documentation de l'agent décrit la seconde forme, des versions
    /// plus anciennes renvoient la première.
    /// </summary>
    private static bool ReadStereoFlag(JsonElement root)
    {
        if (!root.TryGetProperty("stereo", out var stereo))
        {
            return false;
        }

        return stereo.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => true,
            _ => false,
        };
    }

    private static string? ReadStereoName(JsonElement root)
    {
        if (root.TryGetProperty("stereo", out var stereo) &&
            stereo.ValueKind == JsonValueKind.Object &&
            stereo.TryGetProperty("name", out var name) &&
            name.ValueKind == JsonValueKind.String)
        {
            return name.GetString();
        }

        if (root.TryGetProperty("stereoName", out var flat) && flat.ValueKind == JsonValueKind.String)
        {
            return flat.GetString();
        }

        return null;
    }

    /// <summary>
    /// « remembered » : les suiveuses du groupe enregistré, que l'agent ne donne
    /// que lorsqu'aucun groupe n'est vivant. Une entrée sans adresse est ignorée.
    /// </summary>
    private static IReadOnlyList<StrRememberedMember> ReadRemembered(JsonElement root)
    {
        if (!root.TryGetProperty("remembered", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<StrRememberedMember>();
        }

        var members = new List<StrRememberedMember>();

        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var ip = ReadText(item, "ip");

            if (string.IsNullOrWhiteSpace(ip))
            {
                continue;
            }

            members.Add(new StrRememberedMember
            {
                Ip = ip!.Trim(),
                DeviceId = ReadText(item, "deviceID"),
                Name = ReadText(item, "name"),
            });
        }

        return members;
    }

    private static string? ReadText(JsonElement owner, string name)
        => owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>GET /api/box/zone, renvoyé brut (la forme varie selon le châssis).</summary>
    public Task<JsonDocument?> GetZoneRawAsync(CancellationToken ct = default)
        => GetJsonDocumentAsync("/api/box/zone", ct);

    // ---------------------------------------------------------------- Transport

    public Task PauseAsync(CancellationToken ct = default) => PostAsync("/api/pause", null, ct);

    public Task ResumeAsync(CancellationToken ct = default) => PostAsync("/api/resume", null, ct);

    public Task StopAsync(CancellationToken ct = default) => PostAsync("/api/stop", null, ct);

    public Task NextAsync(CancellationToken ct = default) => PostAsync("/api/next", null, ct);

    public Task PreviousAsync(CancellationToken ct = default) => PostAsync("/api/prev", null, ct);

    /// <summary>
    /// Lance la présélection STR (magasin de l'agent), 1 à 12. Les emplacements 7 à
    /// 12 sont la seconde rangée : ils n'existent que dans l'agent, aucune touche du
    /// firmware ne les atteint. Un agent antérieur à la seconde rangée répond 400.
    /// </summary>
    public Task PlaySlotAsync(int slot, CancellationToken ct = default)
        => PostAsync($"/api/play/{slot}", null, ct, WakeTimeout);

    /// <summary>Mime un appui sur la touche matérielle de présélection, 1 à 6.</summary>
    public Task RecallBoxPresetAsync(int slot, CancellationToken ct = default)
        => PostJsonAsync("/api/box/presets/recall", new { slot }, ct);

    /// <summary>
    /// Écrit une présélection dans le magasin de l'agent (PUT /api/presets/{slot}).
    /// L'agent enregistre le flux, le nom et le logo, puis enrôle la touche
    /// matérielle correspondante sur l'enceinte (emplacements 1 à 6 ; 7 à 12
    /// restent dans le seul magasin de l'agent).
    ///
    /// Une station ne vit que sur une touche : si elle est déjà ailleurs, l'agent
    /// refuse par un 409 « already-on-slot » plutôt que de vider l'autre touche.
    /// </summary>
    public Task SavePresetAsync(StrPreset preset, CancellationToken ct = default)
    {
        if (preset.Slot is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(preset), preset.Slot, "Présélection 1 à 12 uniquement");
        }

        return PutJsonAsync($"/api/presets/{preset.Slot}", preset, ct);
    }

    /// <summary>Longueur maximale d'un nom de touche : l'agent coupe au-delà.</summary>
    public const int MaxPresetNameLength = 64;

    /// <summary>
    /// Renomme une touche du magasin de l'agent (PATCH /api/presets/{slot},
    /// agent 1.8.0). Seul le nom change : la station, son image et la playlist
    /// d'une touche Spotify restent. Le nom est nettoyé et coupé à 64 caractères,
    /// comme l'agent le ferait ; un nom vide est refusé avant tout envoi. L'agent
    /// répond 404 quand la touche est vide.
    /// </summary>
    public Task RenamePresetAsync(int slot, string name, CancellationToken ct = default)
    {
        if (slot is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Présélection 1 à 12 uniquement");
        }

        var clean = (name ?? "").Trim();

        if (clean.Length == 0)
        {
            throw new ArgumentException("Nom de touche vide", nameof(name));
        }

        if (clean.Length > MaxPresetNameLength)
        {
            clean = clean[..MaxPresetNameLength];
        }

        return PatchJsonAsync($"/api/presets/{slot}", new { name = clean }, ct);
    }

    /// <summary>
    /// Le titre en direct du flux que l'agent relaie (GET /api/stream/title).
    /// Pendant une touche Spotify, l'agent 1.8.2 y ajoute le morceau, l'artiste et
    /// la pochette. Un agent plus ancien répond 404 : une SpeakerException.
    /// </summary>
    public async Task<StrStreamTitle> GetStreamTitleAsync(CancellationToken ct = default)
        => StrStreamTitle.Parse(await GetStringAsync("/api/stream/title", ct).ConfigureAwait(false));

    /// <summary>Vide une présélection du magasin de l'agent.</summary>
    public async Task DeletePresetAsync(int slot, CancellationToken ct = default)
    {
        using var cts = Budget(null, ct);
        using var response = await _http.DeleteAsync($"{BaseUrl}/api/presets/{slot}", cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"/api/presets/{slot}", cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Garde ce que l'enceinte joue en ce moment sur une touche, 1 à 12
    /// (POST /api/box/save-playing) : la station, le dossier ou la piste de
    /// bibliothèque, ou une copie de la touche qui joue.
    ///
    /// 422 quand rien de conservable ne joue (veille, AUX, Bluetooth, Spotify) ;
    /// 409 « already-on-slot » quand c'est déjà sur une autre touche.
    /// </summary>
    public async Task<StrSavedPlaying> SavePlayingAsync(int slot, CancellationToken ct = default)
    {
        if (slot is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Présélection 1 à 12 uniquement");
        }

        var body = await PostJsonReadAsync("/api/box/save-playing", new { slot }, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<StrSavedPlaying>(body, JsonOptions);

                if (saved is not null)
                {
                    return saved;
                }
            }
            catch (JsonException)
            {
                // Corps inattendu : la commande a tout de même été acceptée.
            }
        }

        return new StrSavedPlaying { Slot = slot };
    }

    /// <summary>
    /// Relance une entrée de l'historique. La requête dépend de ce qu'est l'entrée :
    ///
    ///   – un album ou un dossier de bibliothèque se rejoue par sa clé, sur
    ///     /api/queue/replay-card. Son adresse ne désigne que la première piste ;
    ///   – une piste de bibliothèque part sur /api/play AVEC « mime » et la durée.
    ///     Sans « mime », un agent jusqu'à v1.6.0 la prenait pour une station et la
    ///     reclassait en radio dans l'historique ;
    ///   – une station part sur /api/play avec son nom et son logo, sans « mime ».
    ///
    /// Un 200 n'est pas un succès : un dossier dont le serveur de médias est
    /// introuvable répond 200 avec « offline ». D'où le résultat renvoyé.
    /// </summary>
    public async Task<StrReplayOutcome> ReplayAsync(RecentEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsQueueCard)
        {
            var card = new StrReplayCardRequest { Key = entry.CardKey!, Name = entry.CardName, Art = entry.CardArt };

            return ReadReplay(
                await PostJsonReadAsync("/api/queue/replay-card", card, ct, WakeTimeout).ConfigureAwait(false));
        }

        var request = new StrPlayUrlRequest
        {
            Url = entry.CardUrl ?? "",
            Title = entry.CardName,
            Icon = entry.CardArt,
        };

        if (entry.IsLibrary)
        {
            request.Mime = string.IsNullOrWhiteSpace(entry.Mime) ? null : entry.Mime;
            request.DurationSec = entry.DurationSeconds;
        }

        return ReadReplay(await PostJsonReadAsync("/api/play", request, ct, WakeTimeout).ConfigureAwait(false));
    }

    private static StrReplayOutcome ReadReplay(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new StrReplayOutcome();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new StrReplayOutcome();
            }

            return new StrReplayOutcome
            {
                Offline = ReadFlag(root, "offline") ?? false,
                Server = root.TryGetProperty("server", out var server) &&
                         server.ValueKind == JsonValueKind.String
                    ? server.GetString()
                    : null,
            };
        }
        catch (JsonException)
        {
            return new StrReplayOutcome();
        }
    }

    /// <summary>
    /// Joue une URL. <paramref name="codec"/> et non « mime » : côté agent, mime
    /// signifie « fichier de bibliothèque locale, passe l'URL telle quelle à
    /// l'enceinte », ce qui désactive le relais de flux et le chemin des stations
    /// natives. Le budget est long : c'est une commande qui réveille l'enceinte.
    /// </summary>
    public async Task PlayUrlAsync(string url, string? title = null, string? codec = null, string? icon = null, CancellationToken ct = default)
    {
        await PostJsonAsync("/api/play", new StrPlayUrlRequest { Url = url, Title = title, Codec = codec, Icon = icon }, ct, WakeTimeout)
            .ConfigureAwait(false);

        LastPlayed = new StrPlayedStream(url, title, codec, icon);
    }

    /// <summary>
    /// La dernière adresse que cette application a fait jouer sur l'enceinte, ou
    /// null. Sert à « Ajouter ce qui joue » quand l'enceinte ne rapporte que le relais.
    /// </summary>
    public StrPlayedStream? LastPlayed { get; private set; }

    // ---------------------------------------------------------------- Réglages

    public Task SetVolumeAsync(int value, CancellationToken ct = default)
        => PutJsonAsync("/api/box/volume", new { value = Math.Clamp(value, 0, 100) }, ct);

    public Task SetBassAsync(int value, CancellationToken ct = default)
        => PutJsonAsync("/api/box/bass", new { value }, ct);

    public Task SetPowerAsync(bool on, CancellationToken ct = default)
        => PostJsonAsync("/api/box/power", new { on }, ct, on ? WakeTimeout : null);

    /// <summary>
    /// Minuterie de sommeil en cours (GET /api/box/sleep). Elle ne vit que dans la
    /// mémoire de l'agent : un redémarrage ou une mise à jour l'annule.
    /// </summary>
    public async Task<StrSleepState> GetSleepAsync(CancellationToken ct = default)
        => await GetJsonAsync<StrSleepState>("/api/box/sleep", ct).ConfigureAwait(false)
           ?? new StrSleepState();

    /// <summary>
    /// Arme la minuterie, en remplaçant celle qui tournait. À l'échéance l'enceinte
    /// passe en veille — et tout son groupe avec elle si <paramref name="group"/>.
    /// </summary>
    public Task SetSleepAsync(int minutes, bool group, CancellationToken ct = default)
        => PostJsonAsync("/api/box/sleep", new { minutes = Math.Clamp(minutes, 1, 720), group }, ct);

    /// <summary>Annule la minuterie de sommeil.</summary>
    public async Task CancelSleepAsync(CancellationToken ct = default)
    {
        using var cts = Budget(null, ct);
        using var response = await _http.DeleteAsync(BaseUrl + "/api/box/sleep", cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "/api/box/sleep", cts.Token).ConfigureAwait(false);
    }

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

    /// <summary>
    /// DELETE /api/box/zone — défait le groupe en cours.
    ///
    /// Un 200 ne vaut pas succès. L'agent répond 200 avec <c>ok:false</c> et le
    /// nombre de membres restants quand l'enceinte a accepté la requête puis gardé
    /// le groupe, et 200 avec <c>nothing:true</c> quand il n'y avait rien à défaire.
    /// Prendre le code HTTP pour une réussite, c'est afficher une coche verte sur
    /// un groupe qui joue encore.
    /// </summary>
    /// <param name="forget">Supprime aussi le groupe mémorisé, pas seulement le groupe actif.</param>
    /// <param name="stereo">Défait la paire stéréo au lieu du groupe multi-pièces.</param>
    public async Task<StrDissolveResult> DissolveZoneAsync(
        bool forget = false, bool stereo = false, CancellationToken ct = default)
    {
        var query = (forget, stereo) switch
        {
            (true, true) => "?forget=1&stereo=1",
            (true, false) => "?forget=1",
            (false, true) => "?stereo=1",
            _ => "",
        };

        using var cts = Budget(null, ct);
        using var response = await _http.DeleteAsync(BaseUrl + "/api/box/zone" + query, cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "/api/box/zone", cts.Token).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        return ReadDissolve(body);
    }

    private static StrDissolveResult ReadDissolve(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            // Agent plus ancien, qui ne décrit pas sa réponse : on ne peut que le croire.
            return new StrDissolveResult { Ok = true };
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new StrDissolveResult { Ok = true };
            }

            return new StrDissolveResult
            {
                Ok = ReadFlag(root, "ok") ?? true,
                Nothing = ReadFlag(root, "nothing") ?? false,
                // « firmwarePair » : même constat, quand la paire n'est connue que du
                // firmware et pas du magasin de l'agent.
                StereoPairKept = ReadFlag(root, "stereoPairKept", "firmwarePair") ?? false,
                Remaining = root.TryGetProperty("remaining", out var remaining) &&
                            remaining.ValueKind == JsonValueKind.Number
                    ? remaining.GetInt32()
                    : 0,
                Error = root.TryGetProperty("error", out var error) &&
                        error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : null,
            };
        }
        catch (JsonException)
        {
            return new StrDissolveResult { Ok = true };
        }
    }

    /// <summary>
    /// GET /api/box/presets — les touches telles que l'enceinte les rapporte (1 à 6),
    /// avec les marques de l'agent. Liste vide tant que l'enceinte ne les a pas
    /// encore rapportées.
    /// </summary>
    public async Task<IReadOnlyList<StrBoxPreset>> GetBoxPresetsAsync(CancellationToken ct = default)
        => await GetJsonAsync<List<StrBoxPreset>>("/api/box/presets", ct).ConfigureAwait(false)
           ?? new List<StrBoxPreset>();

    // ---------------------------------------------------------------- Réveils

    /// <summary>GET /api/alarms — le document des réveils de CETTE enceinte, avec son bloc d'état.</summary>
    public async Task<StrAlarmDocument> GetAlarmsAsync(CancellationToken ct = default)
        => StrAlarmDocument.Parse(await GetStringAsync("/api/alarms", ct).ConfigureAwait(false));

    /// <summary>
    /// PUT /api/alarms — remplace le document entier.
    ///
    /// Un 409 n'est pas une erreur : une autre application a enregistré depuis la
    /// lecture, et l'agent renvoie le document actuel pour qu'on y refasse la
    /// modification. Un 400 porte, en texte brut, la raison à montrer à
    /// l'utilisateur ; il remonte en <see cref="SpeakerException"/>.
    /// </summary>
    public async Task<StrAlarmSaveResult> PutAlarmsAsync(StrAlarmDocument document, CancellationToken ct = default)
    {
        using var content = new StringContent(document.ToPutBody(), Encoding.UTF8, "application/json");
        using var cts = Budget(null, ct);
        using var response = await _http.PutAsync(BaseUrl + "/api/alarms", content, cts.Token).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var current = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return new StrAlarmSaveResult { Current = StrAlarmDocument.Parse(current) };
        }

        await EnsureSuccessAsync(response, "/api/alarms", cts.Token).ConfigureAwait(false);

        var saved = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        return new StrAlarmSaveResult { Saved = StrAlarmDocument.Parse(saved) };
    }

    // ---------------------------------------------------------------- Favoris STR

    /// <summary>
    /// GET /api/favorites — les favoris de l'application STR, dans l'ordre choisi.
    /// Un agent qui ne les connaît pas répond 404 (<see cref="SpeakerException"/>).
    /// </summary>
    public async Task<IReadOnlyList<StrFavorite>> GetFavoritesAsync(CancellationToken ct = default)
    {
        var text = await GetStringAsync("/api/favorites", ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? Array.Empty<StrFavorite>() : StrFavorite.ParseList(text);
    }

    /// <summary>
    /// POST /api/favorites — ajoute UN favori. L'agent le transmet aux autres
    /// enceintes STR et à l'application sur ordinateur. Jamais PUT : ce point-là
    /// sert à la synchronisation de l'application sur ordinateur, qui le défait.
    /// </summary>
    public async Task<StrFavoriteAdded> AddFavoriteAsync(StrFavorite favorite, CancellationToken ct = default)
    {
        var text = await PostJsonReadAsync("/api/favorites", favorite, ct).ConfigureAwait(false);

        try
        {
            return (string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<StrFavoriteAdded>(text, JsonOptions))
                   ?? new StrFavoriteAdded { Ok = true, Added = true };
        }
        catch (JsonException)
        {
            // 200 sans corps lisible : l'agent a pris le favori.
            return new StrFavoriteAdded { Ok = true, Added = true };
        }
    }

    /// <summary>DELETE /api/favorites?id=&lt;clé&gt; — retire un favori, partout.</summary>
    public async Task RemoveFavoriteAsync(string key, CancellationToken ct = default)
    {
        var path = "/api/favorites?id=" + Uri.EscapeDataString(key);
        using var cts = Budget(null, ct);
        using var response = await _http.DeleteAsync(BaseUrl + path, cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "/api/favorites", cts.Token).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Actions des touches

    /// <summary>
    /// GET /api/keyactions — les actions des touches pouce et AUX de la télécommande de
    /// CETTE enceinte. Un agent qui ne les connaît pas répond 404 (<see cref="SpeakerException"/>).
    /// </summary>
    public async Task<StrKeyActions> GetKeyActionsAsync(CancellationToken ct = default)
    {
        var text = await GetStringAsync("/api/keyactions", ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? StrKeyActions.Empty() : StrKeyActions.Parse(text);
    }

    /// <summary>
    /// PUT /api/keyactions — remplace le document entier. Un refus (400 : action
    /// impossible sur cette touche ; 409 : la touche porte un groupe) remonte en
    /// <see cref="SpeakerException"/>, avec la raison en texte brut dans
    /// <see cref="SpeakerException.ResponseBody"/>. Rend le document enregistré
    /// que l'agent renvoie, ou à défaut celui qu'on a envoyé.
    /// </summary>
    public async Task<StrKeyActions> PutKeyActionsAsync(StrKeyActions document, CancellationToken ct = default)
    {
        using var content = new StringContent(document.ToPutBody(), Encoding.UTF8, "application/json");
        using var cts = Budget(null, ct);
        using var response = await _http.PutAsync(BaseUrl + "/api/keyactions", content, cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "/api/keyactions", cts.Token).ConfigureAwait(false);

        var saved = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

        try
        {
            return saved.Contains("\"keys\"", StringComparison.Ordinal) ? StrKeyActions.Parse(saved) : document.Clone();
        }
        catch (JsonException)
        {
            return document.Clone();
        }
    }

    // ---------------------------------------------------------------- Volume de départ

    /// <summary>
    /// GET /api/box/start-volume — le volume que prend l'enceinte quand on l'allume
    /// et qu'elle se met à jouer. 0 = désactivé ; null quand l'agent ne le gère pas.
    /// </summary>
    public async Task<int?> GetStartVolumeAsync(CancellationToken ct = default)
    {
        using var document = await GetJsonDocumentAsync("/api/box/start-volume", ct).ConfigureAwait(false);

        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var root = document.RootElement;

        if (root.TryGetProperty("supported", out var supported) && supported.ValueKind == JsonValueKind.False)
        {
            return null;
        }

        return root.TryGetProperty("volume", out var volume) &&
               volume.ValueKind == JsonValueKind.Number &&
               volume.TryGetInt32(out var level)
            ? Math.Clamp(level, 0, 100)
            : null;
    }

    /// <summary>POST /api/box/start-volume — 1 à 100, ou 0 pour désactiver. Enregistré sur l'enceinte.</summary>
    public Task SetStartVolumeAsync(int volume, CancellationToken ct = default)
        => PostJsonAsync("/api/box/start-volume", new { volume = Math.Clamp(volume, 0, 100) }, ct);

    /// <summary>
    /// GET /api/box/max-volume — le plafond de volume de l'enceinte (0 : aucun), ou
    /// null quand l'enceinte ne le gère pas. Même forme que le volume de départ.
    /// </summary>
    public async Task<int?> GetMaxVolumeAsync(CancellationToken ct = default)
    {
        using var document = await GetJsonDocumentAsync("/api/box/max-volume", ct).ConfigureAwait(false);

        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var root = document.RootElement;

        if (root.TryGetProperty("supported", out var supported) && supported.ValueKind == JsonValueKind.False)
        {
            return null;
        }

        return root.TryGetProperty("volume", out var volume) &&
               volume.ValueKind == JsonValueKind.Number &&
               volume.TryGetInt32(out var level)
            ? Math.Clamp(level, 0, 100)
            : null;
    }

    /// <summary>POST /api/box/max-volume — 1 à 100, ou 0 pour le retirer. Enregistré sur l'enceinte.</summary>
    public Task SetMaxVolumeAsync(int volume, CancellationToken ct = default)
        => PostJsonAsync("/api/box/max-volume", new { volume = Math.Clamp(volume, 0, 100) }, ct);

    // ---------------------------------------------------------------- Titres likés Spotify

    /// <summary>
    /// POST /api/spotify/liked (agent 1.8.1 et suivants) : ajoute le titre Spotify en
    /// cours aux Titres likés, ou l'en retire. Le titre va dans le compte Spotify avec
    /// lequel l'ENCEINTE est connectée. Un refus (409, 400, 502) remonte en
    /// <see cref="SpeakerException"/> ; <see cref="StrLikedResult.ReasonOf"/> en lit la raison.
    /// </summary>
    public async Task<StrLikedResult> SetSpotifyLikedAsync(bool liked, CancellationToken ct = default)
    {
        var text = await PostJsonReadAsync("/api/spotify/liked", new { liked }, ct).ConfigureAwait(false);
        return StrLikedResult.Parse(text);
    }

    // ---------------------------------------------------------------- Son coupé

    /// <summary>
    /// POST /api/box/mute : coupe ou rend le son, dans l'état demandé — jamais une
    /// bascule : deux télécommandes qui basculent en même temps s'annuleraient.
    /// C'est le MUTE de l'enceinte elle-même, celui que sa télécommande et
    /// l'application de bureau voient. Rend l'état annoncé par l'agent, ou l'état
    /// demandé quand sa réponse ne le dit pas. Un agent trop ancien répond 404 :
    /// une <see cref="SpeakerException"/>.
    /// </summary>
    public async Task<bool> SetMuteAsync(bool muted, CancellationToken ct = default)
    {
        var text = await PostJsonReadAsync("/api/box/mute", new { muted }, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                using var document = JsonDocument.Parse(text);

                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("muted", out var answer) &&
                    answer.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return answer.GetBoolean();
                }
            }
            catch (JsonException)
            {
                // Réponse illisible : on s'en tient à ce qui a été demandé.
            }
        }

        return muted;
    }

    // ---------------------------------------------------------------- Spotify, Deezer : touches et état

    /// <summary>
    /// Délai des appels Deezer qui attendent l'enceinte : retirer un compte, rendre
    /// la connexion gardée, lancer un lien (l'agent suit un lien de partage auprès
    /// de Deezer). Ce sont les délais de la page téléphone de STR.
    /// </summary>
    public static TimeSpan DeezerTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Délai de lecture de l'état Deezer : l'agent relit une fois la liste des sources de l'enceinte.</summary>
    public static TimeSpan DeezerReadTimeout { get; set; } = TimeSpan.FromSeconds(12);

    /// <summary>
    /// GET /spotify/info : le moteur Spotify de l'agent — compte connecté, formule,
    /// refus de Spotify, playlist chargée. Un agent sans moteur Spotify répond par
    /// une erreur : une <see cref="SpeakerException"/>.
    /// </summary>
    public async Task<StrSpotifyInfo> GetSpotifyInfoAsync(CancellationToken ct = default)
        => StrSpotifyInfo.Parse(await GetStringAsync("/spotify/info", ct).ConfigureAwait(false));

    /// <summary>GET /api/deezer/account (agent 1.9.0) : où en est Deezer sur l'enceinte.</summary>
    public async Task<StrDeezerState> GetDeezerStateAsync(CancellationToken ct = default)
        => StrDeezerState.Parse(await GetStringAsync("/api/deezer/account", ct, DeezerReadTimeout).ConfigureAwait(false));

    /// <summary>
    /// POST /api/deezer/recheck : « Vérifier à nouveau ». Quand l'agent garde une
    /// connexion Deezer que l'enceinte a perdue, il la lui rend. Répond comme
    /// <see cref="GetDeezerStateAsync"/>, avec ce qui a été tenté.
    /// </summary>
    public async Task<StrDeezerState> RecheckDeezerAsync(CancellationToken ct = default)
        => StrDeezerState.Parse(await PostJsonReadAsync("/api/deezer/recheck", new { }, ct, DeezerTimeout).ConfigureAwait(false));

    /// <summary>
    /// DELETE /api/deezer/account : l'enceinte oublie son compte Deezer, et l'agent
    /// sa copie. Sans le cloud Bose, aucun compte ne peut être relié ensuite. Un
    /// refus (502 remove-refused, still-linked…) remonte en <see cref="SpeakerException"/>.
    /// </summary>
    public async Task DisconnectDeezerAsync(CancellationToken ct = default)
    {
        using var cts = Budget(DeezerTimeout, ct);
        using var response = await _http.DeleteAsync($"{BaseUrl}/api/deezer/account", cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "/api/deezer/account", cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// POST /api/deezer/play : fait jouer une playlist, un album ou un titre Deezer
    /// par le client Deezer de l'enceinte. Un refus (409 deezer-not-linked,
    /// native-service-unavailable, 422 deezer-link-unresolved, 502…) remonte en
    /// <see cref="SpeakerException"/> ; <see cref="StrRefusal.From"/> en lit le code.
    /// </summary>
    public async Task<StrDeezerPlayed> PlayDeezerAsync(string url, CancellationToken ct = default)
    {
        var text = await PostJsonReadAsync("/api/deezer/play", new { url = url.Trim() }, ct, DeezerTimeout).ConfigureAwait(false);
        var kind = "";
        var name = "";

        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                using var document = JsonDocument.Parse(text);

                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    kind = TextOf(document.RootElement, "kind");
                    name = TextOf(document.RootElement, "name");
                }
            }
            catch (JsonException)
            {
                // Lecture partie quand même : seul le nom manque.
            }
        }

        return new StrDeezerPlayed(kind, name);
    }

    /// <summary>
    /// Garde ce que l'enceinte joue sur une touche (1 à 12), comme l'appui long de
    /// la page téléphone de STR (saveHeldSlot) :
    ///   – un service propre à l'enceinte (Deezer, Pandora, iHeartRadio) : la touche
    ///     reçoit { type: "native" } et l'agent prend l'élément dans ce que
    ///     l'enceinte joue ;
    ///   – Spotify : la touche reçoit la playlist (<see cref="SpotifyContext.SaveUri"/>)
    ///     et le compte du moteur ;
    ///   – le reste (station, dossier, piste…) : POST /api/box/save-playing, comme
    ///     avant.
    /// Un refus remonte en <see cref="SpeakerException"/> : 409 « already-on-slot »
    /// quand c'est déjà sur une autre touche, 422 quand rien ne peut être gardé.
    /// </summary>
    public async Task<StrKeptPlaying> KeepPlayingAsync(int slot, NowPlaying nowPlaying, CancellationToken ct = default)
    {
        if (slot is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Présélection 1 à 12 uniquement");
        }

        var source = (nowPlaying.Source ?? "").Trim().ToUpperInvariant();
        var location = nowPlaying.Content?.Location ?? "";
        var name = (nowPlaying.Content?.ItemName ?? "").Trim();

        if (source is "DEEZER" or "PANDORA" or "IHEART" or "IHEARTRADIO")
        {
            await PutJsonAsync($"/api/presets/{slot}", new { slot, name, type = "native" }, ct).ConfigureAwait(false);
            return new StrKeptPlaying(KeptKind.Native, name, false);
        }

        StrSpotifyInfo? info = null;

        try
        {
            info = await GetSpotifyInfoAsync(ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Pas de moteur Spotify sur cet agent : seule l'adresse de l'enceinte compte.
        }

        var uri = SpotifyContext.SaveUri(source, location, info?.Context);

        if (SpotifyContext.IsUri(uri))
        {
            var label = name.Length > 0 ? name
                : !string.IsNullOrWhiteSpace(info?.Track) ? info!.Track
                : "Spotify";

            await PutJsonAsync(
                $"/api/presets/{slot}?save=live",
                new { slot, name = label, type = "spotify", uri, account = info?.Account ?? "" },
                ct).ConfigureAwait(false);

            return new StrKeptPlaying(KeptKind.Spotify, label, false);
        }

        var kept = await SavePlayingAsync(slot, ct).ConfigureAwait(false);
        return new StrKeptPlaying(KeptKind.Playing, kept.Name ?? "", kept.Unchanged);
    }

    // ---------------------------------------------------------------- Écran de l'enceinte

    /// <summary>
    /// GET d'un réglage de l'écran (<see cref="StrDisplaySetting.Kinds"/>). Un agent
    /// qui ne le connaît pas répond par une erreur : une <see cref="SpeakerException"/>.
    /// </summary>
    public async Task<StrDisplaySetting> GetDisplaySettingAsync(string kind, CancellationToken ct = default)
        => StrDisplaySetting.Parse(kind, await GetStringAsync(StrDisplaySetting.PathOf(kind), ct).ConfigureAwait(false));

    /// <summary>
    /// Active ou coupe un réglage de l'écran. Pour le titre en cours,
    /// <paramref name="mode"/> dit quoi montrer : artist, title ou both.
    /// </summary>
    public Task SetDisplaySettingAsync(string kind, bool enabled, string? mode = null, CancellationToken ct = default)
    {
        var path = StrDisplaySetting.PathOf(kind);

        return kind == StrDisplaySetting.Track
            ? PostJsonAsync(path, new { enabled, mode = mode ?? StrDisplaySetting.ModeBoth }, ct)
            : PostJsonAsync(path, new { enabled }, ct);
    }

    /// <summary>POST /api/box/identify : « Trouver cette enceinte », HELLO et son nom sur l'écran.</summary>
    public async Task<StrIdentifyOutcome> IdentifyAsync(CancellationToken ct = default)
    {
        var text = await PostJsonReadAsync("/api/box/identify", new { }, ct).ConfigureAwait(false);

        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("shown", out var shown) && shown.ValueKind == JsonValueKind.True)
                {
                    return StrIdentifyOutcome.Shown;
                }

                return TextOf(root, "reason") switch
                {
                    "no display" => StrIdentifyOutcome.NoDisplay,
                    "busy" => StrIdentifyOutcome.Busy,
                    _ => StrIdentifyOutcome.Failed,
                };
            }
        }
        catch (JsonException)
        {
            // Réponse illisible : rien n'est sûr.
        }

        return StrIdentifyOutcome.Failed;
    }

    private static string TextOf(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? "").Trim()
            : "";

    // ---------------------------------------------------------------- Annonce (message vocal)

    /// <summary>
    /// Délai d'une annonce. L'agent télécharge le clip en entier (8 Mo au plus, en
    /// 25 secondes au plus) ou fait lire le texte par la voix de Google avant de le
    /// jouer ; la réponse peut ne venir qu'à la fin du message.
    /// </summary>
    public static TimeSpan AnnounceTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// POST /api/announce : l'enceinte joue un clip (<paramref name="url"/>, que
    /// l'agent va chercher lui-même) ou lit un texte (<paramref name="text"/>, voix
    /// de Google Translate : l'enceinte doit avoir accès à internet), puis reprend
    /// ce qu'elle jouait. <paramref name="lang"/> prend les codes de langue de
    /// Google (fr, en, de, nl, es, it…). Un refus remonte en
    /// <see cref="SpeakerException"/>.
    /// </summary>
    public Task AnnounceAsync(string? text, string? lang, string? url, int? volume = null, CancellationToken ct = default)
        => PostJsonAsync(
            "/api/announce",
            new StrAnnounceRequest
            {
                Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
                Lang = string.IsNullOrWhiteSpace(lang) ? null : lang,
                Url = string.IsNullOrWhiteSpace(url) ? null : url,
                Volume = volume is { } v ? Math.Clamp(v, 1, 100) : null,
            },
            ct,
            AnnounceTimeout);

    // ---------------------------------------------------------------- Copie des touches

    /// <summary>
    /// Délai pour une copie : l'agent écrit les touches sur chaque cible, une par une
    /// sur une cible ancienne, et transmet le compte Spotify si besoin.
    /// </summary>
    public static TimeSpan CopyTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// POST /api/box/preset-copy (agent 1.6.5 et suivants) : copie les touches de
    /// cette enceinte vers les cibles nommées par leur adresse IP, ou vers toutes
    /// celles que l'agent connaît (<paramref name="targets"/> null). Répond quand
    /// chaque cible a été traitée. Un refus global (409 busy, no-presets,
    /// no-targets) ou un agent trop ancien (404) remonte en
    /// <see cref="SpeakerException"/> ; <see cref="StrRefusal.From"/> en lit le code.
    /// </summary>
    public async Task<StrPresetCopyResult> CopyPresetsAsync(IReadOnlyList<string>? targets, CancellationToken ct = default)
    {
        object body = targets is null
            ? new { all = true }
            : new { targets = targets.ToArray() };

        var text = await PostJsonReadAsync("/api/box/preset-copy", body, ct, CopyTimeout).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? StrPresetCopyResult.Parse("{}") : StrPresetCopyResult.Parse(text);
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

    /// <summary>
    /// POST JSON dont le CORPS de réponse compte. Plusieurs points de l'agent
    /// répondent 200 en disant dans le corps que rien ne s'est passé.
    /// </summary>
    private async Task<string> PostJsonReadAsync<T>(string path, T body, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var cts = Budget(timeout, ct);
        using var response = await _http.PostAsync(BaseUrl + path, content, cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, path, cts.Token).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
    }

    private async Task PutJsonAsync<T>(string path, T body, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var cts = Budget(timeout, ct);
        using var response = await _http.PutAsync(BaseUrl + path, content, cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, path, cts.Token).ConfigureAwait(false);
    }

    private async Task PutRawAsync(string path, string json, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var cts = Budget(timeout, ct);
        using var response = await _http.PutAsync(BaseUrl + path, content, cts.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, path, cts.Token).ConfigureAwait(false);
    }

    private async Task PatchJsonAsync<T>(string path, T body, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Patch, BaseUrl + path) { Content = content };
        using var cts = Budget(timeout, ct);
        using var response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
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

        throw new SpeakerException(
            $"{path} a répondu {(int)response.StatusCode}{hint}.", body,
            statusCode: response.StatusCode);
    }
}

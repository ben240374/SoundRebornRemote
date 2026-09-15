using SoundReborn.Core.Models;

namespace SoundReborn.Core;

/// <summary>
/// Façade sur une enceinte : réunit l'API Bose (8090), l'API de l'agent STR
/// quand il est présent (8888/17008) et le flux de notifications (8080).
///
/// Règle de routage : on passe par STR dès qu'il répond, parce que c'est la surface
/// stable et qu'elle gère l'état de l'enceinte (réveil, reprise). On retombe sur
/// l'API Bose sinon, ou quand STR refuse la commande.
/// </summary>
public sealed class SpeakerDevice : IAsyncDisposable
{
    private SpeakerDevice(SpeakerEndpoint endpoint, BoseApiClient bose, StrApiClient? str, GabboSocket notifications)
    {
        Endpoint = endpoint;
        Bose = bose;
        Str = str;
        Notifications = notifications;
    }

    public SpeakerEndpoint Endpoint { get; }

    public BoseApiClient Bose { get; }

    public StrApiClient? Str { get; }

    public GabboSocket Notifications { get; }

    public bool HasStr => Str is not null;

    public string Host => Endpoint.Host;

    /// <summary>Plage de graves du modèle, lue une fois à la connexion.</summary>
    public BassCapabilities BassCapabilities { get; private set; } = BassCapabilities.Unknown;

    /// <summary>Version de l'agent STR, lue une fois à la connexion. Null sans agent.</summary>
    public AgentInfo? Agent { get; private set; }

    /// <summary>
    /// Ouvre une session : détecte l'agent STR si son port n'est pas déjà connu,
    /// lit les capacités de graves et démarre le flux de notifications.
    /// </summary>
    public static async Task<SpeakerDevice> ConnectAsync(
        SpeakerEndpoint endpoint,
        HttpClient http,
        bool startNotifications = true,
        CancellationToken ct = default)
    {
        var bose = new BoseApiClient(endpoint.Host, http);

        if (endpoint.StrPort is null)
        {
            var probe = await StrApiClient
                .ProbeAsync(endpoint.Host, http, TimeSpan.FromSeconds(2), ct)
                .ConfigureAwait(false);

            if (probe is { } found)
            {
                endpoint.StrPort = found.Port;

                if (!string.IsNullOrWhiteSpace(found.Version))
                {
                    endpoint.AgentVersion = found.Version;
                }
            }
        }

        var str = endpoint.StrPort is { } port ? new StrApiClient(endpoint.Host, port, http) : null;
        var device = new SpeakerDevice(endpoint, bose, str, new GabboSocket(endpoint.Host));

        try
        {
            device.BassCapabilities = await bose.GetBassCapabilitiesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            device.BassCapabilities = BassCapabilities.Unknown;
        }

        if (str is not null)
        {
            try
            {
                device.Agent = await str.GetAgentInfoAsync(ct).ConfigureAwait(false);
                endpoint.AgentVersion = device.Agent?.DisplayVersion ?? "";
            }
            catch (Exception)
            {
                // Agent trop ancien pour /api/agent/version : on affiche juste le port.
            }
        }

        if (startNotifications)
        {
            device.Notifications.Start();
        }

        return device;
    }

    // ---------------------------------------------------------------- Lecture d'état

    public async Task<NowPlaying> GetNowPlayingAsync(CancellationToken ct = default)
    {
        if (Str is not null)
        {
            try
            {
                // L'agent met un micro-cache devant le firmware : moins de risque
                // de figer un BoseApp fragile quand plusieurs clients interrogent.
                return await Str.GetStatusAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsDefiniteRefusal(ex))
            {
                // Refus net de l'agent : on retombe sur le firmware.
            }
        }

        return await Bose.GetNowPlayingAsync(ct).ConfigureAwait(false);
    }

    public async Task<VolumeState> GetVolumeAsync(CancellationToken ct = default)
    {
        if (Str is not null)
        {
            try
            {
                var volume = await Str.GetVolumeAsync(ct).ConfigureAwait(false);

                if (volume is not null)
                {
                    return new VolumeState(volume.Target, volume.Value, volume.Muted);
                }
            }
            catch (Exception ex) when (IsDefiniteRefusal(ex))
            {
                // Refus net de l'agent : on retombe sur le firmware.
            }
        }

        return await Bose.GetVolumeAsync(ct).ConfigureAwait(false);
    }

    public Task<BassState> GetBassAsync(CancellationToken ct = default) => Bose.GetBassAsync(ct);

    public Task<IReadOnlyList<PresetInfo>> GetPresetsAsync(CancellationToken ct = default) => Bose.GetPresetsAsync(ct);

    public Task<IReadOnlyList<SourceItem>> GetSourcesAsync(CancellationToken ct = default) => Bose.GetSourcesAsync(ct);

    public Task<ZoneState> GetZoneAsync(CancellationToken ct = default) => Bose.GetZoneAsync(ct);

    // ---------------------------------------------------------------- Commandes

    public async Task SetVolumeAsync(int volume, CancellationToken ct = default)
    {
        volume = Math.Clamp(volume, 0, 100);

        if (Str is not null)
        {
            try
            {
                await Str.SetVolumeAsync(volume, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsDefiniteRefusal(ex))
            {
                // Refus net de l'agent : on retombe sur le firmware.
            }
        }

        await Bose.SetVolumeAsync(volume, ct).ConfigureAwait(false);
    }

    /// <summary>La coupure du son n'a pas d'équivalent STR : c'est la touche MUTE du firmware.</summary>
    public Task ToggleMuteAsync(CancellationToken ct = default) => Bose.PressKeyAsync(RemoteKey.Mute, ct);

    public async Task PlayPauseAsync(bool isPlaying, CancellationToken ct = default)
    {
        if (Str is not null)
        {
            try
            {
                if (isPlaying)
                {
                    await Str.PauseAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    await Str.ResumeAsync(ct).ConfigureAwait(false);
                }

                return;
            }
            catch (Exception ex) when (IsDefiniteRefusal(ex))
            {
                // Refus net de l'agent : on retombe sur le firmware.
            }
        }

        await Bose.PressKeyAsync(RemoteKey.PlayPause, ct).ConfigureAwait(false);
    }

    public async Task NextAsync(CancellationToken ct = default)
    {
        if (Str is not null)
        {
            try
            {
                await Str.NextAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsDefiniteRefusal(ex))
            {
                // Refus net de l'agent : on retombe sur le firmware.
            }
        }

        await Bose.PressKeyAsync(RemoteKey.NextTrack, ct).ConfigureAwait(false);
    }

    public async Task PreviousAsync(CancellationToken ct = default)
    {
        if (Str is not null)
        {
            try
            {
                await Str.PreviousAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsDefiniteRefusal(ex))
            {
                // Refus net de l'agent : on retombe sur le firmware.
            }
        }

        await Bose.PressKeyAsync(RemoteKey.PrevTrack, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Rappelle une présélection. Avec STR on vise d'abord son magasin, qui sait jouer
    /// une radio ou une playlist Spotify que le cloud Bose gérait autrefois ; sinon
    /// on mime l'appui sur la touche matérielle.
    /// </summary>
    public async Task RecallPresetAsync(int slot, CancellationToken ct = default)
    {
        if (Str is not null)
        {
            try
            {
                await Str.PlaySlotAsync(slot, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsDefiniteRefusal(ex))
            {
                // Refus net de l'agent : la touche matérielle reste une option.
            }
        }

        await Bose.RecallPresetAsync(slot, ct).ConfigureAwait(false);
    }

    public async Task SetBassAsync(int value, CancellationToken ct = default)
    {
        if (BassCapabilities.Available)
        {
            value = Math.Clamp(value, BassCapabilities.Min, BassCapabilities.Max);
        }

        if (Str is not null)
        {
            try
            {
                await Str.SetBassAsync(value, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsDefiniteRefusal(ex))
            {
                // Refus net de l'agent : on retombe sur le firmware.
            }
        }

        await Bose.SetBassAsync(value, ct).ConfigureAwait(false);
    }

    /// <summary>Allumage ou mise en veille. STR sait en plus reprendre la dernière station.</summary>
    public async Task SetPowerAsync(bool on, CancellationToken ct = default)
    {
        if (Str is not null)
        {
            try
            {
                await Str.SetPowerAsync(on, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsDefiniteRefusal(ex))
            {
                // Refus net de l'agent : on retombe sur le firmware.
            }
        }

        await Bose.PressKeyAsync(RemoteKey.Power, ct).ConfigureAwait(false);
    }

    public Task SelectSourceAsync(SourceItem source, CancellationToken ct = default)
        => Bose.SelectAsync(source.ToContentItem(), ct);

    /// <summary>
    /// Lance une station trouvée dans l'annuaire. Passe par l'agent, qui ajoute les
    /// métadonnées DIDL, bascule HTTPS en HTTP et convertit le HLS à la volée —
    /// autant de choses que l'enceinte seule ne sait pas faire.
    /// </summary>
    public Task PlayStationAsync(RadioStation station, CancellationToken ct = default)
    {
        if (Str is null)
        {
            throw new InvalidOperationException("Jouer une station demande l'agent STR.");
        }

        // Le codec part tel que l'annuaire le donne : c'est ce que l'agent attend.
        // Y mettre un type MIME le ferait basculer en mode « fichier local ».
        return Str.PlayUrlAsync(station.PlayableUrl, station.Name, station.Codec, station.Favicon, ct);
    }

    /// <summary>Enregistre une station sur une touche de présélection (1 à 6).</summary>
    public Task SaveStationAsync(RadioStation station, int slot, CancellationToken ct = default)
    {
        if (Str is null)
        {
            throw new InvalidOperationException("Enregistrer une présélection demande l'agent STR.");
        }

        return Str.SavePresetAsync(station.ToPreset(slot), ct);
    }


    /// <summary>
    /// Décide si l'échec d'un appel à l'agent autorise le repli sur la touche
    /// matérielle.
    ///
    /// Une réponse d'erreur ou une connexion refusée sont des refus : l'agent a
    /// tranché, le repli est légitime. Un délai dépassé ne dit rien — la commande
    /// est peut-être arrivée et c'est la réponse qui s'est perdue. Or POWER et
    /// PLAY_PAUSE sont des bascules : rejouer la commande éteindrait l'enceinte
    /// qu'on voulait allumer. Dans le doute, on ne fait rien.
    /// </summary>
    private static bool IsDefiniteRefusal(Exception ex)
        => ex is SpeakerException || ex is HttpRequestException;

    public async ValueTask DisposeAsync() => await Notifications.DisposeAsync().ConfigureAwait(false);
}

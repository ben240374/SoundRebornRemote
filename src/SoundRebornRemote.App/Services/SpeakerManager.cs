using System.Text.Json;
using SoundReborn.Core;
using SoundReborn.Core.Models;

namespace SoundRebornRemote.App.Services;

/// <summary>
/// Garde l'enceinte sélectionnée pour toute l'application, mémorise la liste des
/// enceintes connues entre deux lancements, et ouvre/ferme proprement la session
/// (notamment le WebSocket de notifications, qui ne doit jamais rester en double).
/// </summary>
public sealed class SpeakerManager
{
    private const string KnownSpeakersKey = "known_speakers";
    private const string SelectedHostKey = "selected_speaker_host";

    /// <summary>Budget par port sondé pour retrouver l'agent d'une enceinte.</summary>
    private static readonly TimeSpan AgentProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http;
    private readonly ZoneLocator _zones;
    private readonly SemaphoreSlim _switchGate = new(1, 1);

    public SpeakerManager(HttpClient http, ZoneLocator zones)
    {
        _http = http;
        _zones = zones;
        KnownSpeakers = LoadKnownSpeakers();
    }

    /// <summary>Déclenché après chaque changement d'enceinte active (y compris la déconnexion).</summary>
    public event EventHandler<SpeakerDevice?>? DeviceChanged;

    /// <summary>
    /// Déclenché quand la liste des enceintes connues change : ajout par un balayage,
    /// ou retrait depuis les réglages. Les écrans qui affichent cette liste s'y
    /// abonnent, sinon ils gardent l'ancienne jusqu'à leur prochaine apparition —
    /// une enceinte oubliée resterait affichée, et une enceinte retrouvée manquerait.
    /// </summary>
    public event EventHandler? KnownSpeakersChanged;

    /// <summary>
    /// L'enceinte qui reçoit les commandes. Ce n'est pas forcément celle que
    /// l'utilisateur a désignée : dans un groupe, c'est le maître qui diffuse, et
    /// c'est donc lui qu'il faut commander.
    /// </summary>
    public SpeakerDevice? Current { get; private set; }

    /// <summary>
    /// L'enceinte que l'utilisateur a désignée. Elle ne sert qu'à l'affichage des
    /// réglages : toucher une enceinte d'un groupe ne doit rien changer à ce qui
    /// joue, seulement dire par où l'on regarde.
    /// </summary>
    public SpeakerEndpoint? SelectedEndpoint { get; private set; }

    /// <summary>Vrai quand les commandes partent ailleurs que vers l'enceinte désignée.</summary>
    public bool IsRedirected =>
        Current is not null && SelectedEndpoint is not null &&
        !string.Equals(Current.Host, SelectedEndpoint.Host, StringComparison.OrdinalIgnoreCase);

    public List<SpeakerEndpoint> KnownSpeakers { get; }

    public HttpClient Http => _http;

    /// <summary>Hôte de la dernière enceinte utilisée, pour la reconnexion au démarrage.</summary>
    public string? LastSelectedHost
    {
        get
        {
            var stored = Preferences.Default.Get(SelectedHostKey, string.Empty);
            return string.IsNullOrWhiteSpace(stored) ? null : stored;
        }

        private set
        {
            if (value is null)
            {
                Preferences.Default.Remove(SelectedHostKey);
            }
            else
            {
                Preferences.Default.Set(SelectedHostKey, value);
            }
        }
    }

    /// <summary>
    /// Bascule sur une enceinte. L'ancienne session est fermée d'abord, sinon deux
    /// WebSockets restent ouverts et l'interface reçoit les événements des deux.
    /// </summary>
    public async Task<SpeakerDevice> SelectAsync(SpeakerEndpoint endpoint, CancellationToken ct = default)
    {
        SelectedEndpoint = endpoint;
        LastSelectedHost = endpoint.Host;
        Remember(endpoint);

        return await ConnectToTargetAsync(await ResolveTargetAsync(endpoint, ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Relit la zone et, si besoin, redirige les commandes. Appelée quand un groupe
    /// se forme ou se défait : l'enceinte à commander change alors sans que
    /// l'utilisateur ait touché à sa sélection.
    ///
    /// Renvoie vrai quand la cible a effectivement changé.
    /// </summary>
    public async Task<bool> RetargetAsync(CancellationToken ct = default)
    {
        var selected = SelectedEndpoint;

        if (selected is null || Current is null)
        {
            return false;
        }

        var target = await ResolveTargetAsync(selected, ct).ConfigureAwait(false);

        if (string.Equals(target.Host, Current.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await ConnectToTargetAsync(target, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Qui commander pour cette sélection : le maître du groupe si l'enceinte
    /// désignée ne fait que suivre, sinon elle-même.
    /// </summary>
    private async Task<SpeakerEndpoint> ResolveTargetAsync(SpeakerEndpoint selected, CancellationToken ct)
    {
        try
        {
            var zone = await _zones.LocateAsync(selected.Host, KnownSpeakers, ct).ConfigureAwait(false);

            if (!zone.Grouped || zone.IsMaster(selected.Host) || string.IsNullOrWhiteSpace(zone.MasterIp))
            {
                return selected;
            }

            // Le maître doit être une enceinte connue : se brancher sur une adresse
            // qu'on n'a jamais balayée ferait perdre son nom, son modèle et son port
            // d'agent, et l'écran n'afficherait plus qu'une IP.
            return KnownSpeakers.FirstOrDefault(s =>
                string.Equals(s.Host, zone.MasterIp, StringComparison.OrdinalIgnoreCase)) ?? selected;
        }
        catch (Exception)
        {
            // Zone illisible : on commande ce qui a été désigné, faute de mieux.
            return selected;
        }
    }

    private async Task<SpeakerDevice> ConnectToTargetAsync(SpeakerEndpoint target, CancellationToken ct)
    {
        await _switchGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // Déjà branché sur cette enceinte : on ne coupe pas pour rebrancher
            // aussitôt. Le faire arrêtait le WebSocket et faisait échouer les
            // lectures en cours — les six touches revenaient vides parce que leur
            // relecture tombait dans ce trou. Seule la sélection a changé, donc on
            // se contente de l'annoncer.
            if (Current is not null &&
                string.Equals(Current.Host, target.Host, StringComparison.OrdinalIgnoreCase))
            {
                DeviceChanged?.Invoke(this, Current);
                return Current;
            }

            if (Current is not null)
            {
                await Current.DisposeAsync().ConfigureAwait(false);
                Current = null;
            }

            var device = await SpeakerDevice.ConnectAsync(target, _http, startNotifications: true, ct).ConfigureAwait(false);

            Current = device;
            Remember(target);

            DeviceChanged?.Invoke(this, device);
            return device;
        }
        finally
        {
            _switchGate.Release();
        }
    }

    /// <summary>Reconnecte l'enceinte mémorisée au démarrage. Renvoie null si rien n'est joignable.</summary>
    public async Task<SpeakerDevice?> TryRestoreAsync(CancellationToken ct = default)
    {
        var host = LastSelectedHost;

        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var endpoint = KnownSpeakers.FirstOrDefault(s => string.Equals(s.Host, host, StringComparison.OrdinalIgnoreCase))
                       ?? new SpeakerEndpoint { Host = host };

        try
        {
            return await SelectAsync(endpoint, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Enceinte éteinte ou adresse changée : l'utilisateur relancera un balayage.
            return null;
        }
    }

    /// <summary>
    /// Relit la version de l'agent de chaque enceinte qui en a un.
    ///
    /// Toutes, pas seulement celles dont la version manque : un agent se met à jour
    /// sur l'enceinte, sans que l'application en sache rien. Ne relire que les
    /// versions absentes gravait la première valeur lue pour toujours, et l'écran
    /// annonçait une version périmée des mois durant.
    ///
    /// Renvoie vrai quand au moins une version a changé — l'appelant rafraîchit
    /// alors son affichage.
    /// </summary>
    public async Task<bool> EnsureAgentVersionsAsync(CancellationToken ct = default)
    {
        var pending = KnownSpeakers.Where(s => s.HasStr).ToList();

        if (pending.Count == 0)
        {
            return false;
        }

        var changed = false;

        foreach (var speaker in pending)
        {
            try
            {
                // Sonde des deux ports plutôt qu'appel sur le port enregistré : une
                // réinstallation de l'agent peut le déplacer de 8888 à 17008, et
                // l'application serait restée à interroger une porte fermée.
                var found = await StrApiClient
                    .ProbeAsync(speaker.Host, _http, AgentProbeTimeout, ct)
                    .ConfigureAwait(false);

                if (found is null)
                {
                    continue;
                }

                if (speaker.StrPort != found.Value.Port ||
                    !string.Equals(speaker.AgentVersion, found.Value.Version, StringComparison.Ordinal))
                {
                    speaker.StrPort = found.Value.Port;
                    speaker.AgentVersion = found.Value.Version;
                    changed = true;
                }
            }
            catch (Exception)
            {
                // Enceinte éteinte ou agent muet : on réessaiera au prochain passage.
                // Surtout, on ne vide pas la version connue : une enceinte
                // momentanément injoignable n'a pas perdu son agent.
            }
        }

        if (changed)
        {
            SaveKnownSpeakers();
            KnownSpeakersChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    /// <summary>Ajoute ou met à jour une enceinte dans la liste persistée.</summary>
    public void Remember(SpeakerEndpoint endpoint)
    {
        var existing = KnownSpeakers.FirstOrDefault(s => string.Equals(s.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase));
        var added = existing is null;

        if (existing is null)
        {
            KnownSpeakers.Add(endpoint);
        }
        else
        {
            existing.Name = string.IsNullOrWhiteSpace(endpoint.Name) ? existing.Name : endpoint.Name;
            existing.DeviceId = string.IsNullOrWhiteSpace(endpoint.DeviceId) ? existing.DeviceId : endpoint.DeviceId;
            existing.Type = string.IsNullOrWhiteSpace(endpoint.Type) ? existing.Type : endpoint.Type;
            existing.StrPort = endpoint.StrPort ?? existing.StrPort;
            existing.AgentVersion = string.IsNullOrWhiteSpace(endpoint.AgentVersion)
                ? existing.AgentVersion
                : endpoint.AgentVersion;
        }

        SaveKnownSpeakers();

        if (added)
        {
            KnownSpeakersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Forget(SpeakerEndpoint endpoint)
    {
        var removed = KnownSpeakers.RemoveAll(s => string.Equals(s.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(LastSelectedHost, endpoint.Host, StringComparison.OrdinalIgnoreCase))
        {
            LastSelectedHost = null;
        }

        SaveKnownSpeakers();

        if (removed > 0)
        {
            KnownSpeakersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SaveKnownSpeakers()
    {
        try
        {
            Preferences.Default.Set(KnownSpeakersKey, JsonSerializer.Serialize(KnownSpeakers.Select(SpeakerRecord.From)));
        }
        catch (Exception)
        {
            // La persistance des réglages n'est pas critique : on continue sans.
        }
    }

    private static List<SpeakerEndpoint> LoadKnownSpeakers()
    {
        try
        {
            var raw = Preferences.Default.Get(KnownSpeakersKey, string.Empty);

            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<SpeakerEndpoint>();
            }

            var records = JsonSerializer.Deserialize<List<SpeakerRecord>>(raw) ?? new List<SpeakerRecord>();
            return records.Where(r => !string.IsNullOrWhiteSpace(r.Host)).Select(r => r.ToEndpoint()).ToList();
        }
        catch (Exception)
        {
            return new List<SpeakerEndpoint>();
        }
    }

    /// <summary>Forme sérialisable de SpeakerEndpoint (qui n'a pas de constructeur sans paramètre).</summary>
    private sealed class SpeakerRecord
    {
        public string Host { get; set; } = "";
        public string Name { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string Type { get; set; } = "";
        public int? StrPort { get; set; }
        public string AgentVersion { get; set; } = "";

        public static SpeakerRecord From(SpeakerEndpoint endpoint) => new()
        {
            Host = endpoint.Host,
            Name = endpoint.Name,
            DeviceId = endpoint.DeviceId,
            Type = endpoint.Type,
            StrPort = endpoint.StrPort,
            AgentVersion = endpoint.AgentVersion,
        };

        public SpeakerEndpoint ToEndpoint() => new()
        {
            Host = Host,
            Name = Name,
            DeviceId = DeviceId,
            Type = Type,
            StrPort = StrPort,
            AgentVersion = AgentVersion,
        };
    }
}

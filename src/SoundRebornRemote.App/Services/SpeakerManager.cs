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

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _switchGate = new(1, 1);

    public SpeakerManager(HttpClient http)
    {
        _http = http;
        KnownSpeakers = LoadKnownSpeakers();
    }

    /// <summary>Déclenché après chaque changement d'enceinte active (y compris la déconnexion).</summary>
    public event EventHandler<SpeakerDevice?>? DeviceChanged;

    public SpeakerDevice? Current { get; private set; }

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
        await _switchGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (Current is not null)
            {
                await Current.DisposeAsync().ConfigureAwait(false);
                Current = null;
            }

            var device = await SpeakerDevice.ConnectAsync(endpoint, _http, startNotifications: true, ct).ConfigureAwait(false);

            Current = device;
            LastSelectedHost = endpoint.Host;
            Remember(endpoint);

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
    /// Complète la version de l'agent des enceintes connues qui ne l'ont pas encore.
    /// Une enceinte ajoutée par une version antérieure de l'application, ou trouvée
    /// avant que cette lecture n'existe, n'a rien d'enregistré ; c'est ici qu'elle
    /// le récupère, en tâche de fond et sans bloquer l'affichage.
    /// </summary>
    public async Task<bool> EnsureAgentVersionsAsync(CancellationToken ct = default)
    {
        var pending = KnownSpeakers
            .Where(s => s.HasStr && string.IsNullOrWhiteSpace(s.AgentVersion))
            .ToList();

        if (pending.Count == 0)
        {
            return false;
        }

        var changed = false;

        foreach (var speaker in pending)
        {
            try
            {
                var client = new StrApiClient(speaker.Host, speaker.StrPort!.Value, _http);
                var agent = await client.GetAgentInfoAsync(ct).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(agent?.DisplayVersion))
                {
                    speaker.AgentVersion = agent!.DisplayVersion;
                    changed = true;
                }
            }
            catch (Exception)
            {
                // Enceinte éteinte ou agent muet : on réessaiera au prochain passage.
            }
        }

        if (changed)
        {
            SaveKnownSpeakers();
        }

        return changed;
    }

    /// <summary>Ajoute ou met à jour une enceinte dans la liste persistée.</summary>
    public void Remember(SpeakerEndpoint endpoint)
    {
        var existing = KnownSpeakers.FirstOrDefault(s => string.Equals(s.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase));

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
    }

    public void Forget(SpeakerEndpoint endpoint)
    {
        KnownSpeakers.RemoveAll(s => string.Equals(s.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(LastSelectedHost, endpoint.Host, StringComparison.OrdinalIgnoreCase))
        {
            LastSelectedHost = null;
        }

        SaveKnownSpeakers();
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

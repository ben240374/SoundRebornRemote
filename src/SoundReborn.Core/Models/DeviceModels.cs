using System.ComponentModel;

namespace SoundReborn.Core.Models;

/// <summary>Contenu de GET /info sur le port 8090.</summary>
public sealed class DeviceInfo
{
    public string DeviceId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string? SerialNumber { get; init; }
    public string? SoftwareVersion { get; init; }
    public string? MacAddress { get; init; }
    public string? IpAddress { get; init; }

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? DeviceId : Name;
}

/// <summary>
/// Une enceinte trouvée sur le réseau. <see cref="StrPort"/> vaut null tant qu'aucun
/// agent STR n'a répondu ; sinon 8888 ou 17008 selon le châssis.
///
/// La classe signale ses changements : ces valeurs sont modifiées en place quand un
/// balayage repasse sur une enceinte déjà connue, et les listes affichées doivent
/// suivre — sans quoi une enceinte qui vient de recevoir l'agent STR resterait
/// marquée comme dépourvue jusqu'au prochain lancement.
/// </summary>
public sealed class SpeakerEndpoint : INotifyPropertyChanged
{
    private string _name = "";
    private string _deviceId = "";
    private string _type = "";
    private int? _strPort;
    private string _agentVersion = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Host { get; init; }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value, nameof(Name), nameof(DisplayName), nameof(Summary));
    }

    public string DeviceId
    {
        get => _deviceId;
        set => Set(ref _deviceId, value, nameof(DeviceId));
    }

    public string Type
    {
        get => _type;
        set => Set(ref _type, value, nameof(Type), nameof(Summary));
    }

    public int? StrPort
    {
        get => _strPort;
        set => Set(ref _strPort, value, nameof(StrPort), nameof(HasStr), nameof(NeedsStr), nameof(Summary));
    }

    /// <summary>Version de l'agent STR, « v0.9.79 » — vide tant qu'elle n'a pas été lue.</summary>
    public string AgentVersion
    {
        get => _agentVersion;
        set => Set(ref _agentVersion, value, nameof(AgentVersion), nameof(Summary));
    }

    public bool HasStr => StrPort.HasValue;

    /// <summary>
    /// Enceinte d'origine : le firmware répond, aucun agent STR n'écoute. Elle reste
    /// pilotable pour le peu que le firmware seul expose, et elle est candidate à
    /// une installation de STR — qui se fait depuis l'outil PC, pas depuis ici.
    /// </summary>
    public bool NeedsStr => !StrPort.HasValue;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Host : Name;

    /// <summary>
    /// Résumé volontairement sans mot traduisible : la bibliothèque ne connaît pas
    /// la langue de l'interface, donc elle ne produit que des données.
    /// </summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>(3) { Host };

            if (!string.IsNullOrWhiteSpace(Type))
            {
                parts.Add(Type);
            }

            if (HasStr)
            {
                parts.Add(string.IsNullOrWhiteSpace(AgentVersion) ? $"STR:{StrPort}" : $"STR {AgentVersion}");
            }

            return string.Join(" · ", parts);
        }
    }

    public override bool Equals(object? obj) =>
        obj is SpeakerEndpoint other && string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Host);

    /// <summary>
    /// Pose la valeur et signale la propriété, plus celles qui en dérivent. Le nom
    /// est passé explicitement : « params » doit rester le dernier paramètre, donc
    /// pas de CallerMemberName après lui.
    /// </summary>
    private void Set<T>(ref T field, T value, string name, params string[] derived)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;

        Raise(name);

        foreach (var other in derived)
        {
            Raise(other);
        }
    }

    private void Raise(string? name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

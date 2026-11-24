using System.Text.Json;
using System.Text.Json.Serialization;
using SoundReborn.Core.Voice;

namespace SoundReborn.Core.Models;

/// <summary>
/// Le fichier de sauvegarde de ce qui ne vit que dans le téléphone : les
/// présélections « Local », les textes gardés du message vocal et, depuis la
/// version 2 du fichier, les réglages de l'application (<see cref="AppSettingsBackup"/>).
///
/// Ces listes sont dans les préférences de l'application. Elles survivent à une
/// mise à jour, mais pas à une désinstallation — ni à l'installation d'un APK signé
/// d'une autre clé, qu'Android refuse tant que l'ancienne application n'est pas
/// désinstallée. Le fichier, partagé où l'on veut (Drive, e-mail, dossier
/// Téléchargements…), permet de les retrouver.
///
/// Lecture prudente : le fichier vient de l'extérieur. Sa taille est bornée, il
/// doit se présenter comme une sauvegarde de cette application, et chaque entrée
/// passe par les mêmes règles que la saisie (LocalPresetList, VoiceTextList).
/// </summary>
public sealed class FavoritesBackup
{
    /// <summary>Le nom que porte le fichier pour être reconnu.</summary>
    public const string Kind = "soundreborn-remote-favorites";

    /// <summary>Au-delà, ce n'est pas une sauvegarde de cette application : on ne lit pas.</summary>
    public const int MaxBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [JsonPropertyName("kind")] public string? FileKind { get; set; }

    [JsonPropertyName("version")] public int Version { get; set; }

    [JsonPropertyName("savedAt")] public DateTimeOffset? SavedAt { get; set; }

    [JsonPropertyName("localPresets")] public List<LocalPreset?>? LocalPresets { get; set; }

    [JsonPropertyName("voiceTexts")] public List<string?>? VoiceTexts { get; set; }

    /// <summary>Les réglages de l'application ; absents d'un fichier de version 1.</summary>
    [JsonPropertyName("settings")] public AppSettingsBackup? Settings { get; set; }

    /// <summary>Le contenu du fichier, lisible par un humain.</summary>
    public static string Serialize(IEnumerable<LocalPreset> presets, IEnumerable<string> texts, DateTimeOffset now, AppSettingsBackup? settings = null)
        => JsonSerializer.Serialize(
            new FavoritesBackup
            {
                FileKind = Kind,
                Version = settings is null ? 1 : 2,
                SavedAt = now,
                LocalPresets = presets.Select(p => (LocalPreset?)p).ToList(),
                VoiceTexts = texts.Select(t => (string?)t).ToList(),
                Settings = settings,
            },
            Options);

    /// <summary>
    /// Lit un fichier de sauvegarde. Null quand ce n'en est pas un : trop gros,
    /// illisible, ou sans la marque de cette application. Les listes rendues sont
    /// nettoyées et bornées comme à la saisie ; les réglages, contrôlés un à un.
    /// </summary>
    public static FavoritesContent? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBytes)
        {
            return null;
        }

        FavoritesBackup? backup;

        try
        {
            backup = JsonSerializer.Deserialize<FavoritesBackup>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (backup is null || !string.Equals(backup.FileKind, Kind, StringComparison.Ordinal))
        {
            return null;
        }

        return new FavoritesContent(
            LocalPresetList.Sanitize(backup.LocalPresets ?? new List<LocalPreset?>()),
            VoiceTextList.Sanitize(backup.VoiceTexts ?? new List<string?>()),
            backup.Settings?.Sanitized());
    }
}

/// <summary>Ce qu'un fichier de sauvegarde apporte, déjà nettoyé.</summary>
public sealed record FavoritesContent(List<LocalPreset> Presets, List<string> Texts, AppSettingsBackup? Settings);

/// <summary>
/// Les réglages de l'application dans le fichier de sauvegarde. Chaque champ est
/// facultatif : un réglage absent ou illisible est simplement laissé tel quel.
///
/// Langue et thème sont gardés sous leur nom (« French », « Dark ») : c'est
/// l'application qui les reconnaît, ce modèle ne fait que les borner.
/// </summary>
public sealed class AppSettingsBackup
{
    /// <summary>Au-delà, la liste des enceintes n'est pas celle d'une maison.</summary>
    public const int MaxSpeakers = 50;

    [JsonPropertyName("language")] public string? Language { get; set; }

    [JsonPropertyName("theme")] public string? Theme { get; set; }

    /// <summary>« Vérifier automatiquement » les mises à jour (application et agent).</summary>
    [JsonPropertyName("autoCheckUpdates")] public bool? AutoCheckUpdates { get; set; }

    /// <summary>Volume des messages vocaux, 0 à 100 (0 : celui de l'enceinte).</summary>
    [JsonPropertyName("voiceVolume")] public int? VoiceVolume { get; set; }

    [JsonPropertyName("speakers")] public List<BackupSpeaker?>? Speakers { get; set; }

    /// <summary>L'adresse de l'enceinte choisie au moment de la sauvegarde.</summary>
    [JsonPropertyName("selectedSpeaker")] public string? SelectedSpeaker { get; set; }

    /// <summary>Une copie dont chaque champ respecte les bornes ; les entrées invalides sont écartées.</summary>
    public AppSettingsBackup Sanitized()
    {
        var speakers = new List<BackupSpeaker?>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var speaker in Speakers ?? new List<BackupSpeaker?>())
        {
            if (speaker?.Sanitized() is { } clean && seen.Add(clean.Host) && speakers.Count < MaxSpeakers)
            {
                speakers.Add(clean);
            }
        }

        return new AppSettingsBackup
        {
            Language = Word(Language),
            Theme = Word(Theme),
            AutoCheckUpdates = AutoCheckUpdates,
            VoiceVolume = VoiceVolume is int v ? Math.Clamp(v, 0, 100) : null,
            Speakers = speakers,
            SelectedSpeaker = BackupSpeaker.IsHost(SelectedSpeaker) ? SelectedSpeaker!.Trim() : null,
        };
    }

    /// <summary>Un nom de réglage court, en lettres seulement ; sinon rien.</summary>
    private static string? Word(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 32 && value.All(char.IsAsciiLetter) ? value : null;
}

/// <summary>Une enceinte connue, sans ce qui ne vaut qu'à un instant (version de l'agent).</summary>
public sealed class BackupSpeaker
{
    [JsonPropertyName("host")] public string Host { get; set; } = "";

    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("deviceId")] public string? DeviceId { get; set; }

    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("strPort")] public int? StrPort { get; set; }

    /// <summary>Une adresse IP ou un nom d'hôte plausible, sans espace ni schéma.</summary>
    public static bool IsHost(string? host)
        => !string.IsNullOrWhiteSpace(host) && host.Trim().Length <= 253 &&
           host.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':' or '[' or ']');

    public BackupSpeaker? Sanitized()
    {
        if (!IsHost(Host))
        {
            return null;
        }

        return new BackupSpeaker
        {
            Host = Host.Trim(),
            Name = Short(Name, 100),
            DeviceId = Short(DeviceId, 64),
            Type = Short(Type, 64),
            StrPort = StrPort is >= 1 and <= 65535 ? StrPort : null,
        };
    }

    private static string? Short(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];
}

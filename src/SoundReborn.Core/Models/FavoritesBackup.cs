using System.Text.Json;
using System.Text.Json.Serialization;
using SoundReborn.Core.Voice;

namespace SoundReborn.Core.Models;

/// <summary>
/// Le fichier de sauvegarde de ce qui ne vit que dans le téléphone : les
/// présélections « Local » et les textes gardés du message vocal.
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

    /// <summary>Le contenu du fichier, lisible par un humain.</summary>
    public static string Serialize(IEnumerable<LocalPreset> presets, IEnumerable<string> texts, DateTimeOffset now)
        => JsonSerializer.Serialize(
            new FavoritesBackup
            {
                FileKind = Kind,
                Version = 1,
                SavedAt = now,
                LocalPresets = presets.Select(p => (LocalPreset?)p).ToList(),
                VoiceTexts = texts.Select(t => (string?)t).ToList(),
            },
            Options);

    /// <summary>
    /// Lit un fichier de sauvegarde. Null quand ce n'en est pas un : trop gros,
    /// illisible, ou sans la marque de cette application. Les listes rendues sont
    /// nettoyées et bornées comme à la saisie.
    /// </summary>
    public static (List<LocalPreset> Presets, List<string> Texts)? Parse(string? json)
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

        return (
            LocalPresetList.Sanitize(backup.LocalPresets ?? new List<LocalPreset?>()),
            VoiceTextList.Sanitize(backup.VoiceTexts ?? new List<string?>()));
    }
}

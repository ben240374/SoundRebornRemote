using System.Text.Json;
using System.Text.Json.Nodes;

namespace SoundReborn.Core.Models;

/// <summary>
/// La sauvegarde unique de l'application : dans un seul fichier, ce qui vit dans le
/// téléphone (<see cref="FavoritesBackup"/> : présélections « Local », textes,
/// réglages) et ce que l'agent STR garde pour l'enceinte sélectionnée
/// (<see cref="SpeakerBackup"/>). Chaque partie garde sa forme d'origine.
///
/// La lecture reconnaît aussi les fichiers des versions précédentes, qui n'avaient
/// qu'une des deux parties : une sauvegarde des favoris, ou une sauvegarde
/// d'enceinte.
/// </summary>
public static class FullBackup
{
    /// <summary>Le nom que porte le fichier pour être reconnu.</summary>
    public const string Kind = "soundreborn-remote-backup";

    /// <summary>Les deux parties tiennent chacune dans un mégaoctet.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Le fichier : la partie application (le texte de <see cref="FavoritesBackup.Serialize"/>)
    /// et, s'il y en a une, la partie enceinte.
    /// </summary>
    public static string Serialize(string appJson, SpeakerBackup? speaker, DateTimeOffset now)
    {
        var root = new JsonObject
        {
            ["kind"] = Kind,
            ["version"] = 1,
            ["savedAt"] = now.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["app"] = JsonNode.Parse(appJson),
            ["speaker"] = speaker is null ? null : JsonNode.Parse(speaker.Serialize()),
        };

        return root.ToJsonString(Indented);
    }

    /// <summary>
    /// Lit une sauvegarde, nouvelle ou ancienne. Null quand ce n'en est pas une :
    /// trop grosse, illisible, sans la marque de cette application, ou vide.
    /// </summary>
    public static FullBackupContent? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBytes)
        {
            return null;
        }

        JsonObject? root;

        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        var kind = root?["kind"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        FavoritesContent? app = null;
        SpeakerBackup? speaker = null;
        DateTimeOffset? savedAt = null;

        switch (kind)
        {
            case Kind:
                app = root!["app"] is JsonObject appNode ? FavoritesBackup.Parse(appNode.ToJsonString()) : null;
                speaker = root["speaker"] is JsonObject speakerNode ? SpeakerBackup.Parse(speakerNode.ToJsonString()) : null;
                savedAt = root["savedAt"] is JsonValue at && at.TryGetValue<string>(out var stamp) &&
                          DateTimeOffset.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture,
                              System.Globalization.DateTimeStyles.RoundtripKind, out var when)
                    ? when
                    : null;
                break;
            case FavoritesBackup.Kind:
                app = FavoritesBackup.Parse(json);
                savedAt = root!["savedAt"] is JsonValue fat && fat.TryGetValue<string>(out var fstamp) &&
                          DateTimeOffset.TryParse(fstamp, System.Globalization.CultureInfo.InvariantCulture,
                              System.Globalization.DateTimeStyles.RoundtripKind, out var fwhen)
                    ? fwhen
                    : null;
                break;
            case SpeakerBackup.Kind:
                speaker = SpeakerBackup.Parse(json);
                savedAt = speaker?.SavedAt;
                break;
            default:
                return null;
        }

        return app is null && speaker is null ? null : new FullBackupContent(app, speaker, savedAt);
    }
}

/// <summary>Ce qu'une sauvegarde apporte : la partie application, la partie enceinte, ou les deux.</summary>
public sealed record FullBackupContent(FavoritesContent? App, SpeakerBackup? Speaker, DateTimeOffset? SavedAt);

using System.Text.Json;
using System.Text.Json.Nodes;

namespace SoundReborn.Core.Models;

/// <summary>
/// Le fichier de sauvegarde d'une ENCEINTE : ce que son agent STR garde pour elle.
///
/// - les douze touches, telles que GET /api/presets les rend (tous les champs, y
///   compris le compte et la playlist d'une touche Spotify) ;
/// - les réveils, sans la révision ni le bloc d'état, qui ne valent que sur
///   l'enceinte d'origine au moment de la lecture ;
/// - le volume de départ et le volume maximum ;
/// - les actions des touches pouce et AUX de la télécommande ;
/// - les réglages de l'écran que l'enceinte sait faire (titre, horloge, messages,
///   avis, écran d'accueil).
///
/// Ni l'adresse IP, ni l'identifiant de l'enceinte : le fichier sert aussi à
/// recopier ces réglages sur une autre enceinte. Le groupe permanent n'y est pas :
/// il désigne d'autres enceintes par leur adresse, qui peut changer.
///
/// Lecture prudente : le fichier vient de l'extérieur. Sa taille est bornée, il doit
/// se présenter comme une sauvegarde d'enceinte de cette application, et seules les
/// touches 1 à 12 et huit réveils au plus en sont retenus. L'agent contrôle le reste
/// à l'enregistrement.
/// </summary>
public sealed class SpeakerBackup
{
    /// <summary>Le nom que porte le fichier pour être reconnu.</summary>
    public const string Kind = "soundreborn-remote-speaker";

    /// <summary>Au-delà, ce n'est pas une sauvegarde de cette application : on ne lit pas.</summary>
    public const int MaxBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Touches d'une rangée, et des deux rangées (la seconde : la même touche pressée deux fois).</summary>
    private const int PerRow = 6, Total = 12;

    /// <summary>Le nom de l'enceinte sauvegardée, pour la question avant restauration.</summary>
    public string SpeakerName { get; init; } = "";

    public string Model { get; init; } = "";

    public string AgentVersion { get; init; } = "";

    public DateTimeOffset? SavedAt { get; init; }

    /// <summary>Les touches garnies, une par emplacement (1 à 12), dans l'ordre.</summary>
    public JsonArray Presets { get; init; } = new();

    /// <summary>Le document des réveils sans « rev » ni « status », ou null s'il n'a pas été lu.</summary>
    public JsonObject? Alarms { get; init; }

    /// <summary>Le volume de départ (0 = désactivé), ou null si l'agent ne le gère pas.</summary>
    public int? StartVolume { get; init; }

    /// <summary>Les actions des touches de la télécommande, ou null si l'agent ne les connaît pas.</summary>
    public StrKeyActions? KeyActions { get; init; }

    /// <summary>Le volume maximum (0 = aucun), ou null si l'agent ne le gère pas.</summary>
    public int? MaxVolume { get; init; }

    /// <summary>
    /// Les réglages de l'écran, par nom (<see cref="StrDisplaySetting.Kinds"/>) :
    /// <c>{"enabled":bool}</c>, plus <c>"mode"</c> pour le titre. Null : non lus.
    /// </summary>
    public JsonObject? Display { get; init; }

    public int DisplayCount => Display?.Count ?? 0;

    public int PresetCount => Presets.Count;

    public int AlarmCount => Alarms?["alarms"] is JsonArray alarms ? alarms.Count : 0;

    /// <summary>Le numéro de touche d'une entrée du magasin, ou 0.</summary>
    public static int SlotOf(JsonNode? preset)
        => preset is JsonObject o && o["slot"] is JsonValue v && v.TryGetValue<int>(out var slot) ? slot : 0;

    /// <summary>Le contenu du fichier, lisible par un humain.</summary>
    public string Serialize()
    {
        var root = new JsonObject
        {
            ["kind"] = Kind,
            ["version"] = 2,
            ["savedAt"] = SavedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["speaker"] = new JsonObject
            {
                ["name"] = SpeakerName,
                ["model"] = Model,
                ["agentVersion"] = AgentVersion,
            },
            ["presets"] = Presets.DeepClone(),
            ["alarms"] = Alarms?.DeepClone(),
            ["startVolume"] = StartVolume,
            ["keyActions"] = KeyActions is null ? null : JsonNode.Parse(KeyActions.ToPutBody()),
            ["maxVolume"] = MaxVolume,
            ["display"] = Display?.DeepClone(),
        };

        return root.ToJsonString(Indented);
    }

    /// <summary>
    /// Ce qui part dans le fichier : les touches garnies (1 à 12), le document des
    /// réveils privé de sa révision et de son état.
    /// </summary>
    public static SpeakerBackup Create(
        string speakerName,
        string model,
        string agentVersion,
        DateTimeOffset now,
        JsonArray presets,
        StrAlarmDocument? alarms,
        int? startVolume,
        StrKeyActions? keyActions,
        int? maxVolume = null,
        IEnumerable<StrDisplaySetting>? display = null)
    {
        JsonObject? alarmNode = null;

        if (alarms is not null && JsonNode.Parse(alarms.ToPutBody()) is JsonObject node)
        {
            node.Remove("rev");
            node.Remove("status");
            alarmNode = node;
        }

        return new SpeakerBackup
        {
            SpeakerName = speakerName,
            Model = model,
            AgentVersion = agentVersion,
            SavedAt = now,
            Presets = CleanPresets(presets),
            Alarms = alarmNode,
            StartVolume = startVolume is int v ? Math.Clamp(v, 0, 100) : null,
            KeyActions = keyActions,
            MaxVolume = maxVolume is int m ? Math.Clamp(m, 0, 100) : null,
            Display = DisplayNode(display),
        };
    }

    /// <summary>Les réglages d'écran que l'enceinte sait faire, sous la forme du fichier.</summary>
    private static JsonObject? DisplayNode(IEnumerable<StrDisplaySetting>? settings)
    {
        if (settings is null)
        {
            return null;
        }

        var node = new JsonObject();

        foreach (var setting in settings.Where(s => s.Supported && StrDisplaySetting.Kinds.Contains(s.Kind)))
        {
            var entry = new JsonObject { ["enabled"] = setting.Enabled };

            if (setting.Kind == StrDisplaySetting.Track)
            {
                entry["mode"] = setting.Mode;
            }

            node[setting.Kind] = entry;
        }

        return node.Count == 0 ? null : node;
    }

    /// <summary>Les réglages d'écran d'un fichier : noms connus, booléens, mode reconnu.</summary>
    private static JsonObject? CleanDisplay(JsonObject? display)
    {
        if (display is null)
        {
            return null;
        }

        var node = new JsonObject();

        foreach (var kind in StrDisplaySetting.Kinds)
        {
            if (display[kind] is not JsonObject entry ||
                entry["enabled"] is not JsonValue enabledValue ||
                !enabledValue.TryGetValue<bool>(out var enabled))
            {
                continue;
            }

            var clean = new JsonObject { ["enabled"] = enabled };

            if (kind == StrDisplaySetting.Track)
            {
                var mode = Text(entry, "mode");
                clean["mode"] = mode is StrDisplaySetting.ModeArtist or StrDisplaySetting.ModeTitle or StrDisplaySetting.ModeBoth
                    ? mode
                    : StrDisplaySetting.ModeBoth;
            }

            node[kind] = clean;
        }

        return node.Count == 0 ? null : node;
    }

    /// <summary>
    /// Lit un fichier de sauvegarde d'enceinte. Null quand ce n'en est pas un : trop
    /// gros, illisible, ou sans la marque de cette application.
    /// </summary>
    public static SpeakerBackup? Parse(string? json)
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

        if (root is null || Text(root, "kind") != Kind)
        {
            return null;
        }

        var speaker = root["speaker"] as JsonObject;

        JsonObject? alarms = null;

        if (root["alarms"] is JsonObject alarmRoot && alarmRoot["alarms"] is JsonArray list)
        {
            alarms = (JsonObject)alarmRoot.DeepClone();
            alarms.Remove("rev");
            alarms.Remove("status");

            var kept = new JsonArray();

            foreach (var alarm in list.OfType<JsonObject>().Take(StrAlarmDocument.MaxAlarms))
            {
                kept.Add(alarm.DeepClone());
            }

            alarms["alarms"] = kept;
        }

        StrKeyActions? keyActions = null;

        if (root["keyActions"] is JsonObject keys)
        {
            try
            {
                keyActions = StrKeyActions.Parse(keys.ToJsonString());
            }
            catch (JsonException)
            {
                keyActions = null;
            }
        }

        int? startVolume = root["startVolume"] is JsonValue sv && sv.TryGetValue<int>(out var volume)
            ? Math.Clamp(volume, 0, 100)
            : null;

        int? maxVolume = root["maxVolume"] is JsonValue mv && mv.TryGetValue<int>(out var ceiling)
            ? Math.Clamp(ceiling, 0, 100)
            : null;

        DateTimeOffset? savedAt = Text(root, "savedAt") is { } at &&
                                  DateTimeOffset.TryParse(at, System.Globalization.CultureInfo.InvariantCulture,
                                      System.Globalization.DateTimeStyles.RoundtripKind, out var when)
            ? when
            : null;

        return new SpeakerBackup
        {
            SpeakerName = Text(speaker, "name") ?? "",
            Model = Text(speaker, "model") ?? "",
            AgentVersion = Text(speaker, "agentVersion") ?? "",
            SavedAt = savedAt,
            Presets = CleanPresets(root["presets"] as JsonArray ?? new JsonArray()),
            Alarms = alarms,
            StartVolume = startVolume,
            KeyActions = keyActions,
            MaxVolume = maxVolume,
            Display = CleanDisplay(root["display"] as JsonObject),
        };
    }

    /// <summary>
    /// Le jeu à écrire sur une enceinte : les touches de la sauvegarde, sans celles
    /// de la seconde rangée quand l'enceinte ne l'a pas.
    /// </summary>
    public JsonArray PresetsFor(bool secondRow)
    {
        var set = new JsonArray();

        foreach (var preset in Presets)
        {
            var slot = SlotOf(preset);

            if (slot >= 1 && (secondRow ? slot <= Total : slot <= PerRow))
            {
                set.Add(preset!.DeepClone());
            }
        }

        return set;
    }

    /// <summary>Les touches 1 à 12, une par emplacement (la première vue l'emporte), en ordre.</summary>
    private static JsonArray CleanPresets(JsonArray presets)
    {
        var bySlot = new SortedDictionary<int, JsonObject>();

        foreach (var preset in presets.OfType<JsonObject>())
        {
            var slot = SlotOf(preset);

            if (slot is >= 1 and <= Total && !bySlot.ContainsKey(slot))
            {
                bySlot[slot] = (JsonObject)preset.DeepClone();
            }
        }

        var clean = new JsonArray();

        foreach (var preset in bySlot.Values)
        {
            clean.Add(preset);
        }

        return clean;
    }

    private static string? Text(JsonObject? node, string name)
        => node?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

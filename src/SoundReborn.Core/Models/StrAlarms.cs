using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SoundReborn.Core.Models;

/// <summary>
/// Le document des réveils d'une enceinte (GET/PUT /api/alarms).
///
/// L'agent remplace le document EN ENTIER à chaque enregistrement. On garde donc
/// l'arbre JSON tel qu'il a été lu, et on n'y change que les champs que l'écran
/// modifie : un champ que l'application ne connaît pas — le nom d'un réveil,
/// une heure d'arrêt fixée ailleurs, les jours sans réveil, un champ d'une future
/// version — repart tel quel au lieu d'être effacé.
/// </summary>
public sealed class StrAlarmDocument
{
    /// <summary>L'agent n'accepte pas plus de huit réveils par enceinte.</summary>
    public const int MaxAlarms = 8;

    private readonly JsonObject _root;

    private StrAlarmDocument(JsonObject root)
    {
        _root = root;

        if (_root["alarms"] is not JsonArray)
        {
            _root["alarms"] = new JsonArray();
        }
    }

    /// <summary>Lit la réponse de l'agent. Lève <see cref="JsonException"/> si ce n'est pas un objet JSON.</summary>
    public static StrAlarmDocument Parse(string json)
    {
        var node = JsonNode.Parse(json);

        if (node is not JsonObject root)
        {
            throw new JsonException("Le document des réveils n'est pas un objet JSON.");
        }

        return new StrAlarmDocument(root);
    }

    /// <summary>
    /// Révision lue (agent 1.6.3 et suivants). Null sur un agent plus ancien, qui ne
    /// la donne pas. Zéro est une vraie révision.
    /// </summary>
    public long? Rev => _root["rev"] is JsonValue value && value.TryGetValue<long>(out var rev) ? rev : null;

    /// <summary>Fuseau IANA de l'enceinte. Vide veut dire UTC.</summary>
    public string Zone
    {
        get => Text(_root, "zone") ?? "";
        set => _root["zone"] = value ?? "";
    }

    public IReadOnlyList<StrAlarm> Alarms => AlarmArray
        .OfType<JsonObject>()
        .Select(node => new StrAlarm(node))
        .ToList();

    public bool CanAdd => AlarmArray.Count < MaxAlarms;

    /// <summary>Faux quand l'horloge de l'enceinte n'est pas fiable : rien ne sonnera.</summary>
    public bool ClockTrusted => Flag(Status, "clockTrusted") ?? true;

    /// <summary>Faux quand le fuseau enregistré n'est plus reconnu : rien n'est programmé.</summary>
    public bool ZoneResolved => Flag(Status, "zoneResolved") ?? true;

    /// <summary>Le prochain réveil selon l'enceinte elle-même, ou null.</summary>
    public DateTimeOffset? NextFire =>
        Text(Status, "nextFire") is { Length: > 0 } text &&
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)
            ? when
            : null;

    public string? NextAlarmId => Text(Status, "nextAlarmId");

    public StrAlarm? Find(string id) => Alarms.FirstOrDefault(a => a.Id == id);

    /// <summary>Ajoute un réveil sans identifiant : l'agent lui en donne un à l'enregistrement.</summary>
    public StrAlarm Add()
    {
        var node = new JsonObject();
        AlarmArray.Add(node);
        return new StrAlarm(node);
    }

    public bool Remove(string id)
    {
        var array = AlarmArray;

        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is JsonObject node && Text(node, "id") == id)
            {
                array.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>Copie indépendante, sur laquelle on prépare une modification.</summary>
    public StrAlarmDocument Clone() => Parse(_root.ToJsonString());

    /// <summary>
    /// Le corps du PUT : le document sans son bloc d'état, que l'agent ignore de
    /// toute façon. La révision lue repart avec lui : si une autre application a
    /// enregistré entre-temps, l'agent refuse au lieu d'écraser son travail.
    /// </summary>
    public string ToPutBody()
    {
        var copy = (JsonObject)_root.DeepClone();
        copy.Remove("status");
        return copy.ToJsonString();
    }

    private JsonArray AlarmArray => (JsonArray)_root["alarms"]!;

    private JsonObject? Status => _root["status"] as JsonObject;

    internal static string? Text(JsonObject? node, string name)
        => node?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal static bool? Flag(JsonObject? node, string name)
        => node?[name] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    internal static int Number(JsonObject node, string name)
    {
        if (node[name] is not JsonValue value)
        {
            return 0;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return value.TryGetValue<double>(out var real) ? (int)real : 0;
    }
}

/// <summary>Un réveil, lu et modifié directement dans l'arbre JSON du document.</summary>
public sealed class StrAlarm
{
    /// <summary>Durée de répétition d'un réveil qui n'en précise pas.</summary>
    public const int DefaultSnooze = 9;

    private readonly JsonObject _node;

    internal StrAlarm(JsonObject node)
    {
        _node = node;
    }

    /// <summary>Vide pour un réveil pas encore enregistré.</summary>
    public string Id => StrAlarmDocument.Text(_node, "id") ?? "";

    public bool IsNew => string.IsNullOrWhiteSpace(Id);

    public string? Name => StrAlarmDocument.Text(_node, "name");

    public bool Enabled
    {
        get => StrAlarmDocument.Flag(_node, "enabled") ?? false;
        set => _node["enabled"] = value;
    }

    public int Hour
    {
        get => StrAlarmDocument.Number(_node, "hour");
        set => _node["hour"] = value;
    }

    public int Minute
    {
        get => StrAlarmDocument.Number(_node, "minute");
        set => _node["minute"] = value;
    }

    /// <summary>Jours de semaine, 0 = dimanche … 6 = samedi, comme chez l'agent.</summary>
    public IReadOnlyList<int> Days
    {
        get => _node["days"] is JsonArray days
            ? days.OfType<JsonValue>()
                .Select(d => d.TryGetValue<int>(out var day) ? day : -1)
                .Where(d => d is >= 0 and <= 6)
                .Distinct()
                .OrderBy(d => d)
                .ToList()
            : Array.Empty<int>();
        set
        {
            var array = new JsonArray();

            foreach (var day in value.Where(d => d is >= 0 and <= 6).Distinct().OrderBy(d => d))
            {
                array.Add(day);
            }

            _node["days"] = array;
        }
    }

    /// <summary>Touche jouée, 1 à 12 (7 à 12 : touche pressée deux fois).</summary>
    public int Slot
    {
        get => StrAlarmDocument.Number(_node, "slot");
        set => _node["slot"] = value;
    }

    /// <summary>1 à 100 ; 0 laisse l'enceinte à son volume.</summary>
    public int Volume
    {
        get => StrAlarmDocument.Number(_node, "volume");
        set => _node["volume"] = value;
    }

    /// <summary>Arrêt automatique, en minutes après le début ; 0 = jamais.</summary>
    public int AutoOff
    {
        get => StrAlarmDocument.Number(_node, "autoOff");
        set => _node["autoOff"] = value;
    }

    /// <summary>Heure d'arrêt « HH:MM » fixée ailleurs. Quand elle existe, l'agent ignore <see cref="AutoOff"/>.</summary>
    public string? OffAt => StrAlarmDocument.Text(_node, "offAt") is { Length: > 0 } text ? text : null;

    /// <summary>5, 9, 10 ou 15 minutes ; 0 = 9.</summary>
    public int Snooze
    {
        get => StrAlarmDocument.Number(_node, "snooze");
        set => _node["snooze"] = value;
    }

    public int SnoozeMinutes => Snooze > 0 ? Snooze : DefaultSnooze;
}

/// <summary>Issue d'un PUT /api/alarms.</summary>
public sealed class StrAlarmSaveResult
{
    /// <summary>Le document tel que l'agent l'a enregistré, nouvelle révision comprise.</summary>
    public StrAlarmDocument? Saved { get; init; }

    /// <summary>
    /// 409 : une autre application a enregistré entre-temps. C'est le document
    /// actuel, sur lequel il faut refaire la modification.
    /// </summary>
    public StrAlarmDocument? Current { get; init; }

    public bool IsConflict => Current is not null;
}

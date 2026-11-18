using System.Globalization;
using System.Text.Json;

namespace SoundReborn.Core.Models;

/// <summary>
/// Réponse de POST /api/box/preset-copy (agent 1.6.5 et suivants) : la copie des
/// touches de cette enceinte vers d'autres enceintes STR, une ligne par cible.
///
/// La documentation de l'agent nomme les champs sans en fixer toujours la forme
/// (« copied » peut être un nombre ou la liste des touches). La lecture accepte
/// donc les deux, et ne lève jamais pour un champ absent ou inattendu.
/// </summary>
public sealed class StrPresetCopyResult
{
    /// <summary>Touches réglées sur l'enceinte source.</summary>
    public int Keys { get; private init; }

    /// <summary>Cibles traitées et cibles prévues.</summary>
    public int Done { get; private init; }

    public int Total { get; private init; }

    public IReadOnlyList<StrPresetCopyTarget> Results { get; private init; } = Array.Empty<StrPresetCopyTarget>();

    /// <summary>Lève <see cref="JsonException"/> seulement si le texte n'est pas du JSON.</summary>
    public static StrPresetCopyResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new StrPresetCopyResult();
        }

        var results = new List<StrPresetCopyTarget>();

        if (root.TryGetProperty("results", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    results.Add(StrPresetCopyTarget.From(item));
                }
            }
        }

        return new StrPresetCopyResult
        {
            Keys = Count(root, "keys"),
            Done = Count(root, "done"),
            Total = Count(root, "total"),
            Results = results,
        };
    }

    /// <summary>Un nombre, un nombre écrit en texte, ou la longueur d'une liste.</summary>
    internal static int Count(JsonElement node, string name)
    {
        if (!node.TryGetProperty(name, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out var n) ? Math.Max(0, n) : 0,
            JsonValueKind.Array => value.GetArrayLength(),
            JsonValueKind.String => int.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0,
            JsonValueKind.True => 1,
            _ => 0,
        };
    }

    /// <summary>Vrai, un nombre non nul, une liste ou un texte non vides.</summary>
    internal static bool Flag(JsonElement node, string name)
    {
        if (!node.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => value.TryGetDouble(out var d) && d != 0,
            JsonValueKind.Array => value.GetArrayLength() > 0,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()) &&
                                    !string.Equals(value.GetString(), "false", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    internal static string? Text(JsonElement node, string name)
        => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>Le sort d'une cible de la copie.</summary>
public sealed class StrPresetCopyTarget
{
    public const string AlreadyOnSlot = "already-on-slot";
    public const string Unreachable = "unreachable";
    public const string Refused = "refused";
    public const string UnknownSpeaker = "unknown-speaker";

    public string Name { get; private init; } = "";

    public string Ip { get; private init; } = "";

    public bool Ok { get; private init; }

    /// <summary>Touches écrites sur la cible.</summary>
    public int Copied { get; private init; }

    /// <summary>La cible n'a qu'une rangée : la seconde n'a pas été copiée.</summary>
    public bool Row1Only { get; private init; }

    public int NotCopied { get; private init; }

    /// <summary>Une touche Spotify est partie avec la copie : le compte Spotify a été transmis.</summary>
    public bool Spotify { get; private init; }

    /// <summary>already-on-slot, unreachable, refused, unknown-speaker ; null quand tout va bien.</summary>
    public string? Code { get; private init; }

    /// <summary>Pour already-on-slot : la station déjà présente sur la cible, et sa touche.</summary>
    public string? ConflictName { get; private init; }

    public int ConflictSlot { get; private init; }

    /// <summary>Courte phrase anglaise de l'agent, pour les cas sans code connu.</summary>
    public string? Error { get; private init; }

    internal static StrPresetCopyTarget From(JsonElement item)
    {
        string? conflictName = null;
        var conflictSlot = 0;

        if (item.TryGetProperty("conflict", out var conflict) && conflict.ValueKind == JsonValueKind.Object)
        {
            conflictName = StrPresetCopyResult.Text(conflict, "name");
            conflictSlot = StrPresetCopyResult.Count(conflict, "slot");
        }

        return new StrPresetCopyTarget
        {
            Name = StrPresetCopyResult.Text(item, "name") ?? "",
            Ip = StrPresetCopyResult.Text(item, "ip") ?? "",
            Ok = StrPresetCopyResult.Flag(item, "ok"),
            Copied = StrPresetCopyResult.Count(item, "copied"),
            Row1Only = StrPresetCopyResult.Flag(item, "row1Only"),
            NotCopied = StrPresetCopyResult.Count(item, "notCopied"),
            Spotify = StrPresetCopyResult.Flag(item, "spotify"),
            Code = StrPresetCopyResult.Text(item, "code") is { Length: > 0 } code ? code.ToLowerInvariant() : null,
            ConflictName = conflictName,
            ConflictSlot = conflictSlot,
            Error = StrPresetCopyResult.Text(item, "error"),
        };
    }
}

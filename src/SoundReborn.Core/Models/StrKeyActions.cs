using System.Text.Json;
using System.Text.Json.Nodes;

namespace SoundReborn.Core.Models;

/// <summary>
/// Les actions des touches de la télécommande d'une enceinte (GET/PUT /api/keyactions) :
/// <c>{"keys":{"thumbsUp":"sleep","aux":"snooze"}}</c>.
///
/// L'agent remplace le document EN ENTIER à chaque enregistrement. On garde donc
/// toutes les touches lues, et l'écran ne change que celle que l'utilisateur a
/// touchée : une minuterie mise sur un pouce par l'application STR sur ordinateur
/// repart telle quelle.
///
/// Ce que l'agent accepte : « snooze » (répéter le réveil) sur les pouces et sur
/// AUX ; toutes les autres actions sur les pouces seulement. Une touche qui porte
/// un groupe enregistré est refusée (409, texte en clair).
/// </summary>
public sealed class StrKeyActions
{
    public const string Aux = "aux";
    public const string ThumbsUp = "thumbsUp";
    public const string ThumbsDown = "thumbsDown";

    /// <summary>Répète un réveil qui sonne ; ne fait rien d'autre.</summary>
    public const string Snooze = "snooze";

    /// <summary>Les touches qui peuvent porter une action, dans l'ordre de l'écran.</summary>
    public static readonly IReadOnlyList<string> Keys = new[] { Aux, ThumbsUp, ThumbsDown };

    private readonly Dictionary<string, string> _keys;

    private StrKeyActions(Dictionary<string, string> keys) => _keys = keys;

    public static StrKeyActions Empty() => new(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// Lit la réponse de l'agent. Une entrée qui n'est pas du texte est ignorée ;
    /// lève <see cref="JsonException"/> si la réponse n'est pas un objet JSON.
    /// </summary>
    public static StrKeyActions Parse(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root)
        {
            throw new JsonException("Le document des actions de touches n'est pas un objet JSON.");
        }

        var keys = new Dictionary<string, string>(StringComparer.Ordinal);

        if (root["keys"] is JsonObject map)
        {
            foreach (var (key, value) in map)
            {
                if (value is JsonValue v && v.TryGetValue<string>(out var action) && !string.IsNullOrWhiteSpace(action))
                {
                    keys[key] = action.Trim();
                }
            }
        }

        return new StrKeyActions(keys);
    }

    /// <summary>L'action de la touche, ou null si elle n'en porte pas.</summary>
    public string? ActionOf(string key) => _keys.TryGetValue(key, out var action) ? action : null;

    /// <summary>Vrai si la touche répète le réveil (l'agent écrit les noms dans n'importe quelle casse).</summary>
    public bool IsSnooze(string key) => string.Equals(ActionOf(key), Snooze, StringComparison.OrdinalIgnoreCase);

    /// <summary>Met une action sur la touche, ou l'en retire (null).</summary>
    public void Set(string key, string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            _keys.Remove(key);
        }
        else
        {
            _keys[key] = action;
        }
    }

    public StrKeyActions Clone() => new(new Dictionary<string, string>(_keys, StringComparer.Ordinal));

    /// <summary>Le corps du PUT : toutes les touches connues, celles que l'écran ne montre pas comprises.</summary>
    public string ToPutBody()
    {
        var map = new JsonObject();

        foreach (var (key, action) in _keys.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            map[key] = action;
        }

        return new JsonObject { ["keys"] = map }.ToJsonString();
    }
}

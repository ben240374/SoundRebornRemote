using System.Text.Json;

namespace SoundReborn.Core.Voice;

/// <summary>
/// Les textes gardés pour le message vocal (« À table ! », « On part dans cinq
/// minutes »…) : lecture tolérante, écriture, et les règles qu'un texte doit
/// suivre. Sans état : l'application garde la liste dans ses préférences.
/// </summary>
public static class VoiceTextList
{
    /// <summary>Nombre de textes gardés au plus.</summary>
    public const int MaxCount = 10;

    /// <summary>
    /// Longueur d'un texte au plus. 200 caractères, comme l'application STR : la voix
    /// de Google lit un texte plus long moins sûrement sur certaines enceintes
    /// (docs/announce-tts.md de l'agent).
    /// </summary>
    public const int MaxLength = 200;

    /// <summary>
    /// Lit la liste enregistrée : un tableau JSON de chaînes. Les textes vides et
    /// les doublons (casse ignorée) sont écartés, chaque texte est nettoyé, et la
    /// liste coupée à <see cref="MaxCount"/>. Un texte illisible donne une liste
    /// vide, jamais une exception.
    /// </summary>
    public static List<string> Parse(string? json)
    {
        var result = new List<string>();

        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        List<string?>? raw;

        try
        {
            raw = JsonSerializer.Deserialize<List<string?>>(json);
        }
        catch (JsonException)
        {
            return result;
        }

        return Sanitize(raw ?? new List<string?>());
    }

    /// <summary>
    /// Nettoie une liste de textes venue d'ailleurs (préférences, sauvegarde) :
    /// vides et doublons écartés, coupée à <see cref="MaxCount"/>.
    /// </summary>
    public static List<string> Sanitize(IEnumerable<string?> texts)
    {
        var result = new List<string>();

        foreach (var entry in texts)
        {
            var text = Clean(entry);

            if (text.Length == 0 || Contains(result, text))
            {
                continue;
            }

            result.Add(text);

            if (result.Count == MaxCount)
            {
                break;
            }
        }

        return result;
    }

    public static string Serialize(IEnumerable<string> texts)
        => JsonSerializer.Serialize(texts.ToList());

    /// <summary>
    /// Espaces et retours à la ligne ramenés à un seul espace, coupé à
    /// <see cref="MaxLength"/> caractères. Vide quand il ne reste rien.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxLength ? collapsed : collapsed[..MaxLength].TrimEnd();
    }

    /// <summary>Vrai quand la liste a déjà ce texte, casse ignorée.</summary>
    public static bool Contains(IEnumerable<string> texts, string text)
        => texts.Any(t => string.Equals(t, text, StringComparison.OrdinalIgnoreCase));
}

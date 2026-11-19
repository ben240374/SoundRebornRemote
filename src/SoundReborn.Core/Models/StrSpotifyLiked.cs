using System.Text.Json;

namespace SoundReborn.Core.Models;

/// <summary>Réponse de POST /api/spotify/liked (agent 1.8.1 et suivants).</summary>
public sealed class StrLikedResult
{
    /// <summary>Vrai : le titre est dans les Titres likés ; faux : il en a été retiré.</summary>
    public bool Liked { get; private init; }

    public string? Uri { get; private init; }

    /// <summary>Le titre concerné, quand l'agent le donne.</summary>
    public string? Name { get; private init; }

    /// <summary>La même demande pour le même titre venait d'être faite : rien n'a été écrit deux fois.</summary>
    public bool Repeat { get; private init; }

    /// <summary>Ne lève jamais : une réponse vide ou illisible donne un résultat vide.</summary>
    public static StrLikedResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new StrLikedResult();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new StrLikedResult();
            }

            return new StrLikedResult
            {
                Liked = root.TryGetProperty("liked", out var liked) && liked.ValueKind == JsonValueKind.True,
                Uri = Text(root, "uri"),
                Name = Text(root, "name"),
                Repeat = root.TryGetProperty("repeat", out var repeat) && repeat.ValueKind == JsonValueKind.True,
            };
        }
        catch (JsonException)
        {
            return new StrLikedResult();
        }
    }

    /// <summary>
    /// La raison d'un refus (« not-playing », « rate-limited-429 »…), lue dans le
    /// corps {"error","reason"} de l'agent. Null quand ce n'est pas un refus lisible.
    /// Ne lève jamais.
    /// </summary>
    public static string? ReasonOf(Exception? ex)
    {
        if (ex is not SpeakerException { ResponseBody: { Length: > 0 } body })
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object && Text(document.RootElement, "reason") is { Length: > 0 } reason
                ? reason.ToLowerInvariant()
                : null;
        }
        catch (JsonException)
        {
            // 400 « bad-body » arrive parfois en texte brut.
            return null;
        }
    }

    private static string? Text(JsonElement node, string name)
        => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

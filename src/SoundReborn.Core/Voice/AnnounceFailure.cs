using System.Net;
using System.Text.Json;

namespace SoundReborn.Core.Voice;

/// <summary>
/// Lit un refus de POST /api/announce pour le dire en clair.
///
/// L'agent répond 502 avec {"error":"could not fetch announcement audio",
/// "detail":"…"} quand il n'a pas pu obtenir le son : la voix de Google pour un
/// texte (pas d'internet sur l'enceinte, Google qui limite les demandes, délai
/// dépassé), ou le message servi par le téléphone pour la voix. C'est souvent
/// passager : on propose de réessayer.
/// </summary>
public static class AnnounceFailure
{
    /// <summary>Longueur au plus du détail affiché : une ligne, pas un journal.</summary>
    private const int MaxDetailLength = 120;

    /// <summary>Vrai quand l'agent n'a pas pu obtenir le son de l'annonce (502).</summary>
    public static bool IsFetchFailure(Exception ex)
        => ex is SpeakerException { StatusCode: HttpStatusCode.BadGateway };

    /// <summary>Vrai quand l'agent ne connaît pas encore les annonces (404).</summary>
    public static bool IsUnsupported(Exception ex)
        => ex is SpeakerException { StatusCode: HttpStatusCode.NotFound };

    /// <summary>
    /// La raison donnée par l'agent (« detail », sinon « error », sinon le corps
    /// brut), sur une ligne et coupée. Null quand il n'en donne pas.
    /// </summary>
    public static string? Detail(Exception ex)
    {
        if (ex is not SpeakerException { ResponseBody: { } body } || string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        string? text = null;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                text = Field(document.RootElement, "detail") ?? Field(document.RootElement, "error");
            }
        }
        catch (JsonException)
        {
            text = body;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= MaxDetailLength ? line : line[..(MaxDetailLength - 1)] + "…";
    }

    private static string? Field(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

using System.Text.Json;

namespace SoundReborn.Core;

/// <summary>
/// La partie lisible par un programme d'un refus de l'agent STR.
///
/// L'agent accompagne ses refus d'un « code » stable : « already-on-slot » quand
/// une station est déjà sur une autre touche, « library-server-offline » quand le
/// serveur de médias est éteint. Le message HTTP seul — « a répondu 409 » — ne dit
/// rien à l'utilisateur ; le code permet d'afficher une phrase dans sa langue.
/// </summary>
public sealed class StrRefusal
{
    public const string AlreadyOnSlot = "already-on-slot";

    public const string LibraryServerOffline = "library-server-offline";

    private StrRefusal()
    {
    }

    /// <summary>Le code de l'agent, en minuscules. Jamais vide.</summary>
    public string Code { get; private init; } = "";

    /// <summary>Touche concernée (1 à 12) pour « already-on-slot », sinon 0.</summary>
    public int Slot { get; private init; }

    /// <summary>Nom de ce qui occupe la touche, quand l'agent le donne.</summary>
    public string? Name { get; private init; }

    /// <summary>Nom du serveur de médias, pour « library-server-offline ».</summary>
    public string? Server { get; private init; }

    /// <summary>
    /// Lit le refus porté par une exception. Null quand ce n'en est pas un — autre
    /// type d'erreur, corps vide, corps qui n'est pas du JSON, ou JSON sans code.
    /// Ne lève jamais : c'est appelé depuis l'affichage d'une erreur.
    /// </summary>
    public static StrRefusal? From(Exception? ex)
    {
        if (ex is not SpeakerException speaker || string.IsNullOrWhiteSpace(speaker.ResponseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(speaker.ResponseBody!);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var code = ReadText(root, "code");

            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            return new StrRefusal
            {
                Code = code!.Trim().ToLowerInvariant(),
                Slot = root.TryGetProperty("slot", out var slot) &&
                       slot.ValueKind == JsonValueKind.Number &&
                       slot.TryGetInt32(out var number)
                    ? number
                    : 0,
                Name = ReadText(root, "name"),
                Server = ReadText(root, "server"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadText(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

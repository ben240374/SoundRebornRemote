using SoundReborn.Core;
using SoundRebornRemote.App.ViewModels;

namespace SoundRebornRemote.App.Services;

/// <summary>
/// Les raccourcis de l'icône de l'application (appui long sur l'icône, dans le
/// lanceur d'Android) : « Tout mettre en veille » et les touches 1 à 3.
///
/// - « Tout mettre en veille » éteint chaque enceinte connue qui a l'agent STR, par
///   POST /api/box/power {"on":false} : une demande d'arrêt, qui ne rallume pas une
///   enceinte déjà en veille. Une enceinte sans agent n'est PAS touchée : la seule
///   commande du firmware est la touche POWER, une bascule qui allumerait une
///   enceinte éteinte. Le compte rendu le dit.
/// - « Touche n » lance la touche sur l'enceinte sélectionnée ; au démarrage de
///   l'application, après sa reconnexion.
///
/// Ce qui touche à Android (publication des raccourcis, réception) est dans
/// App.xaml.cs et MainActivity ; ici, seulement ce qu'ils font.
/// </summary>
public sealed class ShortcutRunner
{
    public const string StandbyAll = "standby_all";

    /// <summary>« key_1 », « key_2 », « key_3 ».</summary>
    public const string KeyPrefix = "key_";

    /// <summary>Touches proposées dans les raccourcis (le lanceur en montre quatre au plus).</summary>
    public const int ShortcutKeys = 3;

    private readonly SpeakerManager _speakers;

    public ShortcutRunner(SpeakerManager speakers) => _speakers = speakers;

    /// <summary>
    /// Les raccourcis à publier, dans la langue de l'application : identifiant, titre
    /// court (10 à 12 caractères, ce que tout lanceur affiche), titre long (25 au
    /// plus, quand la place le permet), icône (image de l'application, sans extension).
    /// </summary>
    public static IReadOnlyList<(string Id, string Title, string Subtitle, string Icon)> Definitions()
    {
        var list = new List<(string, string, string, string)>
        {
            (StandbyAll, Localization.Get("L_ShortcutStandbyShort"), Localization.Get("L_ShortcutStandbyLong"), "shortcut_power"),
        };

        for (var slot = 1; slot <= ShortcutKeys; slot++)
        {
            list.Add((KeyPrefix + slot, Localization.Get("L_ShortcutKeyShort", slot), Localization.Get("L_ShortcutKeyLong", slot), "shortcut_key"));
        }

        return list;
    }

    /// <summary>
    /// Fait ce que le raccourci demande et rend le compte rendu à afficher. Un
    /// identifiant inconnu (raccourci d'une autre version) ne fait rien : message vide.
    /// </summary>
    public async Task<(string Message, bool IsError)> RunAsync(string? id, CancellationToken ct = default)
    {
        if (id == StandbyAll)
        {
            return await StandbyAllAsync(ct).ConfigureAwait(false);
        }

        if (id is not null && id.StartsWith(KeyPrefix, StringComparison.Ordinal) &&
            int.TryParse(id[KeyPrefix.Length..], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var slot) &&
            slot is >= 1 and <= PresetKeys.PerRow)
        {
            return await PlayKeyAsync(slot, ct).ConfigureAwait(false);
        }

        return ("", false);
    }

    private async Task<(string, bool)> StandbyAllAsync(CancellationToken ct)
    {
        var known = _speakers.KnownSpeakers.ToList();
        var targets = known.Where(s => s.HasStr).ToList();
        var withoutAgent = known.Count - targets.Count;

        if (targets.Count == 0)
        {
            return (Localization.Get(known.Count == 0 ? "S_ShortcutNoSpeaker" : "S_ShortcutNoAgent"), true);
        }

        var results = await Task.WhenAll(targets.Select(t => PowerOffAsync(t, ct))).ConfigureAwait(false);
        var failed = targets.Where((_, i) => !results[i]).Select(t => t.DisplayName).ToList();
        var done = targets.Count - failed.Count;

        var message = failed.Count == 0
            ? Localization.Get("S_ShortcutStandbyDone", done)
            : Localization.Get("S_ShortcutStandbyPartly", done, targets.Count, string.Join(", ", failed));

        if (withoutAgent > 0)
        {
            message += " " + Localization.Get("S_ShortcutStandbySkipped", withoutAgent);
        }

        return (message, failed.Count > 0);
    }

    /// <summary>
    /// Met une enceinte en veille par son agent. Sans réponse sur le port enregistré,
    /// l'agent est cherché sur ses deux ports (une réinstallation peut le déplacer),
    /// puis la demande est refaite une fois.
    /// </summary>
    private async Task<bool> PowerOffAsync(SoundReborn.Core.Models.SpeakerEndpoint endpoint, CancellationToken ct)
    {
        var port = endpoint.StrPort!.Value;

        try
        {
            await new StrApiClient(endpoint.Host, port, _speakers.Http).SetPowerAsync(false, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not SpeakerException { StatusCode: not null })
        {
            // Pas de réponse HTTP : l'agent a peut-être changé de port.
        }
        catch (Exception)
        {
            // L'agent a répondu non : inutile d'insister.
            return false;
        }

        try
        {
            var found = await StrApiClient.ProbeAsync(endpoint.Host, _speakers.Http, StrApiClient.NormalTimeout, ct).ConfigureAwait(false);

            if (found is null || found.Value.Port == port)
            {
                return false;
            }

            await new StrApiClient(endpoint.Host, found.Value.Port, _speakers.Http).SetPowerAsync(false, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<(string, bool)> PlayKeyAsync(int slot, CancellationToken ct)
    {
        try
        {
            var device = _speakers.Current ?? await _speakers.TryRestoreAsync(ct).ConfigureAwait(false);

            if (device is null)
            {
                return (Localization.Get("S_ShortcutNoSpeaker"), true);
            }

            await device.RecallPresetAsync(slot, ct).ConfigureAwait(false);
            return (Localization.Get("S_ShortcutKeyPlayed", PresetKeys.Label(slot), device.Endpoint.DisplayName), false);
        }
        catch (Exception ex)
        {
            return (ex switch
            {
                TaskCanceledException => Localization.Get("S_Timeout"),
                HttpRequestException => Localization.Get("S_Unreachable"),
                _ => ex.Message,
            }, true);
        }
    }
}

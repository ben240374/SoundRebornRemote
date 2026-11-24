using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>
/// La partie « enceinte » de la sauvegarde : ce que l'agent STR garde pour
/// l'enceinte sélectionnée — ses douze touches, ses réveils, ses volumes de départ
/// et maximum, les actions des touches de sa télécommande, les réglages de son
/// écran — lu, puis réécrit sur l'enceinte visée.
///
/// - Les touches s'écrivent d'un seul coup (PUT /api/presets) ; celles que le
///   fichier ne garnit pas restent comme elles sont. Sans seconde rangée sur
///   l'enceinte visée, les touches 7 à 12 du fichier sont passées.
/// - Les réveils de l'enceinte sont remplacés par ceux du fichier.
/// - Chaque partie est faite à son tour ; un refus n'empêche pas la suite, et le
///   compte rendu dit ce qui a été restauré et ce que l'enceinte a refusé.
/// - Le groupe permanent n'est pas sauvegardé : il désigne d'autres enceintes par
///   leur adresse, qui peut changer.
/// </summary>
public static class SpeakerBackupSteps
{
    /// <summary>Lit ce que l'agent garde pour l'enceinte. Sans les touches, pas de partie enceinte (exception).</summary>
    public static async Task<SpeakerBackup> CaptureAsync(SpeakerDevice device, StrApiClient str, DateTimeOffset now)
    {
        var presets = await str.GetPresetsRawAsync().ConfigureAwait(true);
        var alarms = await OptionalAsync(() => str.GetAlarmsAsync()).ConfigureAwait(true);
        var startVolume = await OptionalValueAsync(() => str.GetStartVolumeAsync()).ConfigureAwait(true);
        var keyActions = await OptionalAsync(() => str.GetKeyActionsAsync()).ConfigureAwait(true);
        var maxVolume = await OptionalValueAsync(() => str.GetMaxVolumeAsync()).ConfigureAwait(true);
        var display = await ReadDisplayAsync(str).ConfigureAwait(true);

        return SpeakerBackup.Create(
            device.Endpoint.DisplayName,
            device.Endpoint.Type,
            device.AgentVersionText,
            now,
            presets,
            alarms,
            startVolume,
            keyActions,
            maxVolume,
            display);
    }

    /// <summary>Réécrit chaque partie sur l'enceinte visée ; done et failed reçoivent le compte rendu.</summary>
    public static async Task RestoreAsync(StrApiClient str, SpeakerBackup backup, List<string> done, List<string> failed)
    {
        await RestorePresetsAsync(str, backup, done, failed).ConfigureAwait(true);
        await RestoreAlarmsAsync(str, backup, done, failed).ConfigureAwait(true);
        await RestoreStartVolumeAsync(str, backup, done, failed).ConfigureAwait(true);
        await RestoreKeyActionsAsync(str, backup, done, failed).ConfigureAwait(true);
        await RestoreMaxVolumeAsync(str, backup, done, failed).ConfigureAwait(true);
        await RestoreDisplayAsync(str, backup, done, failed).ConfigureAwait(true);
    }

    private static async Task RestorePresetsAsync(StrApiClient str, SpeakerBackup backup, List<string> done, List<string> failed)
    {
        if (backup.PresetCount == 0)
        {
            return;
        }

        var secondRow = false;

        try
        {
            secondRow = (await str.GetAgentInfoAsync().ConfigureAwait(true))?.HasSecondPresetRow ?? false;
        }
        catch (Exception)
        {
            // Agent muet sur sa version : on s'en tient aux six touches.
        }

        var set = backup.PresetsFor(secondRow);
        var skipped = backup.PresetCount - set.Count;

        if (set.Count > 0)
        {
            try
            {
                await str.PutPresetsAsync(set).ConfigureAwait(true);
                done.Add(Localization.Get("S_SpeakerRestorePresets", set.Count));
            }
            catch (Exception ex)
            {
                failed.Add(Localization.Get("S_SpeakerRestorePresetsFailed", Reason(ex)));
            }
        }

        if (skipped > 0)
        {
            failed.Add(Localization.Get("S_SpeakerRestoreNoSecondRow", skipped));
        }
    }

    private static async Task RestoreAlarmsAsync(StrApiClient str, SpeakerBackup backup, List<string> done, List<string> failed)
    {
        if (backup.Alarms is null)
        {
            return;
        }

        try
        {
            var current = await str.GetAlarmsAsync().ConfigureAwait(true);

            // Deux essais : une autre application a pu enregistrer entre la lecture et l'envoi.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var node = (JsonObject)backup.Alarms.DeepClone();

                if (current.Rev is long rev)
                {
                    node["rev"] = rev;
                }

                var result = await str.PutAlarmsAsync(StrAlarmDocument.Parse(node.ToJsonString())).ConfigureAwait(true);

                if (!result.IsConflict)
                {
                    done.Add(Localization.Get("S_SpeakerRestoreAlarms", backup.AlarmCount));
                    return;
                }

                current = result.Current!;
            }

            failed.Add(Localization.Get("S_SpeakerRestoreAlarmsFailed", Localization.Get("S_AlarmConflict")));
        }
        catch (Exception ex)
        {
            failed.Add(Localization.Get("S_SpeakerRestoreAlarmsFailed", Reason(ex)));
        }
    }

    private static async Task RestoreStartVolumeAsync(StrApiClient str, SpeakerBackup backup, List<string> done, List<string> failed)
    {
        if (backup.StartVolume is not int level)
        {
            return;
        }

        try
        {
            if (await OptionalValueAsync(() => str.GetStartVolumeAsync()).ConfigureAwait(true) is null)
            {
                // L'enceinte visée ne le gère pas : rien à écrire.
                return;
            }

            await str.SetStartVolumeAsync(level).ConfigureAwait(true);
            done.Add(level == 0
                ? Localization.Get("S_SpeakerRestoreStartVolumeOff")
                : Localization.Get("S_SpeakerRestoreStartVolume", level));
        }
        catch (Exception ex)
        {
            failed.Add(Localization.Get("S_SpeakerRestoreStartVolumeFailed", Reason(ex)));
        }
    }

    private static async Task RestoreKeyActionsAsync(StrApiClient str, SpeakerBackup backup, List<string> done, List<string> failed)
    {
        if (backup.KeyActions is null)
        {
            return;
        }

        try
        {
            if (await OptionalAsync(() => str.GetKeyActionsAsync()).ConfigureAwait(true) is null)
            {
                // L'agent visé ne connaît pas les actions de touches.
                return;
            }

            await str.PutKeyActionsAsync(backup.KeyActions).ConfigureAwait(true);
            done.Add(Localization.Get("S_SpeakerRestoreKeyActions"));
        }
        catch (Exception ex)
        {
            failed.Add(Localization.Get("S_SpeakerRestoreKeyActionsFailed", Reason(ex)));
        }
    }

    private static async Task RestoreMaxVolumeAsync(StrApiClient str, SpeakerBackup backup, List<string> done, List<string> failed)
    {
        if (backup.MaxVolume is not int level)
        {
            return;
        }

        try
        {
            if (await OptionalValueAsync(() => str.GetMaxVolumeAsync()).ConfigureAwait(true) is null)
            {
                // L'enceinte visée ne le gère pas : rien à écrire.
                return;
            }

            await str.SetMaxVolumeAsync(level).ConfigureAwait(true);
            done.Add(level == 0
                ? Localization.Get("S_SpeakerRestoreMaxVolumeOff")
                : Localization.Get("S_SpeakerRestoreMaxVolume", level));
        }
        catch (Exception ex)
        {
            failed.Add(Localization.Get("S_SpeakerRestoreMaxVolumeFailed", Reason(ex)));
        }
    }

    /// <summary>
    /// Les réglages de l'écran : seulement ceux que l'enceinte visée sait faire.
    /// Une enceinte sans écran (SoundTouch 10) les passe tous, sans rien signaler.
    /// </summary>
    private static async Task RestoreDisplayAsync(StrApiClient str, SpeakerBackup backup, List<string> done, List<string> failed)
    {
        if (backup.Display is null)
        {
            return;
        }

        var written = 0;
        Exception? refusal = null;

        foreach (var kind in StrDisplaySetting.Kinds)
        {
            if (backup.Display[kind] is not JsonObject entry ||
                entry["enabled"] is not JsonValue enabledValue ||
                !enabledValue.TryGetValue<bool>(out var enabled))
            {
                continue;
            }

            try
            {
                var current = await str.GetDisplaySettingAsync(kind).ConfigureAwait(true);

                if (!current.Supported)
                {
                    continue;
                }

                var mode = entry["mode"] is JsonValue modeValue && modeValue.TryGetValue<string>(out var m) ? m : null;
                await str.SetDisplaySettingAsync(kind, enabled, mode).ConfigureAwait(true);
                written++;
            }
            catch (SpeakerException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Agent antérieur à ce réglage : rien à écrire.
            }
            catch (Exception ex)
            {
                refusal ??= ex;
            }
        }

        if (written > 0)
        {
            done.Add(Localization.Get("S_SpeakerRestoreDisplay", written));
        }

        if (refusal is not null)
        {
            failed.Add(Localization.Get("S_SpeakerRestoreDisplayFailed", Reason(refusal)));
        }
    }

    /// <summary>Les réglages de l'écran de l'enceinte ; null quand l'agent n'en connaît aucun.</summary>
    private static async Task<List<StrDisplaySetting>?> ReadDisplayAsync(StrApiClient str)
    {
        var settings = new List<StrDisplaySetting>();

        foreach (var kind in StrDisplaySetting.Kinds)
        {
            if (await OptionalAsync(() => str.GetDisplaySettingAsync(kind)).ConfigureAwait(true) is { } setting)
            {
                settings.Add(setting);
            }
        }

        return settings.Count == 0 ? null : settings;
    }

    // ================================================================ Outils

    /// <summary>La raison d'un refus : le texte de l'agent quand il en donne un, sinon le message habituel.</summary>
    internal static string Reason(Exception ex) => ex switch
    {
        SpeakerException { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.Conflict } ste
            when !string.IsNullOrWhiteSpace(ste.ResponseBody) && !ste.ResponseBody!.TrimStart().StartsWith('{')
            => ste.ResponseBody!.Trim(),
        SpeakerException { StatusCode: HttpStatusCode.NotFound } => Localization.Get("S_SpeakerRestoreUnsupported"),
        TaskCanceledException => Localization.Get("S_Timeout"),
        HttpRequestException => Localization.Get("S_Unreachable"),
        _ => ex.Message,
    };

    private static async Task<T?> OptionalAsync<T>(Func<Task<T>> read) where T : class
    {
        try
        {
            return await read().ConfigureAwait(true);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<int?> OptionalValueAsync(Func<Task<int?>> read)
    {
        try
        {
            return await read().ConfigureAwait(true);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Le nom de l'enceinte réduit à ce qu'un nom de fichier accepte partout.</summary>
    public static string FileNamePart(string name)
    {
        var builder = new StringBuilder();

        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if ((c == ' ' || c == '-' || c == '_') && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var part = builder.ToString().Trim('-');

        if (part.Length > 40)
        {
            part = part[..40].TrimEnd('-');
        }

        return part.Length == 0 ? "enceinte" : part;
    }
}

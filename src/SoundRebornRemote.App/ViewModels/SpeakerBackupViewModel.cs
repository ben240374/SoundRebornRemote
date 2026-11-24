using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>
/// La carte « Sauvegarde de l'enceinte » des Réglages : ce que l'agent STR garde
/// pour l'enceinte SÉLECTIONNÉE — ses douze touches, ses réveils, ses volumes de
/// départ et maximum, les actions des touches de sa télécommande, les réglages de
/// son écran — dans un fichier, et retour.
///
/// - La restauration demande confirmation, en nommant l'enceinte visée et celle
///   d'où vient le fichier : il peut servir à recopier les réglages sur une autre.
/// - Les touches s'écrivent d'un seul coup (PUT /api/presets) ; celles que le
///   fichier ne garnit pas restent comme elles sont. Sans seconde rangée sur
///   l'enceinte visée, les touches 7 à 12 du fichier sont passées.
/// - Les réveils de l'enceinte sont remplacés par ceux du fichier.
/// - Chaque partie est faite à son tour, et le compte rendu dit ce qui a été
///   restauré et ce que l'enceinte a refusé : un refus n'empêche pas la suite.
/// - Le groupe permanent n'est pas sauvegardé : il désigne d'autres enceintes par
///   leur adresse, qui peut changer.
/// </summary>
public sealed partial class SpeakerBackupViewModel : BaseViewModel
{
    private readonly IFileExchange _files;
    private readonly string _directory;
    private readonly TimeProvider _clock;

    public SpeakerBackupViewModel(SpeakerManager speakers, IFileExchange files)
        : this(speakers, files, FileSystem.CacheDirectory, TimeProvider.System)
    {
    }

    public SpeakerBackupViewModel(SpeakerManager speakers, IFileExchange files, string directory, TimeProvider clock)
        : base(speakers)
    {
        _files = files;
        _directory = directory;
        _clock = clock;
        Speakers.DeviceChanged += (_, _) => OnMainThread(RefreshTarget);
        Localization.LanguageChanged += (_, _) => OnMainThread(RebuildLabels);
        RefreshTarget();
    }

    /// <summary>« Cuisine », ou l'invitation à choisir une enceinte.</summary>
    [ObservableProperty]
    private string _targetText = "";

    /// <summary>Une enceinte avec l'agent STR est sélectionnée : les boutons servent.</summary>
    [ObservableProperty]
    private bool _canUse;

    /// <summary>Après une restauration : de quoi relire les cartes qui montrent l'enceinte.</summary>
    public event EventHandler? Restored;

    /// <summary>Textes traduits : à refaire quand la langue change.</summary>
    public void RebuildLabels()
    {
        RefreshTarget();
        ShowInfo(null);
    }

    private void RefreshTarget()
    {
        var device = Device;
        CanUse = device?.Str is not null;
        TargetText = device is null
            ? Localization.Get("L_NoSpeakerSelected")
            : device.Str is null
                ? Localization.Get("L_SpeakerBackupNoAgent", device.Endpoint.DisplayName)
                : Localization.Get("L_SpeakerBackupTarget", device.Endpoint.DisplayName);
    }

    // ================================================================ Sauvegarder

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var device = Device;

        if (device?.Str is not { } str)
        {
            IsError = true;
            StatusMessage = Localization.Get(device is null ? "S_NoSpeaker" : "S_NeedAgentStation");
            return;
        }

        IsBusy = true;
        ShowInfo(Localization.Get("S_SpeakerBackupReading"));

        try
        {
            // Les touches d'abord : sans elles, pas de sauvegarde.
            var presets = await str.GetPresetsRawAsync().ConfigureAwait(true);
            var alarms = await OptionalAsync(() => str.GetAlarmsAsync()).ConfigureAwait(true);
            var startVolume = await OptionalValueAsync(() => str.GetStartVolumeAsync()).ConfigureAwait(true);
            var keyActions = await OptionalAsync(() => str.GetKeyActionsAsync()).ConfigureAwait(true);
            var maxVolume = await OptionalValueAsync(() => str.GetMaxVolumeAsync()).ConfigureAwait(true);
            var display = await ReadDisplayAsync(str).ConfigureAwait(true);

            var now = _clock.GetLocalNow();
            var backup = SpeakerBackup.Create(
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

            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"SoundRebornRemote-{FileNamePart(device.Endpoint.DisplayName)}-{now:yyyy-MM-dd}.json");

            await File.WriteAllTextAsync(path, backup.Serialize()).ConfigureAwait(true);
            await _files.ShareAsync(path, "application/json", Localization.Get("L_BackupShareTitle")).ConfigureAwait(true);

            ShowInfo(Localization.Get("S_SpeakerBackupSaved", device.Endpoint.DisplayName, backup.PresetCount, backup.AlarmCount));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ================================================================ Restaurer

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var device = Device;

        if (device?.Str is not { } str)
        {
            IsError = true;
            StatusMessage = Localization.Get(device is null ? "S_NoSpeaker" : "S_NeedAgentStation");
            return;
        }

        string? text;

        try
        {
            text = await _files.PickTextAsync(Localization.Get("L_BackupPickTitle"), SpeakerBackup.MaxBytes).ConfigureAwait(true);
        }
        catch (InvalidDataException)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_SpeakerBackupNotRecognized");
            return;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }

        if (text is null)
        {
            // L'utilisateur a renoncé.
            return;
        }

        if (SpeakerBackup.Parse(text) is not { } backup)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_SpeakerBackupNotRecognized");
            return;
        }

        var target = device.Endpoint.DisplayName;
        var source = string.IsNullOrWhiteSpace(backup.SpeakerName) ? "?" : backup.SpeakerName;
        var date = backup.SavedAt?.ToLocalTime().ToString("d", Localization.CultureOf(Localization.Current)) ?? "?";
        var go = Localization.Get("L_SpeakerRestoreGo");

        var answer = await Shell.Current.DisplayActionSheetAsync(
            Localization.Get("L_SpeakerRestoreAsk", target, source, date, backup.PresetCount, backup.AlarmCount),
            Localization.Get("L_Cancel"),
            null,
            go).ConfigureAwait(true);

        if (answer != go)
        {
            return;
        }

        // L'enceinte a pu changer pendant la question : on reste sur celle qui a été nommée.
        if (Device?.Str is null || !string.Equals(Device.Host, device.Host, StringComparison.OrdinalIgnoreCase))
        {
            IsError = true;
            StatusMessage = Localization.Get("S_SpeakerRestoreChanged");
            return;
        }

        str = Device.Str;
        IsBusy = true;
        ShowInfo(Localization.Get("S_SpeakerRestoring", target));

        var done = new List<string>();
        var failed = new List<string>();

        try
        {
            await RestorePresetsAsync(str, backup, done, failed).ConfigureAwait(true);
            await RestoreAlarmsAsync(str, backup, done, failed).ConfigureAwait(true);
            await RestoreStartVolumeAsync(str, backup, done, failed).ConfigureAwait(true);
            await RestoreKeyActionsAsync(str, backup, done, failed).ConfigureAwait(true);
            await RestoreMaxVolumeAsync(str, backup, done, failed).ConfigureAwait(true);
            await RestoreDisplayAsync(str, backup, done, failed).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }

        var report = new StringBuilder(Localization.Get(failed.Count == 0 ? "S_SpeakerRestored" : "S_SpeakerRestoredPartly", target));

        foreach (var line in done.Concat(failed))
        {
            report.Append('\n').Append("• ").Append(line);
        }

        IsError = failed.Count > 0;
        StatusMessage = report.ToString();

        Restored?.Invoke(this, EventArgs.Empty);
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
    private static string Reason(Exception ex) => ex switch
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
    internal static string FileNamePart(string name)
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

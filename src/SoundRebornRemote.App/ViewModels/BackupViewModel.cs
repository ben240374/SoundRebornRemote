using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>
/// La carte « Sauvegarde » de l'onglet Réglages : UN fichier avec tout ce qui peut
/// se perdre (<see cref="FullBackup"/>) :
///
/// - ce qui vit dans le téléphone : présélections « Local », textes gardés du
///   message vocal, réglages de l'application (langue, thème, vérification des
///   mises à jour, volume des messages, enceintes connues) ;
/// - ce que l'agent STR garde pour l'enceinte SÉLECTIONNÉE : touches, réveils,
///   volumes de départ et maximum, touches de la télécommande, écran
///   (<see cref="SpeakerBackupSteps"/>).
///
/// Les listes du téléphone survivent à une mise à jour, mais pas à une
/// désinstallation — y compris celle qu'impose Android avant d'installer un APK
/// signé d'une autre clé.
///
/// La restauration demande une seule confirmation, qui dit ce qui va être fait et
/// sur quelle enceinte. Les présélections et textes du fichier S'AJOUTENT à ceux du
/// téléphone ; ses réglages remplacent ceux de l'application ; la partie enceinte
/// va sur l'enceinte sélectionnée (elle peut servir à recopier une enceinte sur une
/// autre). Les fichiers des versions précédentes (favoris seuls, enceinte seule)
/// sont lus aussi.
/// </summary>
public sealed partial class BackupViewModel : BaseViewModel
{
    private readonly LocalPresetsViewModel _local;
    private readonly VoiceMessageViewModel _voice;
    private readonly IFileExchange _files;
    private readonly string _directory;
    private readonly TimeProvider _clock;

    public BackupViewModel(SpeakerManager speakers, PresetsViewModel presets, VoiceMessageViewModel voice, IFileExchange files)
        : this(speakers, presets.Local, voice, files, FileSystem.CacheDirectory, TimeProvider.System)
    {
    }

    public BackupViewModel(
        SpeakerManager speakers,
        LocalPresetsViewModel local,
        VoiceMessageViewModel voice,
        IFileExchange files,
        string directory,
        TimeProvider clock)
        : base(speakers)
    {
        _local = local;
        _voice = voice;
        _files = files;
        _directory = directory;
        _clock = clock;
        Speakers.DeviceChanged += (_, _) => OnMainThread(RefreshTarget);
        Localization.LanguageChanged += (_, _) => OnMainThread(RebuildLabels);
        RefreshTarget();
    }

    /// <summary>L'enceinte comprise dans la sauvegarde, ou pourquoi il n'y en a pas.</summary>
    [ObservableProperty]
    private string _targetText = "";

    /// <summary>Après une restauration : de quoi relire les cartes des Réglages.</summary>
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
        TargetText = device is null
            ? Localization.Get("L_BackupNoSpeaker")
            : device.Str is null
                ? Localization.Get("L_BackupSpeakerNoAgent", device.Endpoint.DisplayName)
                : Localization.Get("L_SpeakerBackupTarget", device.Endpoint.DisplayName);
    }

    // ================================================================ Sauvegarder

    /// <summary>Écrit le fichier et ouvre l'écran de partage pour le garder.</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var presets = _local.SavedPresets;
        var texts = _voice.SavedTexts;
        var device = Device;

        IsBusy = true;

        try
        {
            var now = _clock.GetLocalNow();

            // L'enceinte d'abord : si elle ne répond pas, le reste part quand même.
            SpeakerBackup? speaker = null;
            string? speakerFailure = null;

            if (device?.Str is { } str)
            {
                ShowInfo(Localization.Get("S_SpeakerBackupReading"));

                try
                {
                    speaker = await SpeakerBackupSteps.CaptureAsync(device, str, now).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    speakerFailure = SpeakerBackupSteps.Reason(ex);
                }
            }

            var app = FavoritesBackup.Serialize(presets, texts, now, CurrentSettings());

            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"SoundRebornRemote-backup-{now:yyyy-MM-dd}.json");

            await File.WriteAllTextAsync(path, FullBackup.Serialize(app, speaker, now)).ConfigureAwait(true);
            await _files.ShareAsync(path, "application/json", Localization.Get("L_BackupShareTitle")).ConfigureAwait(true);

            if (speaker is not null)
            {
                ShowInfo(Localization.Get("S_FullBackupSaved", presets.Count, texts.Count,
                    device!.Endpoint.DisplayName, speaker.PresetCount, speaker.AlarmCount));
            }
            else if (speakerFailure is not null)
            {
                IsError = true;
                StatusMessage = Localization.Get("S_FullBackupSpeakerFailed", presets.Count, texts.Count,
                    device!.Endpoint.DisplayName, speakerFailure);
            }
            else
            {
                ShowInfo(Localization.Get("S_FullBackupAppOnly", presets.Count, texts.Count));
            }
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

    /// <summary>Lit un fichier, dit ce qu'il va faire, et le fait après confirmation.</summary>
    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (IsBusy)
        {
            return;
        }

        string? text;

        try
        {
            text = await _files.PickTextAsync(Localization.Get("L_BackupPickTitle"), FullBackup.MaxBytes).ConfigureAwait(true);
        }
        catch (InvalidDataException)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_BackupNotRecognized");
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

        if (FullBackup.Parse(text) is not { } backup)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_BackupNotRecognized");
            return;
        }

        var device = Device;
        var target = device?.Str is not null ? device : null;
        var culture = Localization.CultureOf(Localization.Current);
        var date = backup.SavedAt?.ToLocalTime().ToString("d", culture) ?? "?";

        // Une seule question, qui dit tout ce qui va être fait.
        var question = new StringBuilder(Localization.Get("L_BackupRestoreAsk", date));

        if (backup.App is { } app)
        {
            question.Append("\n\n• ").Append(Localization.Get(app.Settings is null ? "L_BackupRestoreApp" : "L_BackupRestoreAppSettings",
                app.Presets.Count, app.Texts.Count));
        }

        if (backup.Speaker is { } speakerPart)
        {
            var source = string.IsNullOrWhiteSpace(speakerPart.SpeakerName) ? "?" : speakerPart.SpeakerName;
            question.Append("\n\n• ").Append(target is not null
                ? Localization.Get("L_BackupRestoreSpeaker", target.Endpoint.DisplayName, source, speakerPart.PresetCount, speakerPart.AlarmCount)
                : Localization.Get("L_BackupRestoreNoSpeaker", source));
        }

        var go = Localization.Get("L_SpeakerRestoreGo");
        var answer = await Shell.Current.DisplayActionSheetAsync(question.ToString(), Localization.Get("L_Cancel"), null, go)
            .ConfigureAwait(true);

        if (answer != go)
        {
            return;
        }

        // L'enceinte a pu changer pendant la question : on reste sur celle qui a été nommée.
        if (backup.Speaker is not null && target is not null &&
            (Device?.Str is null || !string.Equals(Device.Host, target.Host, StringComparison.OrdinalIgnoreCase)))
        {
            IsError = true;
            StatusMessage = Localization.Get("S_SpeakerRestoreChanged");
            return;
        }

        IsBusy = true;
        var report = new List<string>();
        var partly = false;

        try
        {
            if (backup.App is { } appPart)
            {
                var presets = _local.Import(appPart.Presets);
                var texts = _voice.ImportTexts(appPart.Texts);

                if (appPart.Settings is { } settings)
                {
                    await ApplySettingsAsync(settings).ConfigureAwait(true);
                    report.Add(Localization.Get("S_BackupImportedAll", presets, texts));
                }
                else
                {
                    report.Add(presets == 0 && texts == 0
                        ? Localization.Get("S_BackupNothingNew")
                        : Localization.Get("S_BackupImported", presets, texts));
                }
            }

            if (backup.Speaker is { } speaker)
            {
                if (target is not null && Device?.Str is { } str)
                {
                    var name = target.Endpoint.DisplayName;
                    ShowInfo(Localization.Get("S_SpeakerRestoring", name));

                    var done = new List<string>();
                    var failed = new List<string>();
                    await SpeakerBackupSteps.RestoreAsync(str, speaker, done, failed).ConfigureAwait(true);

                    var lines = new StringBuilder(Localization.Get(failed.Count == 0 ? "S_SpeakerRestored" : "S_SpeakerRestoredPartly", name));

                    foreach (var line in done.Concat(failed))
                    {
                        lines.Append('\n').Append("• ").Append(line);
                    }

                    report.Add(lines.ToString());
                    partly = failed.Count > 0;
                }
                else
                {
                    report.Add(Localization.Get("L_BackupRestoreNoSpeaker",
                        string.IsNullOrWhiteSpace(speaker.SpeakerName) ? "?" : speaker.SpeakerName));
                }
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        IsError = partly;
        StatusMessage = string.Join("\n\n", report);
        Restored?.Invoke(this, EventArgs.Empty);
    }

    // ================================================================ Réglages

    /// <summary>Les réglages de l'application tels qu'ils sont maintenant.</summary>
    private AppSettingsBackup CurrentSettings() => new()
    {
        Language = Localization.Current.ToString(),
        Theme = ThemeService.Current.ToString(),
        AutoCheckUpdates = ReadAutoCheck(),
        VoiceVolume = (int)Math.Round(_voice.AnnounceVolume),
        Speakers = Speakers.KnownSpeakers
            .Select(e => (BackupSpeaker?)new BackupSpeaker
            {
                Host = e.Host,
                Name = e.Name,
                DeviceId = e.DeviceId,
                Type = e.Type,
                StrPort = e.StrPort,
            })
            .ToList(),
        SelectedSpeaker = Speakers.SelectedEndpoint?.Host ?? Speakers.LastSelectedHost,
    };

    /// <summary>
    /// Applique les réglages d'un fichier, chacun par le service qui le tient (la
    /// langue et le thème changent tout de suite). Un réglage inconnu est passé.
    /// </summary>
    private async Task ApplySettingsAsync(AppSettingsBackup settings)
    {
        if (Enum.TryParse<AppLanguage>(settings.Language, out var language) &&
            Localization.Available.Contains(language) &&
            language != Localization.Current)
        {
            Localization.SetLanguage(language);
        }

        if (Enum.TryParse<AppThemeChoice>(settings.Theme, out var theme) &&
            ThemeService.Available.Contains(theme) &&
            theme != ThemeService.Current)
        {
            ThemeService.Set(theme);
        }

        if (settings.AutoCheckUpdates is bool auto)
        {
            try
            {
                Preferences.Default.Set(UpdateViewModel.AutoKey, auto);
            }
            catch (Exception)
            {
                // Sans stockage, le réglage reste celui d'avant.
            }
        }

        if (settings.VoiceVolume is int volume)
        {
            _voice.AnnounceVolume = volume;
        }

        foreach (var speaker in settings.Speakers ?? new List<BackupSpeaker?>())
        {
            // Une enceinte déjà connue garde ce que le téléphone en sait : son nom ou
            // son port ont pu changer depuis la sauvegarde.
            if (speaker is null ||
                Speakers.KnownSpeakers.Any(e => string.Equals(e.Host, speaker.Host, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Speakers.Remember(new SpeakerEndpoint
            {
                Host = speaker.Host,
                Name = speaker.Name ?? "",
                DeviceId = speaker.DeviceId ?? "",
                Type = speaker.Type ?? "",
                StrPort = speaker.StrPort,
            });
        }

        // Sur un téléphone où aucune enceinte n'est encore choisie (application
        // réinstallée), on reprend celle de la sauvegarde. Jamais à la place d'un
        // choix déjà fait.
        if (Speakers.Current is null && settings.SelectedSpeaker is { } host &&
            Speakers.KnownSpeakers.FirstOrDefault(e => string.Equals(e.Host, host, StringComparison.OrdinalIgnoreCase)) is { } endpoint)
        {
            try
            {
                await Speakers.SelectAsync(endpoint).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Enceinte éteinte ou ailleurs : elle reste dans la liste, à choisir plus tard.
            }
        }
    }

    private static bool ReadAutoCheck()
    {
        try
        {
            return Preferences.Default.Get(UpdateViewModel.AutoKey, true);
        }
        catch (Exception)
        {
            return true;
        }
    }
}

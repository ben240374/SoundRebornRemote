using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>
/// La carte « Sauvegarde des favoris » de l'onglet Réglages : exporte dans un
/// fichier les présélections « Local » et les textes gardés du message vocal, et
/// les réimporte.
///
/// Ces listes vivent dans les préférences de l'application : elles restent après
/// une mise à jour, mais une désinstallation les efface — y compris celle
/// qu'impose Android avant d'installer un APK signé d'une autre clé (une version de
/// test lancée depuis Visual Studio, puis la version publiée, par exemple).
///
/// L'import AJOUTE ce qui manque (adresse ou texte absents), sans rien remplacer ni
/// supprimer, dans la limite de chaque liste.
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
    }

    /// <summary>Exporte les favoris et ouvre l'écran de partage pour garder le fichier.</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var presets = _local.SavedPresets;
        var texts = _voice.SavedTexts;

        if (presets.Count == 0 && texts.Count == 0)
        {
            ShowInfo(Localization.Get("S_BackupEmpty"));
            return;
        }

        IsBusy = true;

        try
        {
            var now = _clock.GetLocalNow();
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"SoundRebornRemote-favoris-{now:yyyy-MM-dd}.json");

            await File.WriteAllTextAsync(path, FavoritesBackup.Serialize(presets, texts, now)).ConfigureAwait(true);
            await _files.ShareAsync(path, "application/json", Localization.Get("L_BackupShareTitle")).ConfigureAwait(true);

            ShowInfo(Localization.Get("S_BackupExported", presets.Count, texts.Count));
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

    /// <summary>Lit un fichier de sauvegarde et ajoute aux listes ce qui y manque.</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            string? text;

            try
            {
                text = await _files.PickTextAsync(Localization.Get("L_BackupPickTitle"), FavoritesBackup.MaxBytes)
                    .ConfigureAwait(true);
            }
            catch (InvalidDataException)
            {
                IsError = true;
                StatusMessage = Localization.Get("S_BackupNotRecognized");
                return;
            }

            if (text is null)
            {
                // L'utilisateur a renoncé.
                return;
            }

            if (FavoritesBackup.Parse(text) is not { } backup)
            {
                IsError = true;
                StatusMessage = Localization.Get("S_BackupNotRecognized");
                return;
            }

            var presets = _local.Import(backup.Presets);
            var texts = _voice.ImportTexts(backup.Texts);

            ShowInfo(presets == 0 && texts == 0
                ? Localization.Get("S_BackupNothingNew")
                : Localization.Get("S_BackupImported", presets, texts));
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
}

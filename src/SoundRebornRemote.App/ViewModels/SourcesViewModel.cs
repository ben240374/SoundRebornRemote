using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>Une entrée de l'historique, avec son logo déjà téléchargé.</summary>
public sealed partial class RecentResult : ObservableObject
{
    public required RecentEntry Entry { get; init; }

    public string DisplayTitle => Entry.DisplayTitle;

    public string DisplaySubtitle => Entry.DisplaySubtitle;

    [ObservableProperty]
    private ImageSource? _logo;

    [ObservableProperty]
    private bool _hasLogo;
}

/// <summary>
/// Sources d'entrée, historique de lecture et lancement d'un flux arbitraire.
///
/// À propos de Spotify : depuis l'arrêt du cloud Bose, l'enceinte n'est plus
/// une cible Spotify par elle-même. Avec l'agent STR elle redevient un point
/// Spotify Connect (go-librespot) : la sélection se fait dans l'application
/// Spotify, pas ici. Cette page sert donc à basculer la source et à voir ce qui
/// tourne ; le transport reste piloté depuis l'onglet Lecture.
/// </summary>
public sealed partial class SourcesViewModel : BaseViewModel
{
    private readonly ArtworkCache _artwork;

    public SourcesViewModel(SpeakerManager speakers, ArtworkCache artwork) : base(speakers)
    {
        _artwork = artwork;
        Speakers.DeviceChanged += (_, _) => OnMainThread(() => _ = RefreshAsync());
    }

    public ObservableCollection<SourceItem> Sources { get; } = new();

    public ObservableCollection<RecentResult> Recent { get; } = new();

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _hasStrAgent;

    [ObservableProperty]
    private string _streamUrl = "";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var device = Device;

        Sources.Clear();
        Recent.Clear();

        if (device is null)
        {
            HasStrAgent = false;
            return;
        }

        HasStrAgent = device.HasStr;
        IsRefreshing = true;

        try
        {
            foreach (var source in await device.GetSourcesAsync().ConfigureAwait(true))
            {
                // Les sources non prêtes (compte déconnecté, Bluetooth non appairé)
                // restent visibles mais l'interface les grise.
                Sources.Add(source);
            }

            if (device.Str is not null)
            {
                foreach (var entry in await device.Str.GetRecentAsync().ConfigureAwait(true))
                {
                    Recent.Add(new RecentResult { Entry = entry });
                }

                // Les logos sont téléchargés par l'application, comme ailleurs :
                // laisser le contrôle Image charger l'URL échoue trop souvent.
                foreach (var result in Recent.ToList())
                {
                    var source = await _artwork.GetImageSourceAsync(result.Entry.CardArt).ConfigureAwait(true);

                    if (source is not null)
                    {
                        result.Logo = source;
                        result.HasLogo = true;
                    }
                }
            }

            ShowInfo(null);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private Task SelectSourceAsync(SourceItem? source)
        => source is null
            ? Task.CompletedTask
            : RunAsync(d => d.SelectSourceAsync(source), Localization.Get("S_SourceSet", source.DisplayName));

    [RelayCommand]
    private Task ReplayAsync(RecentResult? result)
    {
        var entry = result?.Entry;

        if (entry is null || !entry.CanReplay)
        {
            ShowInfo(Localization.Get("S_NotReplayable"));
            return Task.CompletedTask;
        }

        return RunAsync(async d =>
        {
            if (d.Str is null)
            {
                throw new InvalidOperationException(Localization.Get("S_NeedAgentReplay"));
            }

            await d.Str.PlayUrlAsync(entry.CardUrl!, entry.CardName, entry.Mime).ConfigureAwait(false);
        }, Localization.Get("S_Replaying", entry.DisplayTitle));
    }

    /// <summary>
    /// Joue une URL quelconque via l'agent. Pratique pour une webradio que le
    /// magasin ne contient pas encore, ou pour un fichier servi sur le réseau.
    /// </summary>
    [RelayCommand]
    private Task PlayStreamAsync()
    {
        var url = StreamUrl?.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            ShowInfo(Localization.Get("S_EnterStream"));
            return Task.CompletedTask;
        }

        return RunAsync(async d =>
        {
            if (d.Str is null)
            {
                throw new InvalidOperationException(Localization.Get("S_NeedAgentUrl"));
            }

            await d.Str.PlayUrlAsync(url).ConfigureAwait(false);
        }, Localization.Get("S_StreamStarted"));
    }

    [RelayCommand]
    private Task StandbyAsync() => RunAsync(d => d.SetPowerAsync(false), Localization.Get("S_StandbySet"));

    [RelayCommand]
    private Task BluetoothAsync() => RunAsync(async d =>
    {
        if (d.Str is not null)
        {
            await d.Str.SetSourceAsync("BLUETOOTH").ConfigureAwait(false);
            return;
        }

        await d.Bose.SelectAsync(new ContentItem { Source = "BLUETOOTH" }).ConfigureAwait(false);
    }, Localization.Get("S_SourceSet", "Bluetooth"));

    [RelayCommand]
    private Task AuxAsync() => RunAsync(async d =>
    {
        if (d.Str is not null)
        {
            await d.Str.SetSourceAsync("AUX").ConfigureAwait(false);
            return;
        }

        await d.Bose.PressKeyAsync(RemoteKey.AuxInput).ConfigureAwait(false);
    }, Localization.Get("S_SourceSet", Localization.Get("L_SrcAux")));
}

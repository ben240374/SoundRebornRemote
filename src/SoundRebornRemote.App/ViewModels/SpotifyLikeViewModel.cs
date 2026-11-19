using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>
/// « J'aime » et « Je n'aime pas » pour le titre Spotify en cours, dans la carte
/// « En ce moment » de l'onglet Lecture : ce que font les touches pouce de la
/// télécommande, pour l'application (POST /api/spotify/liked, agent 1.8.1).
///
/// Les boutons n'apparaissent que si l'agent annonce la fonction et que c'est
/// Spotify qui joue. Le titre va dans les Titres likés du compte Spotify avec
/// lequel l'ENCEINTE est connectée, pas forcément celui du téléphone : la carte
/// le rappelle.
///
/// Sa propre vue-modèle, alimentée par celle de la lecture à chaque état reçu.
/// </summary>
public sealed partial class SpotifyLikeViewModel : BaseViewModel
{
    private string _trackKey = "";
    private string _title = "";

    public SpotifyLikeViewModel(SpeakerManager speakers) : base(speakers)
    {
    }

    [ObservableProperty]
    private bool _showButtons;

    /// <summary>« Ajouté aux Titres likés : … », ou la raison d'un refus. Vide le reste du temps.</summary>
    [ObservableProperty]
    private string _note = "";

    [ObservableProperty]
    private bool _noteIsError;

    /// <summary>Appelé par la vue-modèle de la lecture à chaque état reçu de l'enceinte.</summary>
    public void Update(SpeakerDevice? device, NowPlaying nowPlaying)
    {
        ShowButtons = device?.SupportsSpotifyLiked == true && IsSpotify(nowPlaying);
        _title = nowPlaying.DisplayTitle;

        // Le compte rendu vaut pour un titre : au suivant, il s'efface.
        var key = string.Join('|', nowPlaying.Track, nowPlaying.StationName, nowPlaying.Content?.ItemName);

        if (!string.Equals(key, _trackKey, StringComparison.Ordinal))
        {
            _trackKey = key;
            Note = "";
            NoteIsError = false;
        }
    }

    /// <summary>
    /// Spotify, que l'enceinte le déclare comme source ou qu'elle lise le flux
    /// Spotify relayé par l'agent (adresse « /spotify/… »).
    /// </summary>
    public static bool IsSpotify(NowPlaying nowPlaying)
        => !nowPlaying.IsIdle &&
           (string.Equals(nowPlaying.Source, "SPOTIFY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(nowPlaying.Content?.Source, "SPOTIFY", StringComparison.OrdinalIgnoreCase) ||
            nowPlaying.Content?.Location?.Contains("/spotify/", StringComparison.OrdinalIgnoreCase) == true);

    [RelayCommand]
    private Task LikeAsync() => SendAsync(true);

    [RelayCommand]
    private Task UnlikeAsync() => SendAsync(false);

    private async Task SendAsync(bool liked)
    {
        if (IsBusy || Device?.Str is not { } str || !ShowButtons)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var result = await str.SetSpotifyLikedAsync(liked).ConfigureAwait(true);
            var name = string.IsNullOrWhiteSpace(result.Name) ? _title : result.Name!;
            NoteIsError = false;
            Note = Localization.Get(liked ? "S_LikedSaved" : "S_LikedRemoved", name);
        }
        catch (SpeakerException ex)
        {
            var reason = StrLikedResult.ReasonOf(ex);
            NoteIsError = true;
            Note = ReasonKey(reason) is { } key
                ? Localization.Get(key)
                : Localization.Get("S_LikedFailed", reason ?? ((int?)ex.StatusCode)?.ToString() ?? "?");
        }
        catch (Exception)
        {
            NoteIsError = true;
            Note = Localization.Get("S_LikedUnreachable");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string? ReasonKey(string? reason) => reason switch
    {
        "not-playing" => "S_LikedNotPlaying",
        "no-track" => "S_LikedNoTrack",
        "not-a-song" => "S_LikedNotSong",
        "no-engine" => "S_LikedNoEngine",
        "engine-too-old" => "S_LikedEngineOld",
        "no-session" => "S_LikedNoSession",
        "unauthorized-401" or "forbidden-403" or "rejected-400" => "S_LikedRefusedBySpotify",
        "rate-limited-429" => "S_LikedRateLimited",
        "timeout" => "S_LikedTimeout",
        _ => null,
    };
}

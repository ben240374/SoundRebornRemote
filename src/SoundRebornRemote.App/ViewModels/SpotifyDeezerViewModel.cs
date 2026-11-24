using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>
/// La page « Spotify &amp; Deezer », ouverte depuis Réglages : la même chose que la
/// vue Spotify &amp; Deezer de l'application de bureau de STR, pour l'enceinte qui
/// reçoit les commandes (en groupe, le maître, puisque c'est lui qui joue).
///
/// Ce qu'on NE peut PAS faire, et pourquoi :
///   – se connecter à Spotify depuis l'application : le compte arrive sur
///     l'enceinte quand on la choisit dans Spotify (Spotify Connect). La page
///     dit quel compte y est, et ouvre Spotify ;
///   – relier un compte Deezer : impossible sans le cloud Bose (STR l'a retiré
///     de ses deux applications). Seul un compte déjà relié à l'enceinte marche.
///
/// Ce qu'on peut faire : voir l'état de Spotify et de Deezer sur l'enceinte,
/// « Vérifier à nouveau » et « Déconnecter » Deezer, faire jouer un lien Deezer
/// (collé, ou partagé depuis l'application Deezer), et garder ce qui joue sur une
/// touche — une playlist Spotify ou Deezer comprise.
/// </summary>
public sealed partial class SpotifyDeezerViewModel : BaseViewModel
{
    /// <summary>Après qu'une connexion Deezer a été rendue : relectures, comme la page téléphone de STR.</summary>
    public static TimeSpan[] RecheckFollowUps { get; set; } =
    {
        TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(95),
    };

    private readonly PresetsViewModel _presets;

    /// <summary>Une lecture dépassée par une autre — enceinte changée entre-temps — est jetée.</summary>
    private int _loadVersion;

    private CancellationTokenSource? _followUps;

    public SpotifyDeezerViewModel(SpeakerManager speakers, PresetsViewModel presets) : base(speakers)
    {
        _presets = presets;

        // L'enceinte qui reçoit les commandes peut changer pendant que la page est
        // ouverte (groupe formé ailleurs, autre enceinte choisie) : on relit.
        Speakers.DeviceChanged += (_, _) => OnMainThread(() =>
        {
            if (IsOpen)
            {
                _ = RefreshAsync();
            }
        });

        Localization.LanguageChanged += (_, _) => OnMainThread(() =>
        {
            if (IsOpen)
            {
                _ = RefreshAsync();
            }
        });
    }

    /// <summary>La page est à l'écran : posé par la page elle-même.</summary>
    public bool IsOpen { get; private set; }

    // ---------------------------------------------------------------- Enceinte

    [ObservableProperty]
    private string _speakerName = "";

    /// <summary>L'enceinte a l'agent STR : sans lui, rien de cette page ne fonctionne.</summary>
    [ObservableProperty]
    private bool _hasAgent;

    [ObservableProperty]
    private string _noAgentNote = "";

    [ObservableProperty]
    private bool _isLoading;

    // ---------------------------------------------------------------- Spotify

    [ObservableProperty]
    private string _spotifyState = "";

    [ObservableProperty]
    private bool _hasSpotifyRefusal;

    [ObservableProperty]
    private string _spotifyRefusal = "";

    [ObservableProperty]
    private string _spotifyRefusalHelp = "";

    [ObservableProperty]
    private string _spotifyMessage = "";

    [ObservableProperty]
    private bool _spotifyMessageIsError;

    // ---------------------------------------------------------------- Deezer

    /// <summary>L'agent connaît Deezer (1.9.0 et suivants) : la carte et le lien sont proposés.</summary>
    [ObservableProperty]
    private bool _deezerSupported;

    /// <summary>L'agent ne connaît pas Deezer : on le dit au lieu d'une carte vide.</summary>
    [ObservableProperty]
    private bool _deezerUnsupported;

    [ObservableProperty]
    private string _deezerState = "";

    [ObservableProperty]
    private bool _deezerStuck;

    [ObservableProperty]
    private bool _deezerRejected;

    [ObservableProperty]
    private bool _canDisconnectDeezer;

    [ObservableProperty]
    private bool _isDeezerBusy;

    [ObservableProperty]
    private string _deezerMessage = "";

    [ObservableProperty]
    private bool _deezerMessageIsError;

    // ---------------------------------------------------------------- Lien Deezer

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LinkKind))]
    [NotifyPropertyChangedFor(nameof(HasLinkKind))]
    [NotifyPropertyChangedFor(nameof(CanPlayLink))]
    private string _linkText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlayLink))]
    private bool _isLinkBusy;

    [ObservableProperty]
    private string _linkMessage = "";

    [ObservableProperty]
    private bool _linkMessageIsError;

    /// <summary>Ce que le lien désigne, ou ce qui ne va pas avec lui ; vide sans lien.</summary>
    public string LinkKind
    {
        get
        {
            var link = LinkText.Trim();

            if (link.Length == 0)
            {
                return "";
            }

            if (!SoundReborn.Core.Models.DeezerLink.IsPlayable(link))
            {
                return Localization.Get("S_DeezerNotALink");
            }

            return SoundReborn.Core.Models.DeezerLink.KindOf(link) switch
            {
                "playlist" => Localization.Get("L_DeezerKindPlaylist"),
                "album" => Localization.Get("L_DeezerKindAlbum"),
                "track" => Localization.Get("L_DeezerKindTrack"),
                _ => Localization.Get("L_DeezerKindShare"),
            };
        }
    }

    public bool HasLinkKind => LinkKind.Length > 0;

    public bool CanPlayLink => !IsLinkBusy && SoundReborn.Core.Models.DeezerLink.IsPlayable(LinkText);

    // ---------------------------------------------------------------- Garder sur une touche

    [ObservableProperty]
    private bool _isKeepBusy;

    // ---------------------------------------------------------------- Page

    /// <summary>La page apparaît : on lit l'état de l'enceinte.</summary>
    public Task OpenAsync()
    {
        IsOpen = true;
        return RefreshAsync();
    }

    /// <summary>La page s'en va : les relectures prévues s'arrêtent.</summary>
    public void Close()
    {
        IsOpen = false;
        CancelFollowUps();
    }

    /// <summary>
    /// Un texte partagé depuis une autre application (Deezer : « Partager »). Le
    /// premier lien Deezer qu'il contient remplit le champ ; sans lien Deezer, on
    /// le dit, et le champ garde ce qu'il avait.
    /// </summary>
    public void OfferSharedText(string? text)
    {
        var link = SoundReborn.Core.Models.DeezerLink.Find(text);

        if (link is null)
        {
            LinkMessageIsError = true;
            LinkMessage = Localization.Get("S_DeezerShareNoLink");
            return;
        }

        LinkText = link;
        LinkMessageIsError = false;
        LinkMessage = Localization.Get("S_DeezerShareReceived");
    }

    // ---------------------------------------------------------------- Lecture de l'état

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        var version = ++_loadVersion;
        var device = Device;
        var agent = device?.Str;

        SpeakerName = device?.Endpoint.DisplayName ?? "";
        HasAgent = agent is not null;
        NoAgentNote = device is null
            ? Localization.Get("S_NoSpeaker")
            : agent is null ? Localization.Get("S_StreamingNeedsAgent") : "";

        if (agent is null)
        {
            ClearSpotify();
            ClearDeezer();
            DeezerSupported = false;
            DeezerUnsupported = false;
            return;
        }

        IsLoading = true;

        try
        {
            var spotify = ReadSpotifyAsync(agent);
            var deezer = ReadDeezerAsync(agent);
            await Task.WhenAll(spotify, deezer).ConfigureAwait(true);

            if (version != _loadVersion)
            {
                return;
            }

            ApplySpotify(spotify.Result);
            ApplyDeezerRead(deezer.Result);
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private static async Task<StrSpotifyInfo?> ReadSpotifyAsync(StrApiClient agent)
    {
        try
        {
            return await agent.GetSpotifyInfoAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<StrDeezerState?> ReadDeezerAsync(StrApiClient agent)
    {
        try
        {
            return await agent.GetDeezerStateAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- Spotify

    private void ClearSpotify()
    {
        SpotifyState = "";
        HasSpotifyRefusal = false;
        SpotifyRefusal = "";
        SpotifyRefusalHelp = "";
    }

    /// <summary>
    /// L'état du Spotify de STR sur l'enceinte. Les phrases du refus sont celles
    /// des applications de STR : un compte gratuit n'a pas la même explication
    /// qu'un compte Premium que Spotify refuse.
    /// </summary>
    private void ApplySpotify(StrSpotifyInfo? info)
    {
        if (info is null)
        {
            ClearSpotify();
            SpotifyState = Localization.Get("S_SpotifyNoEngine");
            return;
        }

        var account = info.AccountLabel;

        if (account.Length == 0)
        {
            SpotifyState = Localization.Get("S_SpotifyNoAccount");
        }
        else
        {
            var plan = info.IsPremium ? Localization.Get("L_SpotifyPlanPremium")
                : info.IsFree ? Localization.Get("L_SpotifyPlanFree")
                : "";

            SpotifyState = plan.Length == 0
                ? Localization.Get("S_SpotifyAccount", account)
                : Localization.Get("S_SpotifyAccountPlan", account, plan);
        }

        HasSpotifyRefusal = info.IsRefused;

        if (!info.IsRefused)
        {
            SpotifyRefusal = "";
            SpotifyRefusalHelp = "";
            return;
        }

        // Le nom n'est donné que pour un refus mémorisé par l'enceinte.
        var name = info.AccountRefused ? info.AccountRefusedName : "";

        if (info.IsFree)
        {
            SpotifyRefusal = name.Length > 0
                ? Localization.Get("S_SpotifyRefusedFreeNamed", name)
                : Localization.Get("S_SpotifyRefusedFree");
            SpotifyRefusalHelp = Localization.Get("S_SpotifyRefusedFreeWhy") + " " + Localization.Get("S_SpotifyRefusedFreeTry");
        }
        else
        {
            SpotifyRefusal = name.Length > 0
                ? Localization.Get("S_SpotifyRefusedNamed", name)
                : Localization.Get("S_SpotifyRefused");
            SpotifyRefusalHelp = Localization.Get("S_SpotifyRefusedWhy") + " " + Localization.Get("S_SpotifyRefusedTry");
        }
    }

    /// <summary>
    /// Ouvre l'application Spotify : c'est là qu'on choisit l'enceinte (l'entrée qui
    /// se termine par « (STR) ») et qu'on lance une playlist.
    /// </summary>
    [RelayCommand]
    private async Task OpenSpotifyAsync()
    {
        SpotifyMessage = "";

        try
        {
            if (await Launcher.Default.TryOpenAsync(new Uri("spotify:")).ConfigureAwait(true))
            {
                return;
            }
        }
        catch (Exception)
        {
            // Pas d'application pour ce lien : on le dit ci-dessous.
        }

        SpotifyMessageIsError = true;
        SpotifyMessage = Localization.Get("S_SpotifyNotInstalled");
    }

    // ---------------------------------------------------------------- Deezer : carte

    private void ClearDeezer()
    {
        DeezerState = "";
        DeezerStuck = false;
        DeezerRejected = false;
        CanDisconnectDeezer = false;
    }

    /// <summary>Une lecture de l'état : sans réponse lisible, l'agent ne connaît pas Deezer.</summary>
    private void ApplyDeezerRead(StrDeezerState? state)
    {
        if (state is null || !state.IsKnown)
        {
            ClearDeezer();
            DeezerSupported = false;
            DeezerUnsupported = true;
            return;
        }

        DeezerSupported = true;
        DeezerUnsupported = false;
        ApplyDeezer(state);
    }

    private void ApplyDeezer(StrDeezerState state)
    {
        DeezerState = DescribeDeezer(state);
        DeezerStuck = state.IsStuck;
        DeezerRejected = state.ShowRejected;
        CanDisconnectDeezer = state.CanDisconnect;
    }

    /// <summary>Le nom de l'état que l'agent a décidé, avec les phrases de la page téléphone de STR.</summary>
    public static string DescribeDeezer(StrDeezerState state) => state.State switch
    {
        StrDeezerState.Connected => state.Account.Length > 0
            ? Localization.Get("S_DeezerConnected", state.Account)
            : Localization.Get("S_DeezerConnectedNoName"),
        StrDeezerState.Connecting => Localization.Get("S_DeezerConnecting"),
        StrDeezerState.Failed => state.Reason switch
        {
            "refused" => Localization.Get("S_DeezerRefused", state.Detail),
            "no-answer" => Localization.Get("S_DeezerNoAnswer"),
            _ => Localization.Get("S_DeezerNotReady", state.Detail),
        },
        StrDeezerState.Unavailable => Localization.Get("S_DeezerUnavailableState"),
        StrDeezerState.NotConnected => Localization.Get("S_DeezerNotConnected"),
        _ => Localization.Get("S_DeezerUnknown"),
    };

    /// <summary>
    /// « Vérifier à nouveau » : quand l'agent garde une connexion Deezer que
    /// l'enceinte a perdue, il la lui rend. L'enceinte met jusqu'à une minute et
    /// demie à se reconnecter : la carte se relit alors d'elle-même.
    /// </summary>
    [RelayCommand]
    private async Task RecheckDeezerAsync()
    {
        var agent = Device?.Str;

        if (agent is null || IsDeezerBusy)
        {
            return;
        }

        IsDeezerBusy = true;
        DeezerMessage = "";

        try
        {
            StrDeezerState state;

            try
            {
                state = await agent.RecheckDeezerAsync().ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Comme la page téléphone : sans réponse à la vérification, on relit l'état.
                state = await agent.GetDeezerStateAsync().ConfigureAwait(true);
            }

            if (!ReferenceEquals(agent, Device?.Str))
            {
                return;
            }

            ApplyDeezerRead(state);

            if (state.RecreateRan)
            {
                ScheduleFollowUps(agent);
            }
        }
        catch (Exception ex)
        {
            DeezerMessageIsError = true;
            DeezerMessage = AgentMessage(ex);
        }
        finally
        {
            IsDeezerBusy = false;
        }
    }

    /// <summary>
    /// « Déconnecter » : l'enceinte oublie son compte Deezer. Sans le cloud Bose,
    /// aucun compte ne pourra être relié ensuite : on demande d'abord.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectDeezerAsync()
    {
        var agent = Device?.Str;

        if (agent is null || IsDeezerBusy)
        {
            return;
        }

        var sure = await Shell.Current.DisplayAlertAsync(
            Localization.Get("L_DeezerDisconnectTitle"),
            Localization.Get("S_DeezerDisconnectConfirm"),
            Localization.Get("L_DeezerDisconnect"),
            Localization.Get("L_Cancel")).ConfigureAwait(true);

        if (!sure)
        {
            return;
        }

        IsDeezerBusy = true;
        DeezerMessage = "";

        try
        {
            await agent.DisconnectDeezerAsync().ConfigureAwait(true);
            DeezerMessageIsError = false;
            DeezerMessage = Localization.Get("S_DeezerDisconnected");
        }
        catch (Exception ex)
        {
            DeezerMessageIsError = true;
            DeezerMessage = Localization.Get("S_DeezerDisconnectFailed", AgentMessage(ex));
        }
        finally
        {
            IsDeezerBusy = false;
        }

        // Ce que l'enceinte dit maintenant, réussite ou non.
        if (ReferenceEquals(agent, Device?.Str))
        {
            ApplyDeezerRead(await ReadDeezerAsync(agent).ConfigureAwait(true));
        }
    }

    private void ScheduleFollowUps(StrApiClient agent)
    {
        CancelFollowUps();

        var cts = new CancellationTokenSource();
        _followUps = cts;
        _ = FollowUpAsync(agent, cts.Token);
    }

    private async Task FollowUpAsync(StrApiClient agent, CancellationToken ct)
    {
        try
        {
            var elapsed = TimeSpan.Zero;

            foreach (var at in RecheckFollowUps)
            {
                var wait = at - elapsed;

                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct).ConfigureAwait(true);
                }

                elapsed = at;

                if (ct.IsCancellationRequested || !ReferenceEquals(agent, Device?.Str))
                {
                    return;
                }

                var state = await ReadDeezerAsync(agent).ConfigureAwait(true);

                if (ct.IsCancellationRequested || !ReferenceEquals(agent, Device?.Str))
                {
                    return;
                }

                ApplyDeezerRead(state);
            }
        }
        catch (OperationCanceledException)
        {
            // Page fermée ou nouvelle vérification : ces relectures n'ont plus lieu d'être.
        }
    }

    private void CancelFollowUps()
    {
        _followUps?.Cancel();
        _followUps = null;
    }

    // ---------------------------------------------------------------- Deezer : lien

    /// <summary>Colle le presse-papiers dans le champ du lien (le premier lien Deezer qu'il contient, s'il y en a un).</summary>
    [RelayCommand]
    private async Task PasteLinkAsync()
    {
        string? text = null;

        try
        {
            text = await Clipboard.Default.GetTextAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Presse-papiers illisible : comme vide.
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            LinkMessageIsError = true;
            LinkMessage = Localization.Get("S_ClipboardEmpty");
            return;
        }

        LinkText = SoundReborn.Core.Models.DeezerLink.Find(text) ?? text.Trim();
        LinkMessage = "";
    }

    /// <summary>Fait jouer le lien par le client Deezer de l'enceinte.</summary>
    [RelayCommand]
    private async Task PlayLinkAsync()
    {
        var agent = Device?.Str;
        var link = LinkText.Trim();

        if (agent is null || IsLinkBusy)
        {
            return;
        }

        if (!SoundReborn.Core.Models.DeezerLink.IsPlayable(link))
        {
            LinkMessageIsError = true;
            LinkMessage = Localization.Get("S_DeezerNotALink");
            return;
        }

        IsLinkBusy = true;
        LinkMessageIsError = false;
        LinkMessage = Localization.Get("S_DeezerStarting");

        try
        {
            var played = await agent.PlayDeezerAsync(link).ConfigureAwait(true);
            var name = played.Name.Length > 0 ? played.Name : "Deezer";

            LinkMessageIsError = false;
            LinkMessage = Localization.Get("S_DeezerStarted", name);
        }
        catch (Exception ex)
        {
            LinkMessageIsError = true;
            LinkMessage = StrRefusal.From(ex)?.Code switch
            {
                "deezer-not-linked" => Localization.Get("S_DeezerNotLinked"),
                "native-service-unavailable" => Localization.Get("S_DeezerServiceUnavailable"),
                "deezer-link-unresolved" => Localization.Get("S_DeezerUnresolved"),
                _ => Localization.Get("S_DeezerPlayFailed", AgentMessage(ex)),
            };
        }
        finally
        {
            IsLinkBusy = false;
        }
    }

    // ---------------------------------------------------------------- Garder sur une touche

    /// <summary>
    /// Garde ce que l'enceinte joue sur une touche choisie : une playlist Spotify ou
    /// Deezer, mais aussi une radio ou un dossier de la bibliothèque. Même chemin
    /// que « Garder ce qui joue sur une touche » de l'onglet Lecture.
    /// </summary>
    [RelayCommand]
    private async Task KeepPlayingAsync()
    {
        var device = Device;

        if (device is null)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_NoSpeaker");
            return;
        }

        if (device.Str is not { } agent)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_NeedAgentKeep");
            return;
        }

        if (IsKeepBusy)
        {
            return;
        }

        IsKeepBusy = true;

        try
        {
            var slot = await _presets.PickSlotAsync(Localization.Get("L_KeepPlayingTitle")).ConfigureAwait(true);

            if (slot is not { } key)
            {
                return;
            }

            var playing = await device.GetNowPlayingAsync().ConfigureAwait(true);
            var kept = await agent.KeepPlayingAsync(key, playing).ConfigureAwait(true);

            await _presets.RevealSlotAsync(key).ConfigureAwait(true);

            var name = kept.Name.Length > 0 ? kept.Name : playing.DisplayTitle;
            ShowInfo(Localization.Get(kept.Unchanged ? "S_KeptAlready" : "S_Kept", name, PresetKeys.Label(key)));
        }
        catch (SpeakerException ex) when (ex.StatusCode is { } code && (int)code == 422)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_NothingToKeep");
        }
        catch (Exception ex) when (IsKnownRefusal(ex))
        {
            ShowError(ex);
        }
        catch (Exception ex)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_KeepFailed", AgentMessage(ex));
        }
        finally
        {
            IsKeepBusy = false;
        }
    }
}

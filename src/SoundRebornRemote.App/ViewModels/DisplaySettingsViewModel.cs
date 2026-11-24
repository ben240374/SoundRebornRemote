using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>
/// La carte « Écran de l'enceinte » des Réglages, pour l'enceinte DÉSIGNÉE : les
/// mêmes réglages que « Réglages de l'enceinte, Son et affichage » de
/// l'application de bureau de STR, sur les mêmes points de l'agent.
///
/// Une ligne n'apparaît que si l'enceinte dit savoir la faire ; une enceinte sans
/// écran (SoundTouch 10) ne montre pas la carte du tout. Comme sur la page
/// téléphone de STR, chaque case s'enregistre dès qu'on la touche : il n'y a pas
/// de bouton « Enregistrer ». Un enregistrement refusé relit la carte, qui montre
/// alors ce que l'enceinte a vraiment.
/// </summary>
public sealed partial class DisplaySettingsViewModel : BaseViewModel
{
    private int _loadVersion;

    /// <summary>Vrai pendant qu'on pose les valeurs lues : les cases ne doivent rien envoyer.</summary>
    private bool _applying;

    private StrApiClient? _agent;

    private string? _host;

    public DisplaySettingsViewModel(SpeakerManager speakers) : base(speakers)
    {
    }

    /// <summary>Au moins un réglage est proposé : la carte s'affiche.</summary>
    [ObservableProperty]
    private bool _isAvailable;

    [ObservableProperty]
    private bool _trackVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeRowVisible))]
    private bool _trackEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeArtist))]
    [NotifyPropertyChangedFor(nameof(ModeTitle))]
    [NotifyPropertyChangedFor(nameof(ModeBoth))]
    private string _trackMode = StrDisplaySetting.ModeBoth;

    [ObservableProperty]
    private bool _clockVisible;

    [ObservableProperty]
    private bool _clockEnabled;

    [ObservableProperty]
    private bool _messagesVisible;

    [ObservableProperty]
    private bool _messagesEnabled;

    [ObservableProperty]
    private bool _noticesVisible;

    [ObservableProperty]
    private bool _noticesEnabled;

    [ObservableProperty]
    private bool _splashVisible;

    [ObservableProperty]
    private bool _splashEnabled;

    [ObservableProperty]
    private string _identifyMessage = "";

    [ObservableProperty]
    private bool _identifyIsError;

    [ObservableProperty]
    private bool _isIdentifying;

    /// <summary>Le choix artiste / titre / les deux n'a de sens que titre en cours activé.</summary>
    public bool ModeRowVisible => TrackVisible && TrackEnabled;

    public bool ModeArtist => TrackMode == StrDisplaySetting.ModeArtist;

    public bool ModeTitle => TrackMode == StrDisplaySetting.ModeTitle;

    public bool ModeBoth => TrackMode == StrDisplaySetting.ModeBoth;

    partial void OnTrackVisibleChanged(bool value) => OnPropertyChanged(nameof(ModeRowVisible));

    // ---------------------------------------------------------------- Lecture

    /// <summary>
    /// Relit les réglages de l'enceinte désignée. Une lecture dépassée par une
    /// autre — l'utilisateur a changé d'enceinte entre-temps — est jetée.
    /// </summary>
    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        var target = Speakers.SelectedEndpoint ?? Device?.Endpoint;
        var agent = AgentFor(target);

        if (!string.Equals(_host, target?.Host, StringComparison.OrdinalIgnoreCase))
        {
            // Autre enceinte : ses messages ne concernent pas celle-ci.
            IdentifyMessage = "";
            StatusMessage = null;
        }

        _host = target?.Host;

        if (agent is null)
        {
            _agent = null;
            Apply(Array.Empty<StrDisplaySetting>());
            return;
        }

        var reads = StrDisplaySetting.Kinds.Select(kind => ReadAsync(agent, kind)).ToList();
        await Task.WhenAll(reads).ConfigureAwait(true);

        if (version != _loadVersion)
        {
            return;
        }

        _agent = agent;
        Apply(reads.Select(r => r.Result).ToList());
    }

    private static async Task<StrDisplaySetting> ReadAsync(StrApiClient agent, string kind)
    {
        try
        {
            return await agent.GetDisplaySettingAsync(kind).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Agent qui ne connaît pas ce réglage (404), ou qui ne répond pas : la ligne reste cachée.
            return new StrDisplaySetting { Kind = kind };
        }
    }

    private void Apply(IReadOnlyCollection<StrDisplaySetting> settings)
    {
        StrDisplaySetting Of(string kind)
            => settings.FirstOrDefault(s => s.Kind == kind) ?? new StrDisplaySetting { Kind = kind };

        var track = Of(StrDisplaySetting.Track);
        var clock = Of(StrDisplaySetting.Clock);
        var messages = Of(StrDisplaySetting.Messages);
        var notices = Of(StrDisplaySetting.Notices);
        var splash = Of(StrDisplaySetting.Splash);

        _applying = true;

        try
        {
            TrackVisible = track.Supported;
            TrackEnabled = track.Supported && track.Enabled;
            TrackMode = track.Mode;
            ClockVisible = clock.Supported;
            ClockEnabled = clock.Supported && clock.Enabled;
            MessagesVisible = messages.Supported;
            MessagesEnabled = messages.Supported && messages.Enabled;
            NoticesVisible = notices.Supported;
            NoticesEnabled = notices.Supported && notices.Enabled;
            SplashVisible = splash.Supported;
            SplashEnabled = splash.Supported && splash.Enabled;
        }
        finally
        {
            _applying = false;
        }

        IsAvailable = TrackVisible || ClockVisible || MessagesVisible || NoticesVisible || SplashVisible;
    }

    // ---------------------------------------------------------------- Enregistrement

    partial void OnTrackEnabledChanged(bool value) => Save(StrDisplaySetting.Track, value);

    partial void OnClockEnabledChanged(bool value) => Save(StrDisplaySetting.Clock, value);

    partial void OnMessagesEnabledChanged(bool value) => Save(StrDisplaySetting.Messages, value);

    partial void OnNoticesEnabledChanged(bool value) => Save(StrDisplaySetting.Notices, value);

    partial void OnSplashEnabledChanged(bool value) => Save(StrDisplaySetting.Splash, value);

    /// <summary>Artiste, titre ou les deux : enregistré tout de suite, titre en cours activé.</summary>
    [RelayCommand]
    private void SetMode(string? mode)
    {
        if (mode is not (StrDisplaySetting.ModeArtist or StrDisplaySetting.ModeTitle or StrDisplaySetting.ModeBoth) ||
            !TrackVisible)
        {
            return;
        }

        if (mode == TrackMode && TrackEnabled)
        {
            return;
        }

        _applying = true;

        try
        {
            TrackMode = mode;
            TrackEnabled = true;
        }
        finally
        {
            _applying = false;
        }

        Save(StrDisplaySetting.Track, true);
    }

    private void Save(string kind, bool enabled)
    {
        if (_applying || _agent is null)
        {
            return;
        }

        _ = SaveAsync(_agent, kind, enabled, kind == StrDisplaySetting.Track ? TrackMode : null);
    }

    private async Task SaveAsync(StrApiClient agent, string kind, bool enabled, string? mode)
    {
        try
        {
            await agent.SetDisplaySettingAsync(kind, enabled, mode).ConfigureAwait(true);

            if (ReferenceEquals(agent, _agent))
            {
                ShowInfo(Localization.Get("S_DisplaySaved"));
            }
        }
        catch (Exception)
        {
            if (!ReferenceEquals(agent, _agent))
            {
                return;
            }

            IsError = true;
            StatusMessage = Localization.Get("S_DisplaySaveFailed");

            // La carte montre ce que l'enceinte a vraiment, pas ce qu'on a voulu.
            await LoadAsync().ConfigureAwait(true);
        }
    }

    // ---------------------------------------------------------------- Trouver l'enceinte

    /// <summary>« Trouver cette enceinte » : HELLO et son nom sur l'écran pendant quelques secondes.</summary>
    [RelayCommand]
    private async Task IdentifyAsync()
    {
        var agent = _agent;

        if (agent is null || IsIdentifying)
        {
            return;
        }

        IsIdentifying = true;

        try
        {
            var outcome = await agent.IdentifyAsync().ConfigureAwait(true);

            IdentifyIsError = outcome is StrIdentifyOutcome.Busy or StrIdentifyOutcome.Failed;
            IdentifyMessage = outcome switch
            {
                StrIdentifyOutcome.Shown => Localization.Get("S_IdentifyShown"),
                StrIdentifyOutcome.NoDisplay => Localization.Get("S_IdentifyNoDisplay"),
                StrIdentifyOutcome.Busy => Localization.Get("S_IdentifyBusy"),
                _ => Localization.Get("S_IdentifyFailed"),
            };
        }
        catch (Exception)
        {
            IdentifyIsError = true;
            IdentifyMessage = Localization.Get("S_IdentifyFailed");
        }
        finally
        {
            IsIdentifying = false;
        }
    }

    private StrApiClient? AgentFor(SoundReborn.Core.Models.SpeakerEndpoint? target)
    {
        if (target is null)
        {
            return null;
        }

        var device = Device;

        if (device is not null && string.Equals(target.Host, device.Host, StringComparison.OrdinalIgnoreCase))
        {
            return device.Str;
        }

        return target.StrPort is int port ? new StrApiClient(target.Host, port, Speakers.Http) : null;
    }
}

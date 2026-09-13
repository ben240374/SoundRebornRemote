using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>
/// Choix de l'enceinte et de la langue, plus un bloc de diagnostic pour
/// comprendre ce que l'enceinte expose réellement.
/// </summary>
public sealed partial class SettingsViewModel : BaseViewModel
{
    private readonly SpeakerDiscovery _discovery;
    private readonly ArtworkCache _artwork;
    private bool _suppressLanguageApply;
    private bool _suppressThemeApply;

    public SettingsViewModel(SpeakerManager speakers, SpeakerDiscovery discovery, ArtworkCache artwork) : base(speakers)
    {
        _discovery = discovery;
        _artwork = artwork;

        foreach (var speaker in Speakers.KnownSpeakers)
        {
            KnownSpeakers.Add(speaker);
        }

        foreach (var language in Localization.Available)
        {
            LanguageNames.Add(Localization.NameOf(language));
        }

        // Le Picker travaille sur des chaînes et un index : pas de liaison sur un
        // type d'élément, donc rien que le compilateur XAML puisse mal résoudre.
        _suppressLanguageApply = true;
        SelectedLanguageIndex = Localization.Available.ToList().IndexOf(Localization.Current);
        _suppressLanguageApply = false;

        BuildThemeLabels();
        RefreshStrGap();

        CurrentSpeakerLabel = Localization.Get("L_NoSpeakerSelected");
        Localization.LanguageChanged += (_, _) => OnMainThread(RefreshLabels);
    }

    // ---------------------------------------------------------------- Thème

    public ObservableCollection<string> ThemeNames { get; } = new();

    [ObservableProperty]
    private int _selectedThemeIndex = -1;

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_suppressThemeApply || value < 0 || value >= ThemeService.Available.Count)
        {
            return;
        }

        var choice = ThemeService.Available[value];

        if (choice != ThemeService.Current)
        {
            ThemeService.Set(choice);
        }
    }

    private void BuildThemeLabels()
    {
        _suppressThemeApply = true;

        try
        {
            ThemeNames.Clear();

            foreach (var choice in ThemeService.Available)
            {
                ThemeNames.Add(Localization.Get(ThemeService.LabelKey(choice)));
            }

            SelectedThemeIndex = ThemeService.Available.ToList().IndexOf(ThemeService.Current);
        }
        finally
        {
            _suppressThemeApply = false;
        }
    }

    // ------------------------------------------ Enceintes sans agent STR

    /// <summary>
    /// Vrai dès qu'une enceinte connue répond sur le port 8090 sans qu'aucun agent
    /// STR n'écoute. Le balayage relève déjà l'information ; ce bloc la rend visible
    /// et renvoie vers l'outil d'installation, qui n'est pas celui-ci.
    /// </summary>
    [ObservableProperty]
    private bool _hasSpeakersWithoutStr;

    [ObservableProperty]
    private string _strMissingSummary = "";

    private void RefreshStrGap()
    {
        var missing = KnownSpeakers.Count(s => s.NeedsStr);

        HasSpeakersWithoutStr = missing > 0;

        StrMissingSummary = missing switch
        {
            0 => "",
            1 => Localization.Get("L_StrMissingOne"),
            _ => Localization.Get("L_StrMissingMany", missing),
        };
    }

    [RelayCommand]
    private void ClearArtCache()
    {
        _artwork.Clear();
        ShowInfo(Localization.Get("S_ArtCacheCleared"));
    }

    // ---------------------------------------------------------------- À propos

    public const string StrSourceCode = "https://github.com/JRpersonal/streborn";

    /// <summary>
    /// Le site STR est traduit et sert chaque langue sous son propre chemin.
    /// On vise directement celui de la langue choisie dans l'application, au lieu
    /// de laisser le navigateur décider selon ses propres préférences.
    /// </summary>
    public string StrWebsite => Localization.Current switch
    {
        AppLanguage.French => "https://st-reborn.de/fr/",
        AppLanguage.German => "https://st-reborn.de/de/",
        AppLanguage.Dutch => "https://st-reborn.de/nl/",
        AppLanguage.Spanish => "https://st-reborn.de/es/",
        _ => "https://st-reborn.de/",
    };

    /// <summary>« SoundReborn Remote V0.9.79 » — le code de version Android n'intéresse personne.</summary>
    public string AppVersion
    {
        get
        {
            try
            {
                return $"{AppInfo.Current.Name} V{AppInfo.Current.VersionString}";
            }
            catch (Exception)
            {
                return "SoundReborn Remote";
            }
        }
    }

    [RelayCommand]
    private async Task OpenUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(new Uri(url)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    public ObservableCollection<SpeakerEndpoint> KnownSpeakers { get; } = new();

    public ObservableCollection<string> LanguageNames { get; } = new();

    [ObservableProperty]
    private int _selectedLanguageIndex = -1;

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (_suppressLanguageApply || value < 0 || value >= Localization.Available.Count)
        {
            return;
        }

        var language = Localization.Available[value];

        if (language != Localization.Current)
        {
            Localization.SetLanguage(language);
        }
    }

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _manualHost = "";

    [ObservableProperty]
    private string _currentSpeakerLabel = "";

    [ObservableProperty]
    private string _diagnostics = "";

    [ObservableProperty]
    private bool _hasDiagnostics;

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        ShowInfo(Localization.Get("S_Scanning"));

        try
        {
            var found = await _discovery.ScanAsync(speaker => OnMainThread(() =>
            {
                Speakers.Remember(speaker);

                if (!KnownSpeakers.Contains(speaker))
                {
                    KnownSpeakers.Add(speaker);
                }
            })).ConfigureAwait(true);

            Sync();

            ShowInfo(found.Count switch
            {
                0 => Localization.Get("S_NoneFound"),
                1 => Localization.Get("S_OneFound"),
                _ => Localization.Get("S_ManyFound", found.Count),
            });
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task AddManualAsync()
    {
        var host = ManualHost?.Trim();

        if (string.IsNullOrWhiteSpace(host))
        {
            ShowInfo(Localization.Get("S_EnterIp"));
            return;
        }

        IsScanning = true;

        try
        {
            var speaker = await _discovery.ProbeAsync(host).ConfigureAwait(true);

            if (speaker is null)
            {
                IsError = true;
                StatusMessage = Localization.Get("S_NothingAt", host);
                return;
            }

            Speakers.Remember(speaker);
            Sync();
            ManualHost = "";
            ShowInfo(Localization.Get("S_Added", speaker.DisplayName));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task SelectAsync(SpeakerEndpoint? speaker)
    {
        if (speaker is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var device = await Speakers.SelectAsync(speaker).ConfigureAwait(true);
            CurrentSpeakerLabel = $"{device.Endpoint.DisplayName} — {device.Endpoint.Summary}";
            ShowInfo(Localization.Get("S_Connected", device.Endpoint.DisplayName));
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

    [RelayCommand]
    private void Forget(SpeakerEndpoint? speaker)
    {
        if (speaker is null)
        {
            return;
        }

        Speakers.Forget(speaker);
        KnownSpeakers.Remove(speaker);
        RefreshStrGap();
        ShowInfo(Localization.Get("S_Removed", speaker.DisplayName));
    }

    /// <summary>Relit les informations de l'enceinte active et affiche ce qu'elle accepte.</summary>
    [RelayCommand]
    private async Task DiagnoseAsync()
    {
        var device = Device;

        if (device is null)
        {
            ShowInfo(Localization.Get("S_SelectFirst"));
            return;
        }

        IsBusy = true;

        try
        {
            var info = await device.Bose.GetInfoAsync().ConfigureAwait(true);

            var agent = device.HasStr
                ? Localization.Get("D_AgentYes", device.Endpoint.StrPort ?? 0)
                : Localization.Get("D_AgentNo");

            var bass = device.BassCapabilities.Available
                ? Localization.Get("D_BassRange",
                    device.BassCapabilities.Min,
                    device.BassCapabilities.Max,
                    device.BassCapabilities.Default)
                : Localization.Get("D_BassNone");

            var lines = new List<string>
            {
                $"{Localization.Get("D_Name")} : {info.Name}",
                $"{Localization.Get("D_Model")} : {info.Type}",
                $"deviceID : {info.DeviceId}",
                $"{Localization.Get("D_Firmware")} : {info.SoftwareVersion ?? "?"}",
                $"{Localization.Get("D_Address")} : {device.Host}",
                $"{Localization.Get("D_Agent")} : {agent}",
                $"{Localization.Get("D_Notifications")} : {Localization.Get(device.Notifications.IsConnected ? "D_WsUp" : "D_WsDown")}",
                $"{Localization.Get("L_Bass")} : {bass}",
            };

            try
            {
                var urls = await device.Bose.GetSupportedUrlsAsync().ConfigureAwait(true);
                lines.Add($"{Localization.Get("D_Endpoints")} ({urls.Count}) : {string.Join(", ", urls.Take(30))}");
            }
            catch (Exception)
            {
                lines.Add($"{Localization.Get("D_Endpoints")} : {Localization.Get("D_EndpointsNone")}");
            }

            Diagnostics = string.Join(Environment.NewLine, lines);
            HasDiagnostics = true;
            ShowInfo(null);
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

    /// <summary>Reconnecte l'enceinte mémorisée au lancement de l'application.</summary>
    public async Task InitializeAsync()
    {
        Sync();

        if (Device is not null)
        {
            CurrentSpeakerLabel = $"{Device.Endpoint.DisplayName} — {Device.Endpoint.Summary}";
            return;
        }

        var device = await Speakers.TryRestoreAsync().ConfigureAwait(true);

        if (device is not null)
        {
            CurrentSpeakerLabel = $"{device.Endpoint.DisplayName} — {device.Endpoint.Summary}";
            ShowInfo(null);
        }
    }

    private void RefreshLabels()
    {
        BuildThemeLabels();
        RefreshStrGap();

        // Le lien « Site du projet STR » suit la langue de l'interface.
        OnPropertyChanged(nameof(StrWebsite));

        if (Device is null)
        {
            CurrentSpeakerLabel = Localization.Get("L_NoSpeakerSelected");
        }

        // Le diagnostic déjà affiché reste dans son ancienne langue : on l'efface
        // plutôt que de laisser un texte à moitié traduit.
        Diagnostics = "";
        HasDiagnostics = false;
        ShowInfo(null);
    }

    private void Sync()
    {
        foreach (var speaker in Speakers.KnownSpeakers)
        {
            if (!KnownSpeakers.Contains(speaker))
            {
                KnownSpeakers.Add(speaker);
            }
        }

        for (var i = KnownSpeakers.Count - 1; i >= 0; i--)
        {
            if (!Speakers.KnownSpeakers.Contains(KnownSpeakers[i]))
            {
                KnownSpeakers.RemoveAt(i);
            }
        }

        RefreshStrGap();
    }
}

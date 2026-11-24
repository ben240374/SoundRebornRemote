using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>
/// Mise à jour de l'application depuis les versions publiées sur GitHub : la carte
/// « Mise à jour » des Réglages et la bannière de l'onglet Lecture.
///
/// - Au démarrage, une vérification au plus une fois par jour, qu'on peut couper.
/// - « Vérifier maintenant » fait la même chose à la demande.
/// - « Installer » télécharge l'APK de la version, contrôle sa taille et son
///   empreinte SHA-256 contre celles que GitHub publie, puis ouvre l'installateur
///   d'Android : l'utilisateur confirme lui-même, et Android refuse un APK signé
///   d'une autre clé que l'application installée.
/// - Sans installation possible (version Google Play, ou APK absent de la version),
///   « Installer » ouvre la page de la version.
///
/// La version Google Play (constante PLAY_STORE) n'a ni la carte ni la bannière :
/// le Play Store s'occupe des mises à jour, et Play interdit tout autre moyen.
/// </summary>
public sealed partial class UpdateViewModel : BaseViewModel
{
    /// <summary>« Vérifier automatiquement » ; la même clé sert à l'agent STR (AgentUpdateViewModel).</summary>
    public const string AutoKey = "update_auto";
    private const string LastCheckKey = "update_last_check";
    private const string SkippedKey = "update_skipped";

    /// <summary>Délai minimal entre deux vérifications automatiques.</summary>
    public static readonly TimeSpan AutoInterval = TimeSpan.FromHours(24);

    private readonly AppUpdateClient _client;
    private readonly IAppInstaller _installer;
    private readonly Version _current;
    private readonly string _directory;
    private readonly TimeProvider _clock;
    private CancellationTokenSource? _downloadCts;

    public UpdateViewModel(SpeakerManager speakers, HttpClient http, IAppInstaller installer)
        : this(speakers, new AppUpdateClient(http), installer, AppInfo.Current.VersionString,
            Path.Combine(FileSystem.CacheDirectory, "updates"), TimeProvider.System)
    {
    }

    public UpdateViewModel(
        SpeakerManager speakers,
        AppUpdateClient client,
        IAppInstaller installer,
        string currentVersion,
        string directory,
        TimeProvider clock)
        : base(speakers)
    {
        _client = client;
        _installer = installer;
        _current = AppUpdateClient.ParseVersion(currentVersion) ?? new Version(0, 0, 0, 0);
        _directory = directory;
        _clock = clock;
        _autoCheck = Preferences.Default.Get(AutoKey, true);
        CurrentVersionText = Localization.Get("L_UpdateInstalled", currentVersion);
    }

    /// <summary>Faux dans la version Google Play : ni carte, ni bannière, ni vérification.</summary>
    public static bool IsAvailable =>
#if PLAY_STORE
        false;
#else
        true;
#endif

    public bool ShowCard => IsAvailable;

    public string CurrentVersionText { get; }

    /// <summary>« Vérifier automatiquement » : une fois par jour au plus, au démarrage.</summary>
    [ObservableProperty]
    private bool _autoCheck;

    partial void OnAutoCheckChanged(bool value) => Preferences.Default.Set(AutoKey, value);

    /// <summary>Relit le réglage, après la restauration d'une sauvegarde.</summary>
    public void ReloadAutoCheck() => AutoCheck = Preferences.Default.Get(AutoKey, true);

    /// <summary>La version plus récente trouvée, ou null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(ShowBanner))]
    [NotifyPropertyChangedFor(nameof(UpdateTitle))]
    private ReleaseInfo? _available;

    public bool HasUpdate => Available is not null;

    public string UpdateTitle => Available is null ? "" : Localization.Get("L_UpdateAvailable", Available.Version.ToString(3));

    /// <summary>« Plus tard » sur la bannière : elle ne revient pas pour cette version.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBanner))]
    private bool _bannerDismissed;

    public bool ShowBanner => IsAvailable && HasUpdate && !BannerDismissed && !IsDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBanner))]
    private bool _isDownloading;

    /// <summary>Avancement du téléchargement, de 0 à 1.</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    /// <summary>Après un échec : proposer la page de la version, d'où l'on peut installer à la main.</summary>
    [ObservableProperty]
    private bool _canOpenPage;

    /// <summary>
    /// Vérification au démarrage : si elle est permise et que la dernière date d'au
    /// moins un jour. Silencieuse : un échec ne dit rien, on réessaiera demain.
    /// </summary>
    public async Task AutoCheckAsync()
    {
        if (!IsAvailable || !AutoCheck || IsBusy || IsDownloading)
        {
            return;
        }

        var now = _clock.GetUtcNow();

        if (DateTimeOffset.TryParse(Preferences.Default.Get(LastCheckKey, ""), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var last) &&
            now - last < AutoInterval && now >= last)
        {
            return;
        }

        Preferences.Default.Set(LastCheckKey, now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            await FindAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Pas de réseau, GitHub indisponible : rien à dire au démarrage.
        }
    }

    /// <summary>« Vérifier maintenant ».</summary>
    [RelayCommand]
    private async Task CheckAsync()
    {
        if (!IsAvailable || IsBusy || IsDownloading)
        {
            return;
        }

        IsBusy = true;
        CanOpenPage = false;
        ShowInfo(Localization.Get("S_UpdateChecking"));

        try
        {
            Preferences.Default.Set(LastCheckKey, _clock.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            var found = await FindAsync().ConfigureAwait(true);

            // Une vérification demandée montre la bannière même après « Plus tard ».
            BannerDismissed = false;

            ShowInfo(found is null
                ? Localization.Get("S_UpdateNone", _current.ToString(3))
                : Localization.Get("S_UpdateFound", found.Version.ToString(3)));
        }
        catch (Exception)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_UpdateCheckFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Lit la dernière version ; la retient si elle est plus récente que celle installée.</summary>
    private async Task<ReleaseInfo?> FindAsync()
    {
        var latest = await _client.GetLatestAsync().ConfigureAwait(true);
        var newer = latest is not null && latest.Version > _current ? latest : null;

        Available = newer;
        BannerDismissed = newer is not null && Preferences.Default.Get(SkippedKey, "") == newer.Tag;

        return newer;
    }

    /// <summary>« Plus tard » : la bannière disparaît jusqu'à la version suivante.</summary>
    [RelayCommand]
    private void Later()
    {
        if (Available is not null)
        {
            Preferences.Default.Set(SkippedKey, Available.Tag);
        }

        BannerDismissed = true;
    }

    /// <summary>« Nouveautés » : la page de la version sur GitHub.</summary>
    [RelayCommand]
    private Task OpenPageAsync()
        => OpenAsync(Available?.PageUrl ?? new Uri($"https://github.com/{AppUpdateClient.Repository}/releases/latest"));

    /// <summary>« Installer » : téléchargement contrôlé, puis installateur d'Android.</summary>
    [RelayCommand]
    private async Task InstallAsync()
    {
        var release = Available;

        if (!IsAvailable || release is null || IsDownloading)
        {
            return;
        }

        // Pas d'installation depuis l'application : on ouvre la page de la version.
        if (release.Apk is null || !_installer.IsSupported)
        {
            await OpenAsync(release.PageUrl).ConfigureAwait(true);
            return;
        }

        CanOpenPage = false;

        if (!await _installer.EnsurePermissionAsync().ConfigureAwait(true))
        {
            ShowInfo(Localization.Get("S_UpdateAllowInstall"));
            return;
        }

        _downloadCts = new CancellationTokenSource();
        IsDownloading = true;
        Progress = 0;
        ProgressText = Localization.Get("L_UpdateDownloading", 0);
        ShowInfo(null);

        try
        {
            Directory.CreateDirectory(_directory);
            CleanDirectory();

            // Nom de fichier tiré du numéro de version, jamais du nom choisi sur GitHub.
            var path = Path.Combine(_directory, $"SoundRebornRemote-{release.Version.ToString(3)}.apk");
            var progress = new Progress<double>(p =>
            {
                Progress = Math.Clamp(p, 0, 1);
                ProgressText = Localization.Get("L_UpdateDownloading", (int)Math.Round(Progress * 100));
            });

            await _client.DownloadAsync(release.Apk, path, progress, _downloadCts.Token).ConfigureAwait(true);

            _installer.Install(path);
            ShowInfo(Localization.Get("S_UpdateInstallerOpened"));
        }
        catch (OperationCanceledException) when (_downloadCts.IsCancellationRequested)
        {
            ShowInfo(Localization.Get("S_UpdateCancelled"));
        }
        catch (Exception ex)
        {
            IsError = true;
            StatusMessage = ex is InvalidDataException
                ? Localization.Get("S_UpdateBadFile")
                : Localization.Get("S_UpdateDownloadFailed");
            CanOpenPage = true;
        }
        finally
        {
            IsDownloading = false;
            _downloadCts = null;
        }
    }

    [RelayCommand]
    private void CancelDownload() => _downloadCts?.Cancel();

    /// <summary>Retire les APK d'avant : un seul téléchargement est gardé, le temps de l'installer.</summary>
    private void CleanDirectory()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory))
            {
                File.Delete(file);
            }
        }
        catch (Exception)
        {
            // Un reste dans le cache ne gêne pas.
        }
    }

    private async Task OpenAsync(Uri url)
    {
        try
        {
            await Launcher.Default.OpenAsync(url).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }
}

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>
/// La carte « Agent STR : nouvelle version » des Réglages : la dernière version de
/// l'agent publiée sur GitHub, et les enceintes connues dont l'agent est plus ancien.
///
/// - La version est lue au plus une fois par jour, à l'ouverture des Réglages, et
///   gardée : la carte se montre ensuite sans réseau. La case « Vérifier
///   automatiquement » de la carte « Mise à jour » coupe aussi cette lecture.
/// - Rien n'est installé d'ici : l'agent se met à jour avec l'application STR sur
///   ordinateur. La carte le dit, et ouvre la page des nouveautés.
/// - « Plus tard » cache la carte jusqu'à la version suivante de l'agent.
/// </summary>
public sealed partial class AgentUpdateViewModel : BaseViewModel
{
    private const string AutoKey = "update_auto";
    private const string LastCheckKey = "agent_update_last_check";
    private const string LatestKey = "agent_update_latest";
    private const string SkippedKey = "agent_update_skipped";

    /// <summary>Délai minimal entre deux lectures sur GitHub.</summary>
    public static readonly TimeSpan AutoInterval = TimeSpan.FromHours(24);

    private readonly AgentReleaseClient _client;
    private readonly TimeProvider _clock;
    private AgentRelease? _latest;
    private bool _checking;

    public AgentUpdateViewModel(SpeakerManager speakers, HttpClient http)
        : this(speakers, new AgentReleaseClient(http), TimeProvider.System)
    {
    }

    public AgentUpdateViewModel(SpeakerManager speakers, AgentReleaseClient client, TimeProvider clock)
        : base(speakers)
    {
        _client = client;
        _clock = clock;
        _latest = LoadCached();
        Speakers.KnownSpeakersChanged += (_, _) => OnMainThread(Recompute);
        Localization.LanguageChanged += (_, _) => OnMainThread(Recompute);
        Recompute();
    }

    /// <summary>Une enceinte connue a un agent plus ancien que la dernière version, pas écartée par « Plus tard ».</summary>
    [ObservableProperty]
    private bool _showCard;

    /// <summary>« La version 1.9.1 de l'agent STR est disponible. »</summary>
    [ObservableProperty]
    private string _summary = "";

    /// <summary>« Cuisine (1.8.3), Salon (1.9.0) ».</summary>
    [ObservableProperty]
    private string _speakersText = "";

    /// <summary>La dernière version connue, ou null.</summary>
    public AgentRelease? Latest => _latest;

    /// <summary>
    /// Lecture à l'ouverture des Réglages : si elle est permise et que la dernière
    /// date d'au moins un jour. Silencieuse : un échec ne dit rien, on réessaiera.
    /// </summary>
    public async Task AutoCheckAsync()
    {
        if (_checking || !Preferences.Default.Get(AutoKey, true))
        {
            return;
        }

        var now = _clock.GetUtcNow();

        if (DateTimeOffset.TryParse(Preferences.Default.Get(LastCheckKey, ""), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var last) &&
            now - last < AutoInterval && now >= last)
        {
            return;
        }

        _checking = true;

        try
        {
            var latest = await _client.GetLatestAsync().ConfigureAwait(true);

            // La date n'est retenue qu'après une lecture réussie : sans réseau à
            // l'ouverture, on réessaie à la suivante.
            Preferences.Default.Set(LastCheckKey, now.ToString("O", CultureInfo.InvariantCulture));

            if (latest is not null)
            {
                _latest = latest;
                Preferences.Default.Set(LatestKey, latest.Tag + "\n" + latest.PageUrl.AbsoluteUri);
                Recompute();
            }
        }
        catch (Exception)
        {
            // Pas de réseau, GitHub indisponible : rien à dire.
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>« Nouveautés » : la page de la version sur GitHub.</summary>
    [RelayCommand]
    private async Task OpenPageAsync()
    {
        try
        {
            await Launcher.Default.OpenAsync(_latest?.PageUrl ?? AgentReleaseClient.ReleasesPage).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>« Plus tard » : la carte revient avec la version suivante de l'agent.</summary>
    [RelayCommand]
    private void Later()
    {
        if (_latest is not null)
        {
            Preferences.Default.Set(SkippedKey, _latest.Tag);
        }

        Recompute();
    }

    /// <summary>Refait la liste des enceintes à mettre à jour, à partir des versions connues.</summary>
    public void Recompute()
    {
        var latest = _latest;

        if (latest is null || Preferences.Default.Get(SkippedKey, "") == latest.Tag)
        {
            Hide();
            return;
        }

        var outdated = Speakers.KnownSpeakers
            .Where(s => s.HasStr && AgentRequirement.Parse(s.AgentVersion) is { } version && version < latest.Version)
            .Select(s => $"{s.DisplayName} ({s.AgentVersion.Trim()})")
            .ToList();

        if (outdated.Count == 0)
        {
            Hide();
            return;
        }

        Summary = Localization.Get("L_AgentNewSummary", latest.Label);
        SpeakersText = Localization.Get("L_AgentNewSpeakers", string.Join(", ", outdated));
        ShowCard = true;
    }

    private void Hide()
    {
        ShowCard = false;
        Summary = "";
        SpeakersText = "";
    }

    /// <summary>
    /// La dernière version connue de l'agent quand elle est plus récente que
    /// <paramref name="version"/> ; null sinon, ou pour une version illisible. Pour la
    /// pastille de la liste des enceintes (onglet Lecture).
    /// </summary>
    public static AgentRelease? NewerThan(string? version)
        => LoadCached() is { } latest && AgentRequirement.Parse(version) is { } current && current < latest.Version
            ? latest
            : null;

    /// <summary>La dernière version lue, gardée d'une ouverture à l'autre : « v1.9.1\nhttps://… ».</summary>
    private static AgentRelease? LoadCached()
    {
        var saved = Preferences.Default.Get(LatestKey, "");
        var parts = saved.Split('\n');

        if (parts.Length != 2 || AppUpdateClient.ParseVersion(parts[0]) is not { } version)
        {
            return null;
        }

        var page = Uri.TryCreate(parts[1], UriKind.Absolute, out var url) &&
                   url.Scheme == Uri.UriSchemeHttps &&
                   url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                   url.AbsolutePath.StartsWith($"/{AgentReleaseClient.Repository}/", StringComparison.OrdinalIgnoreCase)
            ? url
            : AgentReleaseClient.ReleasesPage;

        return new AgentRelease
        {
            Version = new Version(version.Major, version.Minor, Math.Max(0, version.Build)),
            Tag = parts[0],
            PageUrl = page,
        };
    }
}

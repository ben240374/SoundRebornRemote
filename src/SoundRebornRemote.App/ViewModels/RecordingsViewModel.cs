using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundReborn.Core.Recording;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>Une ligne de la liste des enregistrements.</summary>
public sealed partial class RecordingRow : ObservableObject
{
    public required RecordingEntry Entry { get; init; }

    /// <summary>La station.</summary>
    public required string Title { get; init; }

    /// <summary>« 17/11 14:32 · 3 min 20 ».</summary>
    public required string Subtitle { get; init; }

    /// <summary>Vrai pendant l'écoute de ce fichier : le bouton ▶ devient ■, la barre de défilement apparaît.</summary>
    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>
    /// Position de la barre, en secondes : elle suit le lecteur, ou le doigt
    /// pendant un glissé.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    private double _positionSeconds;

    /// <summary>Fin de la barre : la durée du fichier, une seconde au moins.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    private double _durationSeconds = 1;

    /// <summary>« 01:23 / 04:50 » : position et durée de l'enregistrement.</summary>
    public string TimeText => $"{Clock(PositionSeconds, round: false)} / {Clock(DurationSeconds, round: true)}";

    /// <summary>Vrai pendant que le doigt tient la barre : le lecteur ne la déplace plus.</summary>
    public bool IsSeeking { get; set; }

    /// <summary>
    /// Début et fin d'un glissé sur la barre. Le Slider n'a pas de paramètre de
    /// commande : chaque ligne porte donc les siennes, reliées à la vue-modèle.
    /// </summary>
    public IRelayCommand? StartSeek { get; set; }

    public IRelayCommand? EndSeek { get; set; }

    private static string Clock(double seconds, bool round)
    {
        var whole = Math.Max(0, round ? (int)Math.Round(seconds) : (int)Math.Floor(seconds));
        return $"{whole / 60:00}:{whole % 60:00}";
    }
}

/// <summary>
/// La carte « Enregistrer » de l'onglet Sources : enregistrer la radio que joue
/// l'enceinte sélectionnée, 5 minutes au plus, et la liste des enregistrements
/// (10 au plus) avec écoute, partage et corbeille.
///
/// L'adresse du flux est recherchée à l'ouverture de la page et à chaque
/// rafraîchissement, pour l'affichage ; elle est recherchée À NOUVEAU au moment
/// de l'appui, parce que l'enceinte a pu changer de station entre-temps.
/// </summary>
public sealed partial class RecordingsViewModel : BaseViewModel
{
    private readonly RecordingService _service;
    private StreamResolution? _resolution;

    /// <summary>Rythme de mise à jour du compteur et de la barre pendant l'écoute.</summary>
    private static readonly TimeSpan PlaybackTick = TimeSpan.FromMilliseconds(250);

    /// <summary>Boucle de mise à jour pendant l'écoute ; null le reste du temps.</summary>
    private CancellationTokenSource? _ticker;

    /// <summary>L'enceinte n'a pas répondu à la dernière recherche : seul l'enregistrement est impossible.</summary>
    private bool _offline;

    public RecordingsViewModel(SpeakerManager speakers, RecordingService service) : base(speakers)
    {
        _service = service;
        _service.Changed += (_, _) => OnMainThread(ApplyServiceState);
        _service.Finished += (_, finished) => OnMainThread(() => OnFinished(finished));
        Localization.LanguageChanged += (_, _) => OnMainThread(RebuildLabels);
        ApplyServiceState();

        // Les enregistrements sont sur le téléphone : la liste s'affiche tout de
        // suite, sans attendre l'enceinte ni le réseau.
        ReloadItems();
    }

    public ObservableCollection<RecordingRow> Items { get; } = new();

    [ObservableProperty]
    private bool _canRecord;

    /// <summary>« Bel RTL — flux disponible », ou la raison pour laquelle on ne peut pas enregistrer.</summary>
    [ObservableProperty]
    private string _recordHint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecordButton))]
    private bool _isRecording;

    /// <summary>« 02:13 / 05:00 ».</summary>
    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCard))]
    private bool _hasItems;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCard))]
    private bool _hasAgent;

    /// <summary>La carte : utile avec un agent STR, ou dès qu'il existe des enregistrements à écouter.</summary>
    public bool ShowCard => HasAgent || HasItems;

    public bool ShowRecordButton => !IsRecording;

    /// <summary>
    /// Relit la liste depuis le téléphone, sans réseau. Appelé avant toute requête
    /// à l'enceinte, pour qu'une enceinte éteinte ne retarde pas la liste.
    /// </summary>
    public void ReloadList() => ReloadItems();

    /// <summary>Relit la liste et cherche ce qu'on peut enregistrer sur l'enceinte sélectionnée.</summary>
    public async Task RefreshAsync()
    {
        ReloadItems();
        ApplyServiceState();

        var device = Device;
        HasAgent = device?.Str is not null;

        try
        {
            _resolution = await ResolveAsync(device).ConfigureAwait(true);
            _offline = false;
        }
        catch (Exception)
        {
            // Enceinte éteinte ou hors réseau : la liste reste utilisable.
            _resolution = null;
            _offline = device?.Str is not null;
        }

        RebuildLabels();
    }

    /// <summary>Remet les textes dans la langue de l'interface.</summary>
    public void RebuildLabels()
    {
        CanRecord = _resolution?.CanRecord == true;

        if (Device is null)
        {
            RecordHint = Localization.Get("S_RecNoSpeaker");
        }
        else if (Device.Str is null)
        {
            RecordHint = Localization.Get("S_RecNeedAgent");
        }
        else if (_offline)
        {
            RecordHint = Localization.Get("S_RecOffline");
        }
        else if (_resolution is null)
        {
            RecordHint = Localization.Get("S_RecNoUrl");
        }
        else if (_resolution.CanRecord)
        {
            RecordHint = _service.Library.IsFull
                ? Localization.Get("S_RecFull")
                : Localization.Get("L_RecReady", _resolution.Station);
        }
        else
        {
            RecordHint = RefusalText(_resolution.Refusal, _resolution.Source);
        }

        ProgressText = FormatProgress(_service.Elapsed);
        ReloadItems();
    }

    [RelayCommand]
    private async Task RecordAsync()
    {
        if (_service.IsRecording)
        {
            return;
        }

        if (_service.Library.IsFull)
        {
            ShowWarning(Localization.Get("S_RecFull"));
            return;
        }

        var device = Device;

        if (device is null)
        {
            ShowWarning(Localization.Get("S_RecNoSpeaker"));
            return;
        }

        if (device.Str is null)
        {
            ShowWarning(Localization.Get("S_RecNeedAgent"));
            return;
        }

        try
        {
            _resolution = await ResolveAsync(device).ConfigureAwait(true);
            _offline = false;
        }
        catch (Exception)
        {
            _resolution = null;
            _offline = true;
            RebuildLabels();
            ShowWarning(Localization.Get("S_RecOffline"));
            return;
        }

        RebuildLabels();

        if (_resolution is not { CanRecord: true, Url: { } url })
        {
            ShowWarning(RecordHint);
            return;
        }

        var urls = _resolution.Candidates.Count > 0 ? _resolution.Candidates : new[] { url };
        var refusal = await _service.StartAsync(urls, _resolution.Station).ConfigureAwait(true);

        if (refusal is null)
        {
            ShowInfo(null);
        }
        else if (refusal == RecordingRefusal.Network && !string.IsNullOrWhiteSpace(_service.LastFailureDetail))
        {
            ShowWarning(Localization.Get("S_RecNetworkDetail", _service.LastFailureDetail!));
        }
        else
        {
            ShowWarning(RefusalText(refusal, _resolution.Source));
        }

        ApplyServiceState();
    }

    [RelayCommand]
    private Task StopAsync()
    {
        _service.Stop();
        return Task.CompletedTask;
    }

    /// <summary>▶ lance l'écoute ; sur le fichier déjà en cours, ■ l'arrête.</summary>
    [RelayCommand]
    private Task PlayAsync(RecordingRow? row)
    {
        if (row is null)
        {
            return Task.CompletedTask;
        }

        var platform = _service.Platform;

        try
        {
            if (platform.IsPlaying && SamePath(platform.PlayingPath, row.Entry.Path))
            {
                platform.StopPlayback();
            }
            else
            {
                platform.Play(row.Entry.Path, () => OnMainThread(ApplyPlaying));
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }

        ApplyPlaying();
        return Task.CompletedTask;
    }

    /// <summary>Le doigt prend la barre : le lecteur ne la déplace plus jusqu'au lâcher.</summary>
    [RelayCommand]
    private void SeekStarted(RecordingRow? row)
    {
        if (row is not null)
        {
            row.IsSeeking = true;
        }
    }

    /// <summary>
    /// Le doigt lâche la barre : la lecture saute à cet endroit, borné à la durée
    /// du fichier. Sans effet sur une ligne qui n'est pas en lecture.
    /// </summary>
    [RelayCommand]
    private void SeekCompleted(RecordingRow? row)
    {
        if (row is null)
        {
            return;
        }

        row.IsSeeking = false;
        var platform = _service.Platform;

        if (!row.IsPlaying || !platform.IsPlaying || !SamePath(platform.PlayingPath, row.Entry.Path))
        {
            UpdatePlayback();
            return;
        }

        var target = Math.Clamp(row.PositionSeconds, 0, row.DurationSeconds);

        try
        {
            platform.SeekTo(TimeSpan.FromSeconds(target));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }

        row.PositionSeconds = target;
    }

    /// <summary>
    /// Met à jour le compteur et la barre de la ligne en lecture ; remet les autres
    /// au début. Appelée à chaque changement de lecture et quatre fois par seconde
    /// pendant l'écoute.
    /// </summary>
    public void UpdatePlayback()
    {
        var platform = _service.Platform;

        foreach (var row in Items)
        {
            if (row.IsPlaying)
            {
                var duration = platform.PlaybackDuration > TimeSpan.Zero ? platform.PlaybackDuration : row.Entry.Duration;
                row.DurationSeconds = Math.Max(1, duration.TotalSeconds);

                if (!row.IsSeeking)
                {
                    row.PositionSeconds = Math.Clamp(platform.PlaybackPosition.TotalSeconds, 0, row.DurationSeconds);
                }
            }
            else
            {
                row.IsSeeking = false;
                row.DurationSeconds = Math.Max(1, row.Entry.Duration.TotalSeconds);
                row.PositionSeconds = 0;
            }
        }
    }

    [RelayCommand]
    private async Task ShareAsync(RecordingRow? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            var when = row.Entry.Started.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            await _service.Platform.ShareAsync(row.Entry.Path, row.Entry.ContentType, $"{row.Entry.Station} – {when}").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>La corbeille : question d'abord, comme pour les réveils.</summary>
    [RelayCommand]
    private async Task DeleteAsync(RecordingRow? row)
    {
        if (row is null)
        {
            return;
        }

        var local = row.Entry.Started.ToLocalTime();
        var delete = Localization.Get("L_RecDelete");

        var choice = await Shell.Current.DisplayActionSheetAsync(
            Localization.Get(
                "L_RecDeleteAsk",
                row.Entry.Station,
                local.ToString("dd/MM", CultureInfo.InvariantCulture),
                local.ToString("HH:mm", CultureInfo.InvariantCulture)),
            Localization.Get("L_Cancel"),
            null,
            delete).ConfigureAwait(true);

        if (choice != delete)
        {
            return;
        }

        try
        {
            // On n'efface pas un fichier pendant qu'on l'écoute.
            var platform = _service.Platform;

            if (platform.IsPlaying && SamePath(platform.PlayingPath, row.Entry.Path))
            {
                platform.StopPlayback();
            }

            _service.Library.Delete(row.Entry);
            ShowInfo(Localization.Get("S_RecDeleted"));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }

        ReloadItems();
        RebuildLabels();
    }

    // ------------------------------------------------------------------ interne

    private static async Task<StreamResolution?> ResolveAsync(SpeakerDevice? device)
    {
        if (device?.Str is not { } str)
        {
            return null;
        }

        var now = await str.GetStatusAsync().ConfigureAwait(true);

        // L'historique et les présélections sont des pistes : leur absence ne bloque rien.
        IReadOnlyList<RecentEntry> recent;
        IReadOnlyList<StrPreset> presets;

        try
        {
            recent = await str.GetRecentAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            recent = Array.Empty<RecentEntry>();
        }

        try
        {
            presets = await str.GetPresetsAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            presets = Array.Empty<StrPreset>();
        }

        return StreamUrlResolver.Resolve(now, recent, presets, device.Host);
    }

    private void OnFinished(RecordingFinished finished)
    {
        var duration = FormatDuration(finished.Duration);

        if (finished.Entry is null)
        {
            ShowWarning(Localization.Get("S_RecTooShort"));
        }
        else if (finished.End == RecordEnd.MaxReached)
        {
            ShowInfo(Localization.Get("S_RecSavedMax"));
        }
        else if (finished.End == RecordEnd.SizeLimit)
        {
            ShowWarning(Localization.Get("S_RecSizeLimit", duration));
        }
        else if (finished.End == RecordEnd.Interrupted)
        {
            ShowWarning(Localization.Get("S_RecInterrupted", duration));
        }
        else
        {
            ShowInfo(Localization.Get("S_RecSaved", duration));
        }

        ApplyServiceState();
        RebuildLabels();
    }

    private void ApplyServiceState()
    {
        IsRecording = _service.IsRecording;
        ProgressText = FormatProgress(_service.Elapsed);
    }

    private void ReloadItems()
    {
        IReadOnlyList<RecordingEntry> entries;

        try
        {
            entries = _service.Library.Load();
        }
        catch (Exception)
        {
            entries = Array.Empty<RecordingEntry>();
        }

        Items.Clear();

        foreach (var entry in entries)
        {
            var local = entry.Started.ToLocalTime();

            var row = new RecordingRow
            {
                Entry = entry,
                Title = string.IsNullOrWhiteSpace(entry.Station) ? "Radio" : entry.Station,
                Subtitle = $"{local.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)} · {FormatDuration(entry.Duration)}",
                DurationSeconds = Math.Max(1, entry.Duration.TotalSeconds),
            };

            row.StartSeek = new RelayCommand(() => SeekStarted(row));
            row.EndSeek = new RelayCommand(() => SeekCompleted(row));
            Items.Add(row);
        }

        HasItems = Items.Count > 0;
        ApplyPlaying();
    }

    private void ApplyPlaying()
    {
        var platform = _service.Platform;

        foreach (var row in Items)
        {
            row.IsPlaying = platform.IsPlaying && SamePath(platform.PlayingPath, row.Entry.Path);
        }

        UpdatePlayback();

        if (platform.IsPlaying)
        {
            StartTicker();
        }
        else
        {
            StopTicker();
        }
    }

    private void StartTicker()
    {
        if (_ticker is not null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _ticker = cts;
        _ = TickAsync(cts.Token);
    }

    private void StopTicker()
    {
        _ticker?.Cancel();
        _ticker?.Dispose();
        _ticker = null;
    }

    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(PlaybackTick, ct).ConfigureAwait(false);

                // Une lecture arrêtée sans passer par ici (fin de fichier ratée,
                // arrêt par le système) remet aussi le bouton et la barre en place.
                OnMainThread(() =>
                {
                    if (_service.Platform.IsPlaying)
                    {
                        UpdatePlayback();
                    }
                    else
                    {
                        ApplyPlaying();
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Écoute terminée.
        }
    }

    private void ShowWarning(string message)
    {
        IsError = true;
        StatusMessage = message;
    }

    private static string RefusalText(string? code, string? source) => code switch
    {
        RecordingRefusal.NotPlaying => Localization.Get("S_RecNotPlaying"),
        RecordingRefusal.NotRadio => Localization.Get("S_RecNotRadio", FriendlySource(source)),
        RecordingRefusal.Hls => Localization.Get("S_RecHls"),
        RecordingRefusal.NotAudio => Localization.Get("S_RecNotAudio"),
        RecordingRefusal.Full => Localization.Get("S_RecFull"),
        RecordingRefusal.Network => Localization.Get("S_RecNetwork"),
        RecordingRefusal.Busy => Localization.Get("S_RecBusy"),
        _ => Localization.Get("S_RecNoUrl"),
    };

    /// <summary>« SPOTIFY » → « Spotify » ; « AUX » reste tel quel.</summary>
    private static string FriendlySource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "";
        }

        if (source.Length <= 3)
        {
            return source.ToUpperInvariant();
        }

        return char.ToUpperInvariant(source[0]) + source[1..].ToLowerInvariant().Replace('_', ' ');
    }

    /// <summary>« 3 min 20 ».</summary>
    private static string FormatDuration(TimeSpan duration)
    {
        var seconds = (int)Math.Round(duration.TotalSeconds);
        return Localization.Get("L_RecMinSec", seconds / 60, seconds % 60);
    }

    /// <summary>« 02:13 / 05:00 ».</summary>
    private static string FormatProgress(TimeSpan elapsed)
    {
        var max = RecordingRules.MaxDuration;
        return $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00} / {(int)max.TotalMinutes:00}:{max.Seconds:00}";
    }

    private static bool SamePath(string? a, string b)
        => a is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.Ordinal);
}

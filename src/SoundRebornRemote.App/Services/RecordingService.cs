using SoundReborn.Core.Recording;

namespace SoundRebornRemote.App.Services;

/// <summary>Fin d'un enregistrement : comment il s'est terminé, et ce qui a été gardé (null : rien).</summary>
public sealed record RecordingFinished(RecordEnd End, RecordingEntry? Entry, TimeSpan Duration);

/// <summary>
/// Fait tourner l'enregistrement d'une radio : un seul à la fois, 5 minutes au
/// plus.
///
/// Le flux est copié en tâche de fond ; le service au premier plan Android
/// (IRecordingPlatform.Begin) garde l'application en vie pendant ce temps, écran
/// éteint compris. Les événements arrivent sur un fil quelconque : la vue-modèle
/// les ramène sur le fil principal.
/// </summary>
public sealed class RecordingService
{
    private readonly HttpClient _http;
    private readonly IRecordingPlatform _platform;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    private bool _busy;
    private CancellationTokenSource? _stop;
    private Task _running = Task.CompletedTask;

    public RecordingService(HttpClient http, IRecordingPlatform platform, string folder, TimeProvider clock)
    {
        _http = http;
        _platform = platform;
        _clock = clock;
        Library = new RecordingLibrary(folder);

        // Le service Android passe par ici pour le bouton « Arrêter » de la notification.
        Current = this;
    }

    /// <summary>L'instance de l'application, pour le service Android.</summary>
    public static RecordingService? Current { get; private set; }

    public RecordingLibrary Library { get; }

    public IRecordingPlatform Platform => _platform;

    public bool IsRecording { get; private set; }

    public string? Station { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    /// <summary>Chaque seconde pendant l'enregistrement, et à chaque changement d'état.</summary>
    public event EventHandler? Changed;

    public event EventHandler<RecordingFinished>? Finished;

    /// <summary>La copie en cours, ou une tâche terminée. Sert surtout aux essais.</summary>
    public Task WhenIdle => _running;

    /// <summary>
    /// Démarre l'enregistrement. Renvoie null quand c'est parti, sinon un code de
    /// <see cref="RecordingRefusal"/>. Au 11ᵉ enregistrement : refus, rien n'est
    /// supprimé d'office.
    /// </summary>
    public async Task<string?> StartAsync(string url, string station)
    {
        lock (_gate)
        {
            if (_busy)
            {
                return RecordingRefusal.Busy;
            }

            _busy = true;
        }

        PreparedStream? prepared = null;

        try
        {
            if (Library.IsFull)
            {
                return Release(RecordingRefusal.Full);
            }

            await _platform.RequestNotificationPermissionAsync().ConfigureAwait(false);

            try
            {
                prepared = await StreamPreparer.OpenAsync(_http, url, CancellationToken.None).ConfigureAwait(false);
            }
            catch (RecordingException ex)
            {
                return Release(ex.Code);
            }

            // Une écoute en cours s'arrête : on ne lit pas un fichier pendant qu'on en enregistre un autre.
            if (_platform.IsPlaying)
            {
                _platform.StopPlayback();
            }

            var part = Library.BeginPart(station, _clock.GetLocalNow(), prepared.Extension, prepared.ContentType);
            var stop = new CancellationTokenSource();

            lock (_gate)
            {
                _stop = stop;
                IsRecording = true;
                Station = station;
                Elapsed = TimeSpan.Zero;
            }

            try
            {
                _platform.Begin(station);
            }
            catch (Exception)
            {
                // Sans service au premier plan, l'enregistrement tourne quand même
                // tant que l'application reste ouverte.
            }

            var opened = prepared;
            prepared = null;
            _running = Task.Run(() => CopyAsync(opened, part, station, stop));
            Changed?.Invoke(this, EventArgs.Empty);
            return null;
        }
        catch (Exception)
        {
            // Fichier impossible à créer, par exemple : rien n'a démarré.
            prepared?.Dispose();
            return Release(RecordingRefusal.Network);
        }
    }

    /// <summary>Arrête l'enregistrement en cours ; ce qui a été reçu est gardé.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _stop?.Cancel();
        }
    }

    private async Task CopyAsync(PreparedStream prepared, string part, string station, CancellationTokenSource stop)
    {
        var started = _clock.GetUtcNow();
        RecordOutcome outcome;

        var progress = new InlineProgress(elapsed =>
        {
            Elapsed = elapsed;
            _platform.Update(station, elapsed);
            Changed?.Invoke(this, EventArgs.Empty);
        });

        try
        {
            await using var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.Read);
            outcome = await StreamRecorder.CopyAsync(prepared.Body, file, _clock, progress, stop.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Disque plein, fichier inaccessible… : ce qui est écrit est gardé si c'est assez long.
            var elapsed = _clock.GetUtcNow() - started;
            outcome = new RecordOutcome(RecordEnd.Interrupted, elapsed > RecordingRules.MaxDuration ? RecordingRules.MaxDuration : elapsed, 0);
        }
        finally
        {
            prepared.Dispose();
        }

        RecordingEntry? entry = null;

        try
        {
            entry = Library.Commit(part, outcome.Duration, File.Exists(part) ? new FileInfo(part).Length : outcome.Bytes);
        }
        catch (Exception)
        {
            // Le fichier sera repris au prochain rapprochement de la bibliothèque.
        }

        _platform.End();

        lock (_gate)
        {
            IsRecording = false;
            Station = null;
            _stop = null;
            _busy = false;
        }

        stop.Dispose();
        Changed?.Invoke(this, EventArgs.Empty);
        Finished?.Invoke(this, new RecordingFinished(outcome.End, entry, outcome.Duration));
    }

    private string Release(string code)
    {
        lock (_gate)
        {
            _busy = false;
        }

        return code;
    }

    /// <summary>Progress&lt;T&gt; passerait par le contexte de synchronisation ; ici l'appel est direct.</summary>
    private sealed class InlineProgress : IProgress<TimeSpan>
    {
        private readonly Action<TimeSpan> _report;

        public InlineProgress(Action<TimeSpan> report) => _report = report;

        public void Report(TimeSpan value) => _report(value);
    }
}

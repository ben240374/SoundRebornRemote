using Android.Content;
using Android.Media;
using Android.Net;
using SoundReborn.Core.Recording;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App;

/// <summary>
/// Écoute d'une station sur l'appareil, par le lecteur intégré d'Android
/// (MediaPlayer) : MP3, AAC, et le HLS que le lecteur sait ouvrir. Rien n'est
/// enregistré : le son sort du haut-parleur ou du casque, puis tout est libéré.
///
/// Le lecteur est piloté depuis le fil principal : ses rappels (prêt, erreur, fin)
/// y arrivent aussi, ce qui évite toute course avec un arrêt demandé entre-temps.
/// </summary>
public sealed class AndroidStreamPreview : IStreamPreview
{
    /// <summary>Le flux doit commencer à jouer dans ce délai, sinon il est déclaré muet.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;
    private MediaPlayer? _player;

    /// <summary>Numéro de l'écoute en cours : un rappel d'une écoute déjà remplacée est ignoré.</summary>
    private int _generation;

    public AndroidStreamPreview(HttpClient http) => _http = http;

    public bool IsSupported => true;

    public string DeviceKind => DeviceInfo.Current.Idiom == DeviceIdiom.Tablet ? "tablet"
        : DeviceInfo.Current.Idiom == DeviceIdiom.Phone ? "phone"
        : "other";

    public bool IsOnMobileData
    {
        get
        {
            try
            {
                var manager = Android.App.Application.Context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
                var capabilities = manager?.GetNetworkCapabilities(manager.ActiveNetwork);

                return capabilities is not null &&
                       capabilities.HasTransport(TransportType.Cellular) &&
                       !capabilities.HasTransport(TransportType.Wifi);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public event EventHandler? Ended;

    public async Task StartAsync(string url, CancellationToken ct)
    {
        // Liste .pls / .m3u : le lecteur veut l'adresse du flux qu'elle contient.
        var playable = await StreamPreparer.ResolveForPlaybackAsync(_http, url, ct).ConfigureAwait(false);

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            // Une écoute plus récente a pu être demandée pendant la lecture de la
            // liste .pls / .m3u : celle-ci ne doit pas l'arrêter en démarrant.
            ct.ThrowIfCancellationRequested();

            Stop();

            var generation = ++_generation;
            var player = new MediaPlayer();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _player = player;

            player.SetAudioAttributes(new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Music)!
                .Build()!);

            player.Prepared += (_, _) =>
            {
                if (generation != _generation)
                {
                    return;
                }

                try
                {
                    player.Start();
                    ready.TrySetResult();
                }
                catch (Exception ex)
                {
                    ready.TrySetException(ex);
                }
            };

            player.Error += (_, e) =>
            {
                e.Handled = true;

                if (generation != _generation)
                {
                    return;
                }

                // Avant le départ : l'écoute échoue. Après : le flux s'est coupé.
                if (!ready.TrySetException(new IOException($"MediaPlayer {e.What}/{e.Extra}")))
                {
                    Release(generation);
                    Ended?.Invoke(this, EventArgs.Empty);
                }
            };

            player.Completion += (_, _) =>
            {
                if (generation == _generation)
                {
                    Release(generation);
                    Ended?.Invoke(this, EventArgs.Empty);
                }
            };

            try
            {
                // Certaines stations refusent un lecteur sans User-Agent.
                var headers = new Dictionary<string, string> { ["User-Agent"] = "SoundRebornRemote (Android)" };
                player.SetDataSource(Android.App.Application.Context, Android.Net.Uri.Parse(playable)!, headers);
                player.PrepareAsync();

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(StartTimeout);
                await ready.Task.WaitAsync(timeout.Token);
            }
            catch (Exception)
            {
                Release(generation);
                throw;
            }
        }).ConfigureAwait(false);
    }

    public void Stop()
    {
        if (MainThread.IsMainThread)
        {
            _generation++;
            ReleaseCurrent();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(Stop);
        }
    }

    /// <summary>Libère le lecteur de cette écoute, si c'est toujours la sienne.</summary>
    private void Release(int generation)
    {
        if (generation == _generation)
        {
            ReleaseCurrent();
        }
    }

    private void ReleaseCurrent()
    {
        var player = _player;
        _player = null;

        if (player is null)
        {
            return;
        }

        try
        {
            if (player.IsPlaying)
            {
                player.Stop();
            }
        }
        catch (Exception)
        {
            // Lecteur pas encore prêt ou déjà arrêté.
        }

        player.Release();
    }
}

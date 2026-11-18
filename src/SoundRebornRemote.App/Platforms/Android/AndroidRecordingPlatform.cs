using Android.App;
using Android.Content;
using Android.Media;
using Android.Net;
using Android.Net.Wifi;
using Android.OS;
using SoundReborn.Core.Recording;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App;

/// <summary>
/// L'enregistrement côté Android : service au premier plan, notification,
/// verrous (processeur et Wi-Fi), lecture par MediaPlayer, partage, autorisation
/// des notifications.
///
/// Rien ici ne doit faire échouer un enregistrement : chaque appel au système est
/// protégé, et un refus d'autorisation laisse l'enregistrement se faire sans
/// notification visible.
/// </summary>
public sealed class AndroidRecordingPlatform : IRecordingPlatform
{
    private const string AskedKey = "recording.notifications.asked";

    private readonly object _gate = new();
    private PowerManager.WakeLock? _wakeLock;
    private WifiManager.WifiLock? _wifiLock;
    private Timer? _lockLimit;
    private MediaPlayer? _player;
    private string? _playingPath;
    private bool _serviceStarted;

    private static Context AppContext => Android.App.Application.Context;

    public async Task RequestNotificationPermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return;
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();

                // Une seule demande : Android ne la repose de toute façon plus après deux refus.
                if (status == PermissionStatus.Granted || Preferences.Default.Get(AskedKey, false))
                {
                    return;
                }

                Preferences.Default.Set(AskedKey, true);
                await Permissions.RequestAsync<Permissions.PostNotifications>();
            });
        }
        catch (Exception)
        {
            // Sans notification, l'enregistrement se fait quand même.
        }
    }

    public void Begin(string station)
    {
        AcquireLocks();

        try
        {
            RecordingForegroundService.Start(AppContext, station);
            _serviceStarted = true;
        }
        catch (Exception)
        {
            // Démarrage refusé par le système : l'enregistrement continue tant que
            // l'application reste ouverte.
        }
    }

    public void Update(string station, TimeSpan elapsed)
    {
        try
        {
            if (AppContext.GetSystemService(Context.NotificationService) is NotificationManager manager)
            {
                manager.Notify(
                    RecordingForegroundService.NotificationId,
                    RecordingForegroundService.Build(AppContext, station, elapsed));
            }
        }
        catch (Exception)
        {
            // Une notification manquée n'arrête rien.
        }
    }

    public void End()
    {
        try
        {
            // Un service qui n'a pas démarré n'a rien à arrêter : lui envoyer la
            // commande de fin le démarrerait pour rien.
            if (_serviceStarted)
            {
                _serviceStarted = false;
                RecordingForegroundService.Stop(AppContext);
            }

            if (AppContext.GetSystemService(Context.NotificationService) is NotificationManager manager)
            {
                manager.Cancel(RecordingForegroundService.NotificationId);
            }
        }
        catch (Exception)
        {
            // Le service s'arrêtera avec le processus.
        }

        ReleaseLocks();
    }

    // ------------------------------------------------------------------ lecture

    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _player is not null;
            }
        }
    }

    public string? PlayingPath
    {
        get
        {
            lock (_gate)
            {
                return _playingPath;
            }
        }
    }

    public void Play(string path, Action onEnded)
    {
        StopPlayback();

        var player = new MediaPlayer();

        try
        {
            player.SetAudioAttributes(new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Music)!
                .Build()!);
            player.SetDataSource(path);
            player.Prepare();

            player.Completion += (_, _) =>
            {
                var ended = false;

                lock (_gate)
                {
                    if (ReferenceEquals(_player, player))
                    {
                        _player = null;
                        _playingPath = null;
                        ended = true;
                    }
                }

                player.Release();

                if (ended)
                {
                    onEnded();
                }
            };

            lock (_gate)
            {
                _player = player;
                _playingPath = path;
            }

            player.Start();
        }
        catch (Exception)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_player, player))
                {
                    _player = null;
                    _playingPath = null;
                }
            }

            player.Release();
            throw;
        }
    }

    public void StopPlayback()
    {
        MediaPlayer? player;

        lock (_gate)
        {
            player = _player;
            _player = null;
            _playingPath = null;
        }

        if (player is null)
        {
            return;
        }

        try
        {
            player.Stop();
        }
        catch (Exception)
        {
            // Déjà arrêté.
        }

        player.Release();
    }

    // ------------------------------------------------------------------ partage

    public Task ShareAsync(string path, string contentType, string title)
        => Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File = new ShareFile(path, contentType),
        });

    // ------------------------------------------------------------------ verrous

    private void AcquireLocks()
    {
        lock (_gate)
        {
            ReleaseLocksLocked();

            try
            {
                if (AppContext.GetSystemService(Context.PowerService) is PowerManager power)
                {
                    _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "SoundReborn:recording");
                    _wakeLock?.Acquire((long)RecordingRules.LockLimit.TotalMilliseconds);
                }

                if (AppContext.ApplicationContext?.GetSystemService(Context.WifiService) is WifiManager wifi)
                {
#pragma warning disable CS0618 // FullHighPerf est déprécié en API 34 mais reste le seul mode utile écran éteint avant.
                    _wifiLock = wifi.CreateWifiLock(WifiMode.FullHighPerf, "SoundReborn:recording");
#pragma warning restore CS0618
                    _wifiLock?.SetReferenceCounted(false);
                    _wifiLock?.Acquire();
                }
            }
            catch (Exception)
            {
                // Sans verrou, l'enregistrement reste possible écran allumé.
            }

            // Filet : quoi qu'il arrive, les verrous tombent après LockLimit.
            _lockLimit = new Timer(_ => ReleaseLocks(), null, RecordingRules.LockLimit, Timeout.InfiniteTimeSpan);
        }
    }

    private void ReleaseLocks()
    {
        lock (_gate)
        {
            ReleaseLocksLocked();
        }
    }

    private void ReleaseLocksLocked()
    {
        _lockLimit?.Dispose();
        _lockLimit = null;

        try
        {
            if (_wakeLock?.IsHeld == true)
            {
                _wakeLock.Release();
            }

            if (_wifiLock?.IsHeld == true)
            {
                _wifiLock.Release();
            }
        }
        catch (Exception)
        {
            // Déjà relâché par le système.
        }

        _wakeLock = null;
        _wifiLock = null;
    }
}

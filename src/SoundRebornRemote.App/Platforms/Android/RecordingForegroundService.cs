using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using SoundReborn.Core.Recording;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App;

/// <summary>
/// Le service au premier plan qui garde l'application en vie pendant un
/// enregistrement, écran éteint compris.
///
/// Il ne copie rien lui-même : la copie tourne dans RecordingService. Il porte la
/// notification « Enregistrement en cours – Bel RTL · 02:13 / 05:00 » et son
/// bouton « Arrêter ». Le type « dataSync » est celui qu'Android 14 et suivants
/// demandent pour un transfert de données lancé par l'utilisateur.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class RecordingForegroundService : Service
{
    public const int NotificationId = 4807;

    private const string ChannelId = "recording";
    private const string ActionStop = "be.barbaix.soundrebornremote.STOP_RECORDING";
    private const string ActionEnd = "be.barbaix.soundrebornremote.END_RECORDING";
    private const string ExtraStation = "station";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            // Bouton « Arrêter » de la notification. Le service s'arrêtera quand
            // RecordingService aura gardé le fichier (IRecordingPlatform.End).
            // Processus relancé entre-temps : plus rien n'enregistre, on s'arrête.
            if (RecordingService.Current is { IsRecording: true } service)
            {
                service.Stop();
            }
            else
            {
                StopSelf();
            }

            return StartCommandResult.NotSticky;
        }

        if (intent?.Action == ActionEnd)
        {
            // Fin d'enregistrement. Elle arrive par la même file que le démarrage :
            // StartForeground a donc déjà été appelé, même pour un flux coupé dans
            // la première seconde. Arrêter le service avant cet appel fait planter
            // l'application sur certaines versions d'Android.
            StopForegroundCompat();
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        var station = intent?.GetStringExtra(ExtraStation) ?? "";
        var notification = Build(this, station, TimeSpan.Zero);

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }

        // Si Android tue le processus, on ne relance pas un enregistrement dont
        // le flux est perdu : la bibliothèque reprendra le fichier à la relance.
        return StartCommandResult.NotSticky;
    }

    /// <summary>Démarre le service au premier plan pour un nouvel enregistrement.</summary>
    public static void Start(Context context, string station)
    {
        var intent = new Intent(context, typeof(RecordingForegroundService));
        intent.PutExtra(ExtraStation, station);

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    /// <summary>
    /// Termine le service. Passe par une commande, pas par StopService : voir
    /// ActionEnd dans OnStartCommand.
    /// </summary>
    public static void Stop(Context context)
    {
        var intent = new Intent(context, typeof(RecordingForegroundService));
        intent.SetAction(ActionEnd);
        context.StartService(intent);
    }

    // L'application démarre à l'API 24 : la forme à drapeaux suffit.
    private void StopForegroundCompat() => StopForeground(StopForegroundFlags.Remove);

    /// <summary>La notification : station, compteur, bouton « Arrêter ». Toucher ouvre l'application.</summary>
    public static Notification Build(Context context, string station, TimeSpan elapsed)
    {
        EnsureChannel(context);

        var flags = PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable;

        var open = new Intent(context, typeof(MainActivity));
        open.SetFlags(ActivityFlags.SingleTop | ActivityFlags.NewTask);
        var openIntent = PendingIntent.GetActivity(context, 0, open, flags);

        var stop = new Intent(context, typeof(RecordingForegroundService));
        stop.SetAction(ActionStop);
        var stopIntent = PendingIntent.GetService(context, 1, stop, flags);

        var max = RecordingRules.MaxDuration;
        var text = $"{station} · {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00} / {(int)max.TotalMinutes:00}:{max.Seconds:00}";

        return new AndroidX.Core.App.NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle(Localization.Get("L_RecNotifTitle"))!
            .SetContentText(text)!
            .SetSmallIcon(Resource.Drawable.ic_stat_record)!
            .SetOngoing(true)!
            .SetOnlyAlertOnce(true)!
            .SetSilent(true)!
            .SetContentIntent(openIntent)!
            .AddAction(0, Localization.Get("L_RecStop"), stopIntent)!
            .SetPriority(AndroidX.Core.App.NotificationCompat.PriorityLow)!
            .Build()!;
    }

    /// <summary>Canal « Enregistrement » : importance basse, sans son ni vibration.</summary>
    private static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        if (context.GetSystemService(NotificationService) is not NotificationManager manager ||
            manager.GetNotificationChannel(ChannelId) is not null)
        {
            return;
        }

        var channel = new NotificationChannel(ChannelId, Localization.Get("L_RecTitle"), NotificationImportance.Low);
        channel.SetSound(null, null);
        channel.EnableVibration(false);
        manager.CreateNotificationChannel(channel);
    }
}

namespace SoundRebornRemote.App.Services;

/// <summary>
/// Ce que l'enregistrement demande au système : le service au premier plan et sa
/// notification, la lecture d'un fichier, le partage et l'autorisation des
/// notifications.
///
/// L'implémentation Android est dans Platforms/Android. Rien ici ne dépend
/// d'Android : le service d'enregistrement et la vue-modèle se vérifient sans
/// téléphone.
/// </summary>
public interface IRecordingPlatform
{
    /// <summary>
    /// Demande une fois l'autorisation d'afficher des notifications (Android 13 et
    /// suivants). Ne lève jamais d'exception ; un refus n'empêche pas
    /// l'enregistrement, seule la notification n'apparaît pas.
    /// </summary>
    Task RequestNotificationPermissionAsync();

    /// <summary>Démarre le service au premier plan et prend les verrous (processeur, Wi-Fi).</summary>
    void Begin(string station);

    /// <summary>Met à jour la notification : « Bel RTL · 02:13 / 05:00 ».</summary>
    void Update(string station, TimeSpan elapsed);

    /// <summary>Arrête le service au premier plan et relâche les verrous.</summary>
    void End();

    /// <summary>Lit un enregistrement ; <paramref name="onEnded"/> est appelé à la fin du fichier.</summary>
    void Play(string path, Action onEnded);

    void StopPlayback();

    bool IsPlaying { get; }

    /// <summary>Le fichier en cours de lecture, ou null.</summary>
    string? PlayingPath { get; }

    /// <summary>Ouvre le menu de partage du système avec ce fichier.</summary>
    Task ShareAsync(string path, string contentType, string title);
}

/// <summary>Plateforme sans enregistrement (autre que le téléphone Android) : rien ne se passe.</summary>
public sealed class NoRecordingPlatform : IRecordingPlatform
{
    public Task RequestNotificationPermissionAsync() => Task.CompletedTask;

    public void Begin(string station) { }

    public void Update(string station, TimeSpan elapsed) { }

    public void End() { }

    public void Play(string path, Action onEnded) { }

    public void StopPlayback() { }

    public bool IsPlaying => false;

    public string? PlayingPath => null;

    public Task ShareAsync(string path, string contentType, string title) => Task.CompletedTask;
}

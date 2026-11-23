namespace SoundRebornRemote.App.Services;

/// <summary>
/// Écoute d'une station sur l'appareil lui-même (téléphone ou tablette), avant de
/// la diffuser sur l'enceinte. Une seule écoute à la fois ; c'est un aperçu, pas un
/// lecteur radio : elle s'arrête quand on quitte l'onglet ou l'application.
/// </summary>
public interface IStreamPreview
{
    /// <summary>Vrai quand l'appareil sait lire un flux.</summary>
    bool IsSupported { get; }

    /// <summary>« phone », « tablet » ou « other » : le libellé dit « ce téléphone » ou « cette tablette ».</summary>
    string DeviceKind { get; }

    /// <summary>Vrai quand l'appareil passe par les données mobiles (pas de Wi-Fi).</summary>
    bool IsOnMobileData { get; }

    /// <summary>
    /// Démarre la lecture ; se termine quand le son sort, ou lève une exception si
    /// le flux ne s'ouvre pas. Arrête d'abord une écoute en cours.
    /// </summary>
    Task StartAsync(string url, CancellationToken ct);

    /// <summary>Arrête l'écoute. Sans effet s'il n'y en a pas.</summary>
    void Stop();

    /// <summary>L'écoute s'est arrêtée d'elle-même (flux coupé, erreur du lecteur).</summary>
    event EventHandler? Ended;
}

/// <summary>Pas d'écoute sur l'appareil (autre plateforme, essais).</summary>
public sealed class NoStreamPreview : IStreamPreview
{
    public bool IsSupported => false;

    public string DeviceKind => "other";

    public bool IsOnMobileData => false;

    public Task StartAsync(string url, CancellationToken ct) => throw new NotSupportedException();

    public void Stop() { }

    public event EventHandler? Ended
    {
        add { }
        remove { }
    }
}

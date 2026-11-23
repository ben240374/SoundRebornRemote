namespace SoundRebornRemote.App.Services;

/// <summary>
/// Installation d'un APK téléchargé, par l'installateur d'Android : l'utilisateur
/// confirme toujours lui-même. Absent de la version Google Play, qui se met à jour
/// par le Play Store (règle « Device and Network Abuse »).
/// </summary>
public interface IAppInstaller
{
    /// <summary>Vrai quand l'application peut ouvrir l'installateur d'Android.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Vrai si Android autorise déjà l'application à proposer une installation.
    /// Sinon, ouvre l'écran des réglages où l'utilisateur peut l'autoriser, et
    /// renvoie faux : il touchera « Installer » une seconde fois en revenant.
    /// </summary>
    Task<bool> EnsurePermissionAsync();

    /// <summary>Ouvre l'installateur d'Android sur ce fichier.</summary>
    void Install(string apkPath);
}

/// <summary>Pas d'installation depuis l'application : la mise à jour passe par la page GitHub.</summary>
public sealed class NoAppInstaller : IAppInstaller
{
    public bool IsSupported => false;

    public Task<bool> EnsurePermissionAsync() => Task.FromResult(false);

    public void Install(string apkPath) { }
}

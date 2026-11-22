namespace SoundRebornRemote.App.Services;

/// <summary>
/// Le micro du téléphone, pour le message vocal. Seule l'implémentation Android
/// enregistre ; ailleurs <see cref="NoVoiceRecorder"/> dit que ce n'est pas possible.
/// </summary>
public interface IVoiceRecorder
{
    /// <summary>Faux quand cet appareil ne sait pas enregistrer.</summary>
    bool IsSupported { get; }

    /// <summary>Demande l'accès au micro si besoin. Vrai quand il est accordé.</summary>
    Task<bool> EnsurePermissionAsync();

    /// <summary>Commence l'enregistrement (AAC) dans ce fichier. Lève si le micro ne démarre pas.</summary>
    void Start(string path);

    /// <summary>Arrête et termine le fichier. Faux quand rien d'utilisable n'a été enregistré.</summary>
    bool Stop();

    /// <summary>Abandonne l'enregistrement en cours, s'il y en a un. Ne lève jamais.</summary>
    void Cancel();
}

/// <summary>Un appareil sans micro utilisable par l'application.</summary>
public sealed class NoVoiceRecorder : IVoiceRecorder
{
    public bool IsSupported => false;

    public Task<bool> EnsurePermissionAsync() => Task.FromResult(false);

    public void Start(string path)
        => throw new NotSupportedException("Pas de micro sur cet appareil.");

    public bool Stop() => false;

    public void Cancel()
    {
    }
}

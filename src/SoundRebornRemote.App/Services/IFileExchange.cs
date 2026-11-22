namespace SoundRebornRemote.App.Services;

/// <summary>
/// Échange de fichiers avec le reste du téléphone, pour la sauvegarde des
/// favoris : partager un fichier (Drive, e-mail, Fichiers…) et en choisir un.
/// L'implémentation Android passe par les écrans du système ;
/// <see cref="NoFileExchange"/> sert ailleurs.
/// </summary>
public interface IFileExchange
{
    /// <summary>Ouvre l'écran de partage du système pour ce fichier.</summary>
    Task ShareAsync(string path, string contentType, string title);

    /// <summary>
    /// Laisse choisir un fichier et en rend le texte. Null quand l'utilisateur
    /// renonce. Lève <see cref="InvalidDataException"/> pour un fichier plus gros
    /// que <paramref name="maxBytes"/>.
    /// </summary>
    Task<string?> PickTextAsync(string title, int maxBytes);
}

/// <summary>Un appareil sans écran de partage ni sélecteur de fichiers.</summary>
public sealed class NoFileExchange : IFileExchange
{
    public Task ShareAsync(string path, string contentType, string title)
        => throw new NotSupportedException("Pas de partage sur cet appareil.");

    public Task<string?> PickTextAsync(string title, int maxBytes)
        => Task.FromResult<string?>(null);
}

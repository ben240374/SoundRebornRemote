using System.Text;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App;

/// <summary>
/// Partage et choix de fichiers par les écrans d'Android (feuille de partage,
/// sélecteur de documents). Aucune autorisation de stockage n'est demandée : le
/// fichier exporté part du cache de l'application, et le fichier importé est lu
/// par le flux que le sélecteur ouvre.
/// </summary>
public sealed class AndroidFileExchange : IFileExchange
{
    public Task ShareAsync(string path, string contentType, string title)
        => Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File = new ShareFile(path, contentType),
        });

    public async Task<string?> PickTextAsync(string title, int maxBytes)
    {
        var picked = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = title,

            // Selon l'application qui l'a gardé, un fichier .json peut arriver
            // comme JSON, texte ou simple suite d'octets.
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = new[] { "application/json", "text/plain", "application/octet-stream" },
            }),
        }).ConfigureAwait(false);

        if (picked is null)
        {
            return null;
        }

        await using var stream = await picked.OpenReadAsync().ConfigureAwait(false);

        // Lecture bornée : un fichier démesuré n'est pas une sauvegarde de favoris.
        var buffer = new byte[maxBytes + 1];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total)).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        if (total > maxBytes)
        {
            throw new InvalidDataException("Fichier trop gros.");
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }
}

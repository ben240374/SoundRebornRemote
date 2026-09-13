using System.Security.Cryptography;
using System.Text;

namespace SoundRebornRemote.App.Services;

/// <summary>
/// Télécharge les logos de stations et les sert depuis un fichier local.
///
/// Pourquoi ne pas laisser le contrôle Image charger l'URL lui-même : ces logos
/// sont hébergés par les stations elles-mêmes, pas par un CDN sérieux. Certificats
/// expirés ou auto-signés, redirections en cascade, serveurs qui refusent une
/// requête sans User-Agent, hôtes carrément morts. Le chargeur d'images d'Android
/// échoue alors en silence : pas d'erreur, pas d'image, une tuile vide — exactement
/// ce qu'on observait.
///
/// En téléchargeant nous-mêmes, on contrôle l'en-tête, le délai d'attente, la
/// taille maximale et la tolérance aux certificats, on sait pourquoi ça a échoué,
/// et l'image reste disponible hors ligne au lancement suivant.
/// </summary>
public sealed class ArtworkCache
{
    /// <summary>Au-delà, ce n'est plus un logo : on abandonne.</summary>
    private const int MaxBytes = 3 * 1024 * 1024;

    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(12);

    private readonly HttpClient _strict;
    private readonly HttpClient _lenient;
    private readonly string _directory;

    /// <summary>URL déjà résolues pendant cette session, y compris les échecs (valeur nulle).</summary>
    private readonly Dictionary<string, string?> _resolved = new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _gate = new(4);

    public ArtworkCache()
    {
        _directory = Path.Combine(FileSystem.CacheDirectory, "artwork");
        Directory.CreateDirectory(_directory);

        _strict = CreateClient(lenient: false);

        // Second client, utilisé seulement après un échec TLS du premier. Ce qui
        // transite ici est une image publique affichée dans une tuile : aucune
        // donnée de l'utilisateur ne passe par cette connexion, et l'alternative
        // est de ne jamais afficher le logo d'une station dont le site a un
        // certificat périmé — c'est-à-dire beaucoup d'entre elles.
        _lenient = CreateClient(lenient: true);
    }

    /// <summary>
    /// Renvoie le chemin local du logo, en le téléchargeant si besoin.
    /// Null quand l'image est introuvable ou illisible — l'appelant affiche alors
    /// son symbole de repli.
    /// </summary>
    public async Task<string?> GetLocalPathAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !url!.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        url = url.Trim();

        lock (_resolved)
        {
            if (_resolved.TryGetValue(url, out var known))
            {
                return known;
            }
        }

        var path = Path.Combine(_directory, FileNameFor(url));

        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            Remember(url, path);
            return path;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // Une autre tâche a pu télécharger la même image pendant l'attente.
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                Remember(url, path);
                return path;
            }

            var bytes = await DownloadAsync(url, ct).ConfigureAwait(false);

            if (bytes is null)
            {
                Remember(url, null);
                return null;
            }

            // Écriture par un fichier temporaire puis renommage : une image à
            // moitié écrite, si l'application est tuée, ne sera jamais servie.
            var temp = path + ".part";
            await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);

            Remember(url, path);
            return path;
        }
        catch (Exception)
        {
            Remember(url, null);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Découpe une valeur « art » telle que l'agent la stocke : une chaîne de
    /// candidats séparés par des barres verticales, du plus probable au moins sûr.
    /// L'application de bureau fait exactement pareil (data-fallbacks). Traiter la
    /// chaîne entière comme une seule URL ne donne jamais rien.
    /// </summary>
    public static IReadOnlyList<string> SplitCandidates(string? art)
    {
        if (string.IsNullOrWhiteSpace(art))
        {
            return Array.Empty<string>();
        }

        return art.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(c => c.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                  .ToList();
    }

    /// <summary>
    /// Essaie chaque candidat de la chaîne et renvoie le premier qui donne une
    /// image, avec l'URL retenue. (null, null) si aucun ne répond.
    /// </summary>
    public async Task<(ImageSource? Source, string? Url)> GetFromChainAsync(string? art, CancellationToken ct = default)
    {
        foreach (var candidate in SplitCandidates(art))
        {
            var source = await GetImageSourceAsync(candidate, ct).ConfigureAwait(false);

            if (source is not null)
            {
                return (source, candidate);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Renvoie une source d'image utilisable dans une vue.
    ///
    /// On passe par un flux plutôt que par ImageSource.FromFile : sur Android, une
    /// source de type fichier commence par chercher une ressource du même nom dans
    /// l'APK et ne retombe sur le chemin qu'ensuite. Lire le fichier nous-mêmes
    /// évite cette ambiguïté, et une image qui ne s'affiche pas redevient une
    /// erreur visible plutôt qu'une tuile vide.
    /// </summary>
    public async Task<ImageSource?> GetImageSourceAsync(string? url, CancellationToken ct = default)
    {
        var path = await GetLocalPathAsync(url, ct).ConfigureAwait(false);

        if (path is null)
        {
            return null;
        }

        return ImageSource.FromStream(() => (Stream)File.OpenRead(path));
    }

    /// <summary>Vide le cache disque. Utile depuis les réglages si un logo a changé.</summary>
    public void Clear()
    {
        lock (_resolved)
        {
            _resolved.Clear();
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory))
            {
                File.Delete(file);
            }
        }
        catch (Exception)
        {
            // Cache non vidé : sans conséquence.
        }
    }

    private async Task<byte[]?> DownloadAsync(string url, CancellationToken ct)
    {
        var bytes = await TryDownloadAsync(_strict, url, ct).ConfigureAwait(false);

        if (bytes is not null)
        {
            return bytes;
        }

        return await TryDownloadAsync(_lenient, url, ct).ConfigureAwait(false);
    }

    private static async Task<byte[]?> TryDownloadAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DownloadTimeout);

            using var response = await client
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            // Beaucoup de stations renvoient une page HTML d'erreur avec un code 200 ;
            // la stocker donnerait une tuile vide plutôt qu'un repli propre.
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";

            if (mediaType.Length > 0 && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (response.Content.Headers.ContentLength is > MaxBytes)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);

            if (bytes.Length == 0 || bytes.Length > MaxBytes || !LooksLikeImage(bytes))
            {
                return null;
            }

            return bytes;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Contrôle du nombre magique : PNG, JPEG, GIF, BMP, WebP ou SVG.
    /// Un serveur qui annonce « image/png » et renvoie du HTML est monnaie courante.
    /// </summary>
    private static bool LooksLikeImage(byte[] bytes)
    {
        if (bytes.Length < 4)
        {
            return false;
        }

        // PNG
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return true;
        }

        // JPEG
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return true;
        }

        // GIF
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
        {
            return true;
        }

        // BMP
        if (bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            return true;
        }

        // RIFF....WEBP
        if (bytes.Length > 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return true;
        }

        // SVG : du texte, donc on regarde le début après d'éventuels espaces.
        var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 256)).TrimStart();

        return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
               head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    private void Remember(string url, string? path)
    {
        lock (_resolved)
        {
            _resolved[url] = path;
        }
    }

    /// <summary>Nom de fichier stable et sans surprise, dérivé de l'URL.</summary>
    private static string FileNameFor(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        var name = Convert.ToHexString(hash, 0, 16).ToLowerInvariant();

        // L'extension aide Android à choisir son décodeur ; .img quand l'URL n'en a pas.
        var extension = Path.GetExtension(new Uri(url, UriKind.Absolute).AbsolutePath).ToLowerInvariant();

        if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg"))
        {
            extension = ".img";
        }

        return name + extension;
    }

    private static HttpClient CreateClient(bool lenient)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };

        if (lenient)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }

        var client = new HttpClient(handler)
        {
            Timeout = DownloadTimeout,
        };

        // Certaines stations refusent une requête sans User-Agent de navigateur.
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Android) SoundRebornRemote/1.0");

        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/*,*/*;q=0.8");

        return client;
    }
}

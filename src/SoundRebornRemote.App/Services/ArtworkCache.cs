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

    /// <summary>
    /// Moment de chaque échec. Un échec n'est plus définitif pour la session : une
    /// enceinte qui redémarre, un réseau qui hoquette, et un logo restait absent
    /// jusqu'à la fermeture de l'application. L'agent STR a corrigé le même défaut
    /// dans son application de bureau (v1.6.2) : nouvel essai une minute plus tard.
    /// </summary>
    private readonly Dictionary<string, DateTime> _failedAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Délai avant de retenter une image qui n'a pas pu être obtenue.</summary>
    internal TimeSpan RetryFailedAfter { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Téléchargements en cours, par URL. Deux demandes de la même image — la tuile
    /// d'une station et la vignette « En ce moment » de cette même station, au
    /// lancement — partagent un seul téléchargement au lieu d'écrire toutes deux le
    /// même fichier : la perdante échouait, et notait l'image comme introuvable
    /// pour toute la session alors qu'elle venait d'être enregistrée.
    /// </summary>
    private readonly Dictionary<string, Task<string?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _gate = new(4);

    /// <summary>
    /// Fichiers déjà passés au décodeur d'images pendant cette session : vrai quand
    /// il sait les lire. Voir <see cref="CanDecode(string)"/>.
    /// </summary>
    private readonly Dictionary<string, bool> _decodable = new(StringComparer.Ordinal);

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
                if (known is not null)
                {
                    return known;
                }

                if (_failedAt.TryGetValue(url, out var when) && DateTime.UtcNow - when < RetryFailedAfter)
                {
                    return null;
                }

                // Échec ancien : on retente.
                _resolved.Remove(url);
                _failedAt.Remove(url);
            }
        }

        var path = Path.Combine(_directory, FileNameFor(url));

        if (IsUsable(path))
        {
            Remember(url, path);
            return path;
        }

        Task<string?> download;

        lock (_resolved)
        {
            if (!_inFlight.TryGetValue(url, out var running))
            {
                // Task.Run : le téléchargement démarre hors du verrou, et hors du
                // fil de l'interface.
                var target = url;
                running = Task.Run(() => DownloadToFileAsync(target, path));
                _inFlight[url] = running;
            }

            download = running;
        }

        // Le jeton de l'appelant n'interrompt que SON attente. Le téléchargement,
        // partagé, va au bout : l'annuler pour un appelant priverait les autres de
        // l'image, et la noterait introuvable pour la session.
        return await download.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Télécharge une image vers son fichier de cache. Un seul appel à la fois par
    /// URL (voir <see cref="_inFlight"/>). Ne lève jamais : null en cas d'échec.
    /// </summary>
    private async Task<string?> DownloadToFileAsync(string url, string path)
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            // Une autre tâche a pu télécharger la même image pendant l'attente.
            if (IsUsable(path))
            {
                Remember(url, path);
                return path;
            }

            var bytes = await DownloadAsync(url, CancellationToken.None).ConfigureAwait(false);

            if (bytes is null || !CanDecode(bytes))
            {
                Remember(url, null);
                return null;
            }

            // Écriture par un fichier temporaire puis renommage : une image à
            // moitié écrite, si l'application est tuée, ne sera jamais servie.
            var temp = path + ".part";
            await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);

            Remember(url, path);
            return path;
        }
        catch (Exception)
        {
            // Un échec d'écriture n'efface pas une image qui est bel et bien là.
            var found = IsUsable(path) ? path : null;
            Remember(url, found);
            return found;
        }
        finally
        {
            _gate.Release();

            lock (_resolved)
            {
                _inFlight.Remove(url);
            }
        }
    }

    /// <summary>Vrai quand le fichier existe et n'est pas vide. Ne lève jamais.</summary>
    private static bool IsUsable(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch (Exception)
        {
            return false;
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

        // Un fichier que le décodeur ne sait pas lire — un logo SVG, notamment, ou
        // une image déjà en cache avant ce contrôle — compte comme une image
        // absente : la chaîne passe au candidat suivant, ou la tuile à son symbole.
        if (path is null || !IsDecodable(path))
        {
            return null;
        }

        return ImageSource.FromStream(() => (Stream)File.OpenRead(path));
    }

    /// <summary>
    /// <see cref="CanDecode(string)"/>, mémorisé pour la session : un fichier du
    /// cache ne change pas tant qu'il garde son nom.
    /// </summary>
    private bool IsDecodable(string path)
    {
        lock (_decodable)
        {
            if (_decodable.TryGetValue(path, out var known))
            {
                return known;
            }
        }

        var result = CanDecode(path);

        lock (_decodable)
        {
            _decodable[path] = result;
        }

        return result;
    }

    /// <summary>
    /// Vrai quand le décodeur d'images d'Android sait lire ce fichier.
    ///
    /// Pourquoi ce contrôle : un logo que le contrôle Image ne sait pas décoder ne
    /// s'affiche pas, et sur Android il ne retire pas pour autant l'image d'avant.
    /// La tuile d'une touche de la seconde rangée gardait ainsi le logo de la
    /// touche de la première rangée qu'elle montrait juste avant. Le cas typique
    /// est un logo SVG, que le décodeur d'Android ne lit pas.
    ///
    /// Seules les dimensions sont lues, sans charger l'image. Ne lève jamais.
    /// </summary>
    private static bool CanDecode(string path)
    {
#if ANDROID
        try
        {
            var options = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            Android.Graphics.BitmapFactory.DecodeFile(path, options);
            return options.OutWidth > 0 && options.OutHeight > 0;
        }
        catch (Exception)
        {
            return false;
        }
#else
        return true;
#endif
    }

    /// <summary><see cref="CanDecode(string)"/> pour une image encore en mémoire.</summary>
    private static bool CanDecode(byte[] bytes)
    {
#if ANDROID
        try
        {
            var options = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options);
            return options.OutWidth > 0 && options.OutHeight > 0;
        }
        catch (Exception)
        {
            return false;
        }
#else
        return bytes.Length > 0;
#endif
    }

    /// <summary>Vide le cache disque. Utile depuis les réglages si un logo a changé.</summary>
    public void Clear()
    {
        lock (_resolved)
        {
            _resolved.Clear();
            _failedAt.Clear();
        }

        lock (_decodable)
        {
            _decodable.Clear();
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
    /// Le SVG passe ce premier tri ; c'est <see cref="CanDecode(byte[])"/> qui l'écarte
    /// ensuite sur Android, où il ne s'affiche pas.
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

            if (path is null)
            {
                _failedAt[url] = DateTime.UtcNow;
            }
            else
            {
                _failedAt.Remove(url);
            }
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

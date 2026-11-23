using System.Net.Http.Headers;
using System.Text;

namespace SoundReborn.Core.Recording;

/// <summary>Un flux ouvert, prêt à être copié. Le libérer ferme la connexion.</summary>
public sealed class PreparedStream : IDisposable
{
    private readonly HttpResponseMessage _response;

    internal PreparedStream(HttpResponseMessage response, Stream body, string contentType, string extension, string finalUrl)
    {
        _response = response;
        Body = body;
        ContentType = contentType;
        Extension = extension;
        FinalUrl = finalUrl;
    }

    public Stream Body { get; }

    public string ContentType { get; }

    /// <summary>« .mp3 », « .aac » ou « .ogg » : le fichier garde le format du flux.</summary>
    public string Extension { get; }

    /// <summary>L'adresse réellement ouverte, après une éventuelle liste .pls ou .m3u.</summary>
    public string FinalUrl { get; }

    public void Dispose()
    {
        Body.Dispose();
        _response.Dispose();
    }
}

/// <summary>
/// Ouvre le flux d'une radio pour l'enregistrer.
///
/// - Une liste .pls ou .m3u est lue, et sa première adresse est ouverte. On ne
///   suit qu'un niveau de liste.
/// - Le HLS (.m3u8) est découpé en morceaux : il n'est pas pris en charge.
/// - La requête ne demande PAS les titres intercalés (en-tête Icy-MetaData) : le
///   serveur envoie alors l'audio pur, et le fichier se lit tel quel.
/// - Une réponse qui n'est pas de l'audio (page d'erreur, portail Wi-Fi d'un
///   hôtel) est refusée avant d'écrire quoi que ce soit.
/// </summary>
public static class StreamPreparer
{
    /// <summary>Délai pour recevoir les en-têtes d'une réponse.</summary>
    private static readonly TimeSpan HeadersTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Une liste de lecture plus longue n'en est pas une.</summary>
    private const int MaxPlaylistBytes = 64 * 1024;

    private static readonly string[] PlaylistTypes = { "audio/x-scpls", "audio/mpegurl", "audio/x-mpegurl" };

    private static readonly string[] HlsTypes = { "application/vnd.apple.mpegurl", "application/x-mpegurl" };

    /// <summary>L'extension du fichier selon le type du flux, ou null si ce n'est pas un audio connu.</summary>
    public static string? ExtensionFor(string? contentType)
        => contentType?.Trim().ToLowerInvariant() switch
        {
            "audio/mpeg" or "audio/mp3" or "audio/mpeg3" => ".mp3",
            "audio/aac" or "audio/aacp" or "audio/x-aac" => ".aac",
            "audio/ogg" => ".ogg",
            _ => null,
        };

    public static async Task<PreparedStream> OpenAsync(HttpClient http, string url, CancellationToken ct)
    {
        var response = await GetAsync(http, url, ct).ConfigureAwait(false);
        var type = MediaType(response.Content.Headers.ContentType);

        if (IsHls(url, type))
        {
            response.Dispose();
            throw new RecordingException(RecordingRefusal.Hls);
        }

        if (IsPlaylist(url, type))
        {
            string text;

            try
            {
                text = await ReadLimitedAsync(response, ct).ConfigureAwait(false);
            }
            finally
            {
                response.Dispose();
            }

            if (text.Contains("#EXT-X-", StringComparison.OrdinalIgnoreCase))
            {
                // Une liste HLS servie avec un type de liste ordinaire.
                throw new RecordingException(RecordingRefusal.Hls);
            }

            var inner = FirstUrlInPlaylist(text) ?? throw new RecordingException(RecordingRefusal.NotAudio);

            if (StreamUrlResolver.IsHls(inner))
            {
                throw new RecordingException(RecordingRefusal.Hls);
            }

            response = await GetAsync(http, inner, ct).ConfigureAwait(false);
            type = MediaType(response.Content.Headers.ContentType);
            url = inner;
        }

        var extension = ExtensionFor(type);

        if (extension is null)
        {
            response.Dispose();
            throw new RecordingException(RecordingRefusal.NotAudio);
        }

        try
        {
            var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return new PreparedStream(response, body, type!, extension, url);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            response.Dispose();
            throw new RecordingException(RecordingRefusal.Network, ex);
        }
    }

    /// <summary>La première adresse http(s) d'une liste .pls (« FileN=… ») ou .m3u (une adresse par ligne).</summary>
    /// <summary>
    /// Ce qu'on peut dire d'un flux qui n'a pas pu s'ouvrir : « HTTP 403 »,
    /// « timeout », le type d'erreur réseau… Null quand on ne sait rien de plus.
    /// </summary>
    public static string? Describe(RecordingException ex)
    {
        if (!string.IsNullOrWhiteSpace(ex.Detail))
        {
            return ex.Detail;
        }

        return ex.InnerException switch
        {
            OperationCanceledException => "timeout",
            HttpRequestException { HttpRequestError: not HttpRequestError.Unknown } http => http.HttpRequestError.ToString(),
            null => null,
            var inner => inner.GetType().Name,
        };
    }

    /// <summary>
    /// L'adresse à donner au lecteur du téléphone pour écouter une station : la
    /// première adresse d'une liste .pls ou .m3u, l'adresse telle quelle sinon (flux
    /// direct ou HLS .m3u8, que le lecteur d'Android ouvre lui-même). Une liste
    /// illisible ou vide laisse l'adresse d'origine : le lecteur dira s'il la lit.
    /// </summary>
    public static async Task<string> ResolveForPlaybackAsync(HttpClient http, string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Adresse de flux invalide.", nameof(url));
        }

        var path = uri.AbsolutePath;

        if (!path.EndsWith(".pls", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
        {
            return uri.ToString();
        }

        try
        {
            using var response = await GetAsync(http, uri.ToString(), ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxPlaylistBytes)
            {
                return uri.ToString();
            }

            // Lecture bornée en taille et en temps : une adresse en .m3u qui serait en
            // fait un flux audio ne doit pas être lue sans fin.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(HeadersTimeout);
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var buffer = new byte[MaxPlaylistBytes + 1];
            var total = 0;
            int read;

            while (total < buffer.Length &&
                   (read = await body.ReadAsync(buffer.AsMemory(total), timeout.Token).ConfigureAwait(false)) > 0)
            {
                total += read;
            }

            if (total > MaxPlaylistBytes)
            {
                return uri.ToString();
            }

            return FirstUrlInPlaylist(System.Text.Encoding.UTF8.GetString(buffer, 0, total)) ?? uri.ToString();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return uri.ToString();
        }
    }

    public static string? FirstUrlInPlaylist(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var equals = line.IndexOf('=');

            if (equals > 0 && line.StartsWith("File", StringComparison.OrdinalIgnoreCase))
            {
                line = line[(equals + 1)..].Trim();
            }

            if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }
        }

        return null;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HeadersTimeout);
        HttpResponseMessage? response = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or UriFormatException or InvalidOperationException)
        {
            throw new RecordingException(RecordingRefusal.Network, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new RecordingException(RecordingRefusal.Network, null, $"HTTP {status}");
        }

        return response;
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[MaxPlaylistBytes];
        var total = 0;
        int read;

        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false)) > 0)
        {
            total += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static string? MediaType(MediaTypeHeaderValue? header)
        => header?.MediaType?.Trim().ToLowerInvariant();

    private static bool IsHls(string url, string? type)
        => StreamUrlResolver.IsHls(url) || (type is not null && HlsTypes.Contains(type));

    private static bool IsPlaylist(string url, string? type)
    {
        var path = url.Split('?', '#')[0];

        return path.EndsWith(".pls", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
               (type is not null && PlaylistTypes.Contains(type));
    }
}

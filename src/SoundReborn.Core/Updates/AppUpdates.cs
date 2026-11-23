using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoundReborn.Core;

/// <summary>Le fichier APK joint à une version GitHub, avec son empreinte.</summary>
public sealed class ReleaseApk
{
    public required string Name { get; init; }

    public required Uri DownloadUrl { get; init; }

    public required long Size { get; init; }

    /// <summary>Empreinte SHA-256 publiée par GitHub, 64 chiffres hexadécimaux en minuscules.</summary>
    public required string Sha256 { get; init; }
}

/// <summary>La dernière version publiée de l'application sur GitHub.</summary>
public sealed class ReleaseInfo
{
    public required Version Version { get; init; }

    /// <summary>« v1.9.1 », tel que sur GitHub.</summary>
    public required string Tag { get; init; }

    /// <summary>Page de la version, pour « Nouveautés » et le repli sans installation.</summary>
    public required Uri PageUrl { get; init; }

    /// <summary>L'APK à installer ; null quand la version n'en a pas, ou un APK douteux.</summary>
    public ReleaseApk? Apk { get; init; }
}

/// <summary>
/// Lit la dernière version de l'application sur GitHub et télécharge son APK.
///
/// Une seule adresse est interrogée, celle du dépôt de l'application ; aucune
/// donnée de l'utilisateur ne part, GitHub voit seulement une requête anonyme.
///
/// La confiance repose sur deux contrôles :
///   – l'APK téléchargé doit avoir exactement la taille et l'empreinte SHA-256
///     que l'API de GitHub annonce pour ce fichier ;
///   – Android refuse ensuite d'installer un APK signé d'une autre clé que celle de
///     l'application en place.
/// </summary>
public sealed class AppUpdateClient
{
    /// <summary>Le dépôt de l'application. Rien d'autre n'est jamais interrogé.</summary>
    public const string Repository = "ben240374/SoundRebornRemote";

    public static readonly Uri LatestUrl = new($"https://api.github.com/repos/{Repository}/releases/latest");

    /// <summary>Une réponse de l'API plus grosse n'est pas une fiche de version.</summary>
    public const int MaxReleaseJsonBytes = 1024 * 1024;

    /// <summary>Un APK plus gros n'est pas le nôtre (la 1.9.0 pèse 45 Mo).</summary>
    public const long MaxApkBytes = 200L * 1024 * 1024;

    public static TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

    private const string UserAgent = "SoundRebornRemote (+https://github.com/ben240374/SoundRebornRemote)";

    private readonly HttpClient _http;

    public AppUpdateClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <summary>
    /// « 1.9.1 », « v1.9.1 » ou « 1.9 » en version comparable ; null pour autre
    /// chose (« beta », « 1.9.1-rc1 »). Les parties absentes valent zéro.
    /// </summary>
    public static Version? ParseVersion(string? text)
    {
        var raw = (text ?? "").Trim();

        if (raw.StartsWith('v') || raw.StartsWith('V'))
        {
            raw = raw[1..];
        }

        var parts = raw.Split('.');

        if (parts.Length is < 2 or > 4)
        {
            return null;
        }

        var numbers = new int[4];

        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length is 0 or > 6 || !parts[i].All(char.IsAsciiDigit) ||
                !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    /// <summary>La dernière version publiée (ni brouillon, ni pré-version).</summary>
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Aucune version publiée.
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new SpeakerException($"GitHub a répondu {(int)response.StatusCode}.", null, null, response.StatusCode);
        }

        var json = await ReadBoundedAsync(response.Content, MaxReleaseJsonBytes, cts.Token).ConfigureAwait(false);

        return Parse(json);
    }

    /// <summary>Lit la fiche d'une version ; null si elle est inutilisable.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        GitHubRelease? release;

        try
        {
            release = JsonSerializer.Deserialize<GitHubRelease>(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (release is null || release.Draft || release.Prerelease || ParseVersion(release.TagName) is not { } version)
        {
            return null;
        }

        if (!Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var page) || !IsRepositoryPage(page))
        {
            page = new Uri($"https://github.com/{Repository}/releases/tag/{Uri.EscapeDataString(release.TagName!)}");
        }

        return new ReleaseInfo
        {
            Version = version,
            Tag = release.TagName!,
            PageUrl = page,
            Apk = (release.Assets ?? new List<GitHubAsset>()).Select(ToApk).FirstOrDefault(a => a is not null),
        };
    }

    /// <summary>Un fichier joint n'est retenu que s'il a tout d'un APK de ce dépôt.</summary>
    private static ReleaseApk? ToApk(GitHubAsset? asset)
    {
        if (asset is null ||
            asset.Name is null || !asset.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ||
            asset.State is not (null or "uploaded") ||
            asset.Size <= 0 || asset.Size > MaxApkBytes ||
            !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps ||
            !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !url.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // « sha256:… » : sans empreinte, pas d'installation depuis l'application.
        var digest = asset.Digest ?? "";

        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hex = digest[7..].ToLowerInvariant();

        if (hex.Length != 64 || !hex.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        return new ReleaseApk { Name = asset.Name, DownloadUrl = url, Size = asset.Size, Sha256 = hex };
    }

    private static bool IsRepositoryPage(Uri page)
        => page.Scheme == Uri.UriSchemeHttps &&
           page.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
           page.AbsolutePath.StartsWith($"/{Repository}/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Télécharge l'APK dans <paramref name="destination"/>. Le fichier n'est
    /// gardé que si sa taille et son empreinte sont exactement celles annoncées ;
    /// sinon il est effacé et une exception explique pourquoi.
    /// </summary>
    /// <param name="progress">Avancement, de 0 à 1.</param>
    public async Task DownloadAsync(ReleaseApk apk, string destination, IProgress<double>? progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(apk);

        var temp = destination + ".part";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, apk.DownloadUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new SpeakerException($"Téléchargement refusé ({(int)response.StatusCode}).", null, null, response.StatusCode);
            }

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;

            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var file = File.Create(temp))
            {
                var buffer = new byte[81920];
                int read;

                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += read;

                    if (total > apk.Size)
                    {
                        throw new InvalidDataException("Le fichier reçu est plus gros qu'annoncé.");
                    }

                    sha.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    progress?.Report((double)total / apk.Size);
                }
            }

            if (total != apk.Size)
            {
                throw new InvalidDataException("Le fichier reçu est incomplet.");
            }

            var hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();

            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(apk.Sha256)))
            {
                throw new InvalidDataException("L'empreinte du fichier ne correspond pas à celle publiée sur GitHub.");
            }

            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Un reste dans le cache : sans conséquence, effacé au prochain essai.
        }
    }

    internal static async Task<string> ReadBoundedAsync(HttpContent content, int max, CancellationToken ct)
    {
        if (content.Headers.ContentLength > max)
        {
            throw new InvalidDataException("Réponse de GitHub trop grosse.");
        }

        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;

        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > max)
            {
                throw new InvalidDataException("Réponse de GitHub trop grosse.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}

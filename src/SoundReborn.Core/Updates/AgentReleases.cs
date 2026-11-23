using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoundReborn.Core;

/// <summary>La dernière version publiée de l'agent STR (le projet de Jens Roggenfelder).</summary>
public sealed class AgentRelease
{
    public required Version Version { get; init; }

    /// <summary>« v1.9.1 », tel que sur GitHub.</summary>
    public required string Tag { get; init; }

    /// <summary>La page de la version : ses nouveautés.</summary>
    public required Uri PageUrl { get; init; }

    /// <summary>« 1.9.1 ».</summary>
    public string Label => $"{Version.Major}.{Version.Minor}.{Version.Build}";
}

/// <summary>
/// Lit sur GitHub la dernière version de l'agent STR, pour dire quelles enceintes
/// ont un agent plus ancien. Rien n'est installé d'ici : la mise à jour de l'agent
/// se fait avec l'application STR sur ordinateur.
///
/// Une seule adresse est interrogée, celle du dépôt de STR ; aucune donnée de
/// l'utilisateur ne part, GitHub voit seulement une requête anonyme.
/// </summary>
public sealed class AgentReleaseClient
{
    /// <summary>Le dépôt de STR. Rien d'autre n'est interrogé.</summary>
    public const string Repository = "JRpersonal/streborn";

    public static readonly Uri LatestUrl = new($"https://api.github.com/repos/{Repository}/releases/latest");

    /// <summary>La page des versions, quand la fiche n'en donne pas de lisible.</summary>
    public static readonly Uri ReleasesPage = new($"https://github.com/{Repository}/releases/latest");

    /// <summary>Une réponse plus grosse n'est pas une fiche de version.</summary>
    public const int MaxReleaseJsonBytes = 1024 * 1024;

    public static TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

    private const string UserAgent = "SoundRebornRemote (+https://github.com/ben240374/SoundRebornRemote)";

    private readonly HttpClient _http;

    public AgentReleaseClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <summary>La dernière version publiée (ni brouillon, ni pré-version) ; null s'il n'y en a pas.</summary>
    public async Task<AgentRelease?> GetLatestAsync(CancellationToken ct = default)
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
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new SpeakerException($"GitHub a répondu {(int)response.StatusCode}.", null, null, response.StatusCode);
        }

        var json = await AppUpdateClient.ReadBoundedAsync(response.Content, MaxReleaseJsonBytes, cts.Token).ConfigureAwait(false);
        return Parse(json);
    }

    /// <summary>Lit la fiche d'une version ; null si elle est inutilisable.</summary>
    public static AgentRelease? Parse(string json)
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

        if (release is null || release.Draft || release.Prerelease ||
            AppUpdateClient.ParseVersion(release.TagName) is not { } version)
        {
            return null;
        }

        if (!Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var page) ||
            page.Scheme != Uri.UriSchemeHttps ||
            !page.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !page.AbsolutePath.StartsWith($"/{Repository}/", StringComparison.OrdinalIgnoreCase))
        {
            page = new Uri($"https://github.com/{Repository}/releases/tag/{Uri.EscapeDataString(release.TagName!)}");
        }

        return new AgentRelease { Version = new Version(version.Major, version.Minor, Math.Max(0, version.Build)), Tag = release.TagName!, PageUrl = page };
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    }
}

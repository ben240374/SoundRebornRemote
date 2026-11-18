using SoundReborn.Core.Models;

namespace SoundReborn.Core.Recording;

/// <summary>
/// Trouve l'adresse du flux que joue l'enceinte, pour l'enregistrer depuis le
/// téléphone.
///
/// Le son ne passe jamais par le téléphone : c'est l'enceinte qui va chercher la
/// radio, le plus souvent à travers le relais de l'agent STR. Le téléphone ouvre
/// donc le flux D'ORIGINE, en parallèle. Il n'ouvre jamais le relais
/// (« /stream/N », « /spotify/… ») ni l'adresse de l'enceinte : une seconde
/// lecture sur le relais pourrait couper ce que l'enceinte joue.
///
/// Trois endroits, dans cet ordre :
/// 1. l'historique de l'agent : sa dernière entrée, si c'est la station en cours ;
/// 2. le magasin de présélections STR : la présélection du même nom ;
/// 3. la « location » que rapporte l'enceinte, si c'est une vraie adresse de flux.
/// </summary>
public static class StreamUrlResolver
{
    /// <summary>Sources dont le son ne peut pas être enregistré depuis le téléphone.</summary>
    private static readonly HashSet<string> NotRadioSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "SPOTIFY", "DEEZER", "AMAZON", "PANDORA", "IHEART", "BLUETOOTH", "AUX", "PRODUCT", "ALEXA",
    };

    public static StreamResolution Resolve(
        NowPlaying now,
        IReadOnlyList<RecentEntry> recent,
        IReadOnlyList<StrPreset> presets,
        string speakerHost)
    {
        var station = FirstNonEmpty(now.StationName, now.Content?.ItemName) ?? "";

        if (now.IsIdle)
        {
            return Refuse(station, RecordingRefusal.NotPlaying, now.Source);
        }

        if (NotRadioSources.Contains(now.Source))
        {
            return Refuse(station, RecordingRefusal.NotRadio, now.Source);
        }

        var url = FromRecent(recent, station, speakerHost, out var isLibrary);

        if (isLibrary)
        {
            // Un morceau de la bibliothèque musicale n'est pas une émission.
            return Refuse(station, RecordingRefusal.NotRadio, now.Source);
        }

        url ??= FromPresets(presets, station, speakerHost);
        url ??= FromLocation(now.Content?.Location, speakerHost);

        if (url is null)
        {
            return Refuse(station, RecordingRefusal.NoUrl, now.Source);
        }

        if (IsHls(url))
        {
            return new StreamResolution(null, station, RecordingRefusal.Hls, now.Source);
        }

        return new StreamResolution(url, station, null, now.Source);
    }

    /// <summary>Vrai pour une adresse HLS (« .m3u8 »), sans tenir compte de la query.</summary>
    public static bool IsHls(string url)
    {
        var path = url.Split('?', '#')[0];
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FromRecent(IReadOnlyList<RecentEntry> recent, string station, string speakerHost, out bool isLibrary)
    {
        isLibrary = false;

        // Seule la dernière entrée compte : c'est ce qui joue. Une entrée plus
        // ancienne du même nom pourrait porter une adresse périmée.
        var last = recent.Count > 0 ? recent[0] : null;

        if (last is null || station.Length == 0 || !SameName(last.CardName, station))
        {
            return null;
        }

        if (last.IsLibrary)
        {
            isLibrary = true;
            return null;
        }

        return IsDirect(last.CardUrl, speakerHost) ? last.CardUrl : null;
    }

    private static string? FromPresets(IReadOnlyList<StrPreset> presets, string station, string speakerHost)
    {
        if (station.Length == 0)
        {
            return null;
        }

        return presets
            .Where(p => SameName(p.Name, station) && IsDirect(p.StreamUrl, speakerHost))
            .Select(p => p.StreamUrl)
            .FirstOrDefault();
    }

    private static string? FromLocation(string? location, string speakerHost)
        => IsDirect(location, speakerHost) ? location : null;

    /// <summary>
    /// Une adresse http(s) qui n'est ni l'enceinte, ni le téléphone lui-même, ni
    /// un chemin de relais de l'agent. Vaut pour TOUTES les sources d'adresse :
    /// ouvrir le relais en parallèle de l'enceinte risquerait de couper ce qu'elle joue.
    /// </summary>
    private static bool IsDirect(string? url, string speakerHost)
    {
        if (!IsHttp(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;

        if (string.Equals(host, speakerHost, StringComparison.OrdinalIgnoreCase) ||
            host == "127.0.0.1" ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = uri.AbsolutePath;

        if (path.StartsWith("/stream/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/spotify/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsHttp(string? url)
        => !string.IsNullOrWhiteSpace(url) &&
           (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    private static bool SameName(string? a, string b)
        => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static StreamResolution Refuse(string station, string code, string? source)
        => new(null, station, code, source);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

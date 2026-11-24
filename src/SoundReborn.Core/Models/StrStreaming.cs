using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoundReborn.Core.Models;

/// <summary>
/// Réponse de GET /spotify/info : le moteur Spotify de l'agent (« (STR) » dans la
/// liste des appareils de l'application Spotify).
///
/// Il n'y a pas de connexion à faire depuis une application : le compte arrive
/// sur l'enceinte quand quelqu'un la choisit dans Spotify (Spotify Connect). Ce
/// que l'agent en dit sert à l'afficher, et à garder une playlist sur une touche.
///
/// La lecture est tolérante : un corps illisible donne une réponse vide.
/// </summary>
public sealed class StrSpotifyInfo
{
    /// <summary>Le compte avec lequel le moteur de l'enceinte est connecté (identifiant Spotify).</summary>
    public string Account { get; init; } = "";

    /// <summary>Nom affichable des comptes connus de l'enceinte, par identifiant.</summary>
    public IReadOnlyDictionary<string, string> AccountNames { get; init; } = new Dictionary<string, string>();

    /// <summary>Formule du compte : « premium », « free », « open », ou vide quand elle n'est pas connue.</summary>
    public string Product { get; init; } = "";

    /// <summary>L'agent sait que le compte connecté n'a pas Premium.</summary>
    public bool PremiumRequired { get; init; }

    /// <summary>Spotify refuse ce compte sur le moteur de STR ; l'enceinte s'en souvient jusqu'à ce qu'il joue.</summary>
    public bool AccountRefused { get; init; }

    public string AccountRefusedName { get; init; } = "";

    /// <summary>Agent plus ancien : refus constaté dans les dix dernières minutes.</summary>
    public bool AudioKeyRefused { get; init; }

    /// <summary>La playlist, l'album… que le moteur a chargé : « spotify:playlist:… ».</summary>
    public string Context { get; init; } = "";

    /// <summary>Le morceau en cours sur le moteur.</summary>
    public string Track { get; init; } = "";

    public string Artist { get; init; } = "";

    /// <summary>Nom du compte connecté, ou son identifiant à défaut ; vide sans compte.</summary>
    public string AccountLabel
    {
        get
        {
            if (Account.Length == 0)
            {
                return "";
            }

            return AccountNames.TryGetValue(Account, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name.Trim()
                : Account;
        }
    }

    /// <summary>Vrai pour un compte Premium dont la formule est connue.</summary>
    public bool IsPremium => string.Equals(Product, "premium", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Compte gratuit : d'après la formule quand l'enceinte la connaît, sinon
    /// d'après « premiumRequired ». Même règle que la page téléphone de STR.
    /// </summary>
    public bool IsFree => !IsPremium &&
        (string.Equals(Product, "free", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Product, "open", StringComparison.OrdinalIgnoreCase) ||
         PremiumRequired);

    /// <summary>Spotify refuse ce compte sur le moteur de STR (refus mémorisé, ou récent sur un agent plus ancien).</summary>
    public bool IsRefused => AccountRefused || AudioKeyRefused;

    public static StrSpotifyInfo Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new StrSpotifyInfo();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new StrSpotifyInfo();
            }

            var names = new Dictionary<string, string>(StringComparer.Ordinal);

            if (root.TryGetProperty("accountNames", out var map) && map.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in map.EnumerateObject())
                {
                    if (entry.Value.ValueKind == JsonValueKind.String)
                    {
                        names[entry.Name] = entry.Value.GetString() ?? "";
                    }
                }
            }

            return new StrSpotifyInfo
            {
                Account = StreamingJson.Text(root, "account"),
                AccountNames = names,
                Product = StreamingJson.Text(root, "product"),
                PremiumRequired = StreamingJson.Flag(root, "premiumRequired"),
                AccountRefused = StreamingJson.Flag(root, "accountRefused"),
                AccountRefusedName = StreamingJson.Text(root, "accountRefusedName"),
                AudioKeyRefused = StreamingJson.Flag(root, "audioKeyRefused"),
                Context = StreamingJson.Text(root, "context"),
                Track = StreamingJson.Text(root, "track"),
                Artist = StreamingJson.Text(root, "artist"),
            };
        }
        catch (JsonException)
        {
            return new StrSpotifyInfo();
        }
    }
}

/// <summary>
/// Réponse de GET /api/deezer/account (agent 1.9.0) : où en est le client Deezer
/// propre à l'enceinte.
///
/// Une nouvelle connexion à Deezer n'est pas possible sans le cloud Bose : l'agent
/// garde seulement la connexion que l'enceinte avait déjà, et la lui rend quand
/// elle l'a perdue. L'agent décide de l'état ; l'application ne fait que le nommer.
/// </summary>
public sealed class StrDeezerState
{
    public const string Connected = "connected";
    public const string Connecting = "connecting";
    public const string Unavailable = "unavailable";
    public const string NotConnected = "not-connected";
    public const string Unknown = "unknown";

    /// <summary>Réponse d'un agent plus ancien.</summary>
    public const string Failed = "failed";

    /// <summary>connected, connecting, unavailable, not-connected, unknown (ou failed, agent plus ancien).</summary>
    public string State { get; init; } = "";

    public string Detail { get; init; } = "";

    /// <summary>Le compte, masqué par l'agent.</summary>
    public string Account { get; init; } = "";

    /// <summary>Agent plus ancien, avec « failed » : refused ou no-answer.</summary>
    public string Reason { get; init; } = "";

    /// <summary>Le client Deezer de l'enceinte reste bloqué jusqu'à ce qu'on la débranche.</summary>
    public bool Stuck { get; init; }

    /// <summary>Deezer a refusé la connexion gardée (code 3330).</summary>
    public bool Rejected { get; init; }

    /// <summary>Après « Vérifier à nouveau » : l'agent a rendu la connexion gardée à l'enceinte.</summary>
    public bool RecreateRan { get; init; }

    /// <summary>Vrai quand l'agent a répondu un état lisible.</summary>
    public bool IsKnown => State.Length > 0;

    /// <summary>Un compte est présent sur l'enceinte : « Déconnecter » a un sens.</summary>
    public bool CanDisconnect => State is Connected or Connecting or Failed or Unavailable;

    /// <summary>Même règle que les applications de STR : l'agent le dit, ou un agent plus ancien a répondu 1024 ou rien.</summary>
    public bool IsStuck => Stuck ||
        State == Failed && (Reason == "no-answer" || Regex.IsMatch(Detail, @"\b1024\b"));

    /// <summary>Le refus de Deezer ne se signale que tant que l'enceinte n'est pas connectée.</summary>
    public bool ShowRejected => Rejected && State != Connected;

    public static StrDeezerState Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new StrDeezerState();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new StrDeezerState();
            }

            var ran = root.TryGetProperty("recreate", out var recreate) &&
                      recreate.ValueKind == JsonValueKind.Object &&
                      StreamingJson.Flag(recreate, "ran");

            return new StrDeezerState
            {
                State = StreamingJson.Text(root, "state").ToLowerInvariant(),
                Detail = StreamingJson.Text(root, "detail"),
                Account = StreamingJson.Text(root, "account"),
                Reason = StreamingJson.Text(root, "reason").ToLowerInvariant(),
                Stuck = StreamingJson.Flag(root, "stuck"),
                Rejected = StreamingJson.Flag(root, "rejected"),
                RecreateRan = ran,
            };
        }
        catch (JsonException)
        {
            return new StrDeezerState();
        }
    }
}

/// <summary>Réponse de POST /api/deezer/play : ce qui joue maintenant.</summary>
public sealed record StrDeezerPlayed(string Kind, string Name);

/// <summary>
/// Un réglage de l'écran de l'enceinte (agent : /api/box/display-…), comme dans
/// « Réglages de l'enceinte, Son et affichage » de l'application de bureau.
/// Un réglage n'est proposé que si l'enceinte dit savoir le faire.
/// </summary>
public sealed class StrDisplaySetting
{
    public const string Track = "track";
    public const string Clock = "clock";
    public const string Messages = "messages";
    public const string Notices = "notices";
    public const string Splash = "splash";

    /// <summary>Les cinq réglages, dans l'ordre de l'application de bureau.</summary>
    public static readonly string[] Kinds = { Track, Clock, Messages, Notices, Splash };

    /// <summary>Mode du titre en cours : artiste, titre, ou les deux.</summary>
    public const string ModeArtist = "artist";
    public const string ModeTitle = "title";
    public const string ModeBoth = "both";

    public string Kind { get; init; } = "";

    /// <summary>L'enceinte sait le faire : le réglage est proposé.</summary>
    public bool Supported { get; init; }

    public bool Enabled { get; init; }

    /// <summary>Pour le titre en cours : artist, title ou both.</summary>
    public string Mode { get; init; } = ModeBoth;

    public static string PathOf(string kind) => kind switch
    {
        Track => "/api/box/display-track",
        Clock => "/api/box/display-clock",
        Messages => "/api/box/display-messages",
        Notices => "/api/box/display-notices",
        Splash => "/api/box/display-splash",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Réglage d'écran inconnu"),
    };

    /// <summary>
    /// Lit la réponse d'un réglage. Ce qui dit « proposé » n'est pas le même champ
    /// partout, comme dans la page téléphone de STR :
    ///   – titre en cours : « onPanel », l'écran sur lequel STR dessine lui-même
    ///     (sans lui, chaque changement de titre recoupe le flux : l'application de
    ///     bureau demande avant de l'activer, ce qu'on ne fait pas ici) ;
    ///   – messages : « hasDisplay » ;
    ///   – les autres : « supported ».
    /// </summary>
    public static StrDisplaySetting Parse(string kind, string? json)
    {
        var none = new StrDisplaySetting { Kind = kind };

        if (string.IsNullOrWhiteSpace(json))
        {
            return none;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return none;
            }

            var supported = kind switch
            {
                Track => StreamingJson.Flag(root, "onPanel"),
                Messages => StreamingJson.Flag(root, "hasDisplay"),
                _ => StreamingJson.Flag(root, "supported"),
            };

            var mode = StreamingJson.Text(root, "mode").ToLowerInvariant();

            return new StrDisplaySetting
            {
                Kind = kind,
                Supported = supported,
                Enabled = StreamingJson.Flag(root, "enabled"),
                Mode = mode is ModeArtist or ModeTitle or ModeBoth ? mode : ModeBoth,
            };
        }
        catch (JsonException)
        {
            return none;
        }
    }
}

/// <summary>Réponse de POST /api/box/identify (« Trouver cette enceinte »).</summary>
public enum StrIdentifyOutcome
{
    /// <summary>L'écran dit HELLO et le nom de l'enceinte.</summary>
    Shown,

    /// <summary>L'enceinte n'a pas d'écran sur lequel STR sait dessiner.</summary>
    NoDisplay,

    /// <summary>L'écran est occupé (un jeu, par exemple).</summary>
    Busy,

    /// <summary>Pas de réponse lisible.</summary>
    Failed,
}

/// <summary>Ce que « garder ce qui joue sur une touche » a fait.</summary>
public enum KeptKind
{
    /// <summary>Une playlist, un album… Spotify.</summary>
    Spotify,

    /// <summary>Un service propre à l'enceinte : Deezer, Pandora, iHeartRadio.</summary>
    Native,

    /// <summary>L'agent a gardé lui-même ce qui joue : station, dossier, piste.</summary>
    Playing,
}

/// <summary>Résultat de <see cref="StrApiClient.KeepPlayingAsync"/>.</summary>
public sealed record StrKeptPlaying(KeptKind Kind, string Name, bool Unchanged);

/// <summary>
/// Les liens Deezer qu'on peut faire jouer sur l'enceinte, mêmes règles que les
/// applications de STR : un lien deezer.com vers une playlist, un album ou un titre,
/// ou un lien de partage (link.deezer.com, deezer.page.link…) que l'agent suit
/// lui-même auprès de Deezer.
/// </summary>
public static class DeezerLink
{
    private static readonly Regex Canonical = new(
        @"^https?://(?:www\.)?deezer\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?(track|album|playlist)/(\d{1,20})/?(?:[?#].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Share = new(
        @"^https://(?:link\.deezer\.com|deezer\.page\.link|dzr\.page\.link|deezer\.app\.link|deezer\.onelink\.me)/\S+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Url = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Vrai pour un lien que l'agent sait faire jouer.</summary>
    public static bool IsPlayable(string? url)
    {
        var text = (url ?? "").Trim();
        return Canonical.IsMatch(text) || Share.IsMatch(text);
    }

    /// <summary>« playlist », « album », « track », ou vide pour un lien de partage (ou autre chose).</summary>
    public static string KindOf(string? url)
    {
        var match = Canonical.Match((url ?? "").Trim());
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : "";
    }

    /// <summary>
    /// Le premier lien Deezer d'un texte partagé (« Écoute … sur Deezer :
    /// https://link.deezer.com/s/… »). Null quand le texte n'en contient pas.
    /// Une ponctuation collée à la fin du lien est retirée.
    /// </summary>
    public static string? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (Match match in Url.Matches(text))
        {
            var candidate = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '»');

            if (IsPlayable(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>
/// Quelle playlist Spotify garder sur une touche, portage de
/// spotifySaveContextURI (application de bureau et page téléphone de STR).
///
/// Deux choses très différentes jouent du Spotify :
///   – le moteur de STR (adresse …/spotify/stream…) : il connaît la playlist, son
///     contexte l'emporte, l'adresse de l'enceinte ne sert que de secours ;
///   – l'entrée Spotify Connect propre à l'enceinte (source SPOTIFY, adresse
///     /playback/container/&lt;base64 spotify:…&gt;) : seule l'adresse de l'enceinte
///     dit la vérité ; le moteur peut garder une ancienne playlist en pause, qui
///     ne doit jamais être prise à sa place.
/// </summary>
public static class SpotifyContext
{
    private const string ContainerMarker = "/playback/container/";

    /// <summary>L'URI spotify: d'une adresse « /playback/container/&lt;base64&gt; », ou vide.</summary>
    public static string UriFromContainer(string? location)
    {
        var text = location ?? "";
        var at = text.IndexOf(ContainerMarker, StringComparison.Ordinal);

        if (at < 0)
        {
            return "";
        }

        var encoded = text[(at + ContainerMarker.Length)..];
        var end = encoded.IndexOfAny(new[] { '/', '?', '#' });

        if (end >= 0)
        {
            encoded = encoded[..end];
        }

        encoded = encoded.Replace('-', '+').Replace('_', '/');

        // atob accepte un base64 sans remplissage ; Convert l'exige.
        var padding = encoded.Length % 4;

        if (padding == 1)
        {
            return "";
        }

        if (padding > 0)
        {
            encoded += new string('=', 4 - padding);
        }

        try
        {
            var uri = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return uri.StartsWith("spotify:", StringComparison.Ordinal) ? uri : "";
        }
        catch (FormatException)
        {
            return "";
        }
    }

    /// <summary>
    /// L'URI à garder sur une touche pendant que Spotify joue, ou vide quand ce
    /// qui joue n'est pas du Spotify (ou qu'on ne peut pas savoir lequel).
    /// Même règle que saveHeldSlot de la page téléphone.
    /// </summary>
    public static string SaveUri(string? source, string? location, string? engineContext)
    {
        var loc = location ?? "";
        var boxUri = UriFromContainer(loc);

        if (loc.Contains("/spotify/stream", StringComparison.Ordinal))
        {
            return FirstNonEmpty(engineContext, boxUri);
        }

        if (string.Equals(source, "SPOTIFY", StringComparison.OrdinalIgnoreCase) || boxUri.Length > 0)
        {
            return boxUri;
        }

        return "";
    }

    /// <summary>Vrai pour une URI que l'agent accepte sur une touche : spotify:quelque chose.</summary>
    public static bool IsUri(string? uri)
        => !string.IsNullOrEmpty(uri) && uri.Length > "spotify:".Length && uri.StartsWith("spotify:", StringComparison.Ordinal);

    private static string FirstNonEmpty(string? first, string second)
        => string.IsNullOrEmpty(first) ? second : first;
}

/// <summary>Lecture tolérante de champs JSON.</summary>
internal static class StreamingJson
{
    public static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? "").Trim()
            : "";

    public static bool Flag(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

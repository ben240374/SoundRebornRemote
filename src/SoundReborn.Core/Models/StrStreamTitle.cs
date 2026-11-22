using System.Net;
using System.Text.Json;

namespace SoundReborn.Core.Models;

/// <summary>
/// Réponse de GET /api/stream/title : le titre en direct du flux que l'agent
/// relaie.
///
///   – une radio : « title » vaut « Artiste - Titre » (vide quand la station
///     n'envoie rien) ;
///   – une touche Spotify (agent 1.8.2) : « title » reprend le morceau en cours,
///     et « track », « artist » et « art » donnent le morceau seul, l'artiste
///     seul et l'adresse de la pochette. Pas d'album : le moteur Spotify ne le
///     fournit pas.
///
/// Les trois champs Spotify sont absents le reste du temps. La lecture est
/// tolérante : un corps illisible donne un titre vide, jamais une exception.
/// </summary>
public sealed class StrStreamTitle
{
    public string Title { get; init; } = "";

    public bool Swapped { get; init; }

    public string? Track { get; init; }

    public string? Artist { get; init; }

    public string? Art { get; init; }

    /// <summary>Vrai quand l'agent donne le morceau Spotify en cours, au moins son nom.</summary>
    public bool HasSong => !string.IsNullOrWhiteSpace(Track);

    /// <summary>
    /// Le morceau d'une radio, tiré de <see cref="Title"/> : null quand la station
    /// n'envoie rien, ou n'envoie que son propre nom.
    ///
    /// L'agent rend le titre sous la forme « Artiste - Titre » ; il le remet
    /// lui-même dans cet ordre quand la station l'envoie à l'envers (« swapped »).
    /// Le découpage se fait au premier « - » entouré d'espaces. Sans ce séparateur
    /// — une publicité, le nom d'une émission — le texte entier devient le titre,
    /// sans artiste.
    ///
    /// Les codes HTML (« &amp;eacute; », « &amp;#382; ») sont remis en lettres :
    /// l'agent ne le fait lui-même que depuis la version 1.8.3.
    /// </summary>
    public RadioSong? RadioSongFor(string? stationName)
    {
        var title = Clean(Title);

        if (title.Length == 0 ||
            !string.IsNullOrWhiteSpace(stationName) &&
            string.Equals(title, stationName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var cut = title.IndexOf(" - ", StringComparison.Ordinal);

        if (cut > 0)
        {
            var artist = title[..cut].Trim();
            var song = title[(cut + 3)..].Trim();

            if (artist.Length > 0 && song.Length > 0)
            {
                return new RadioSong(song, artist);
            }
        }

        return new RadioSong(title, null);
    }

    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var decoded = text.Contains('&') ? WebUtility.HtmlDecode(text) : text;
        return decoded.Trim();
    }

    public static StrStreamTitle Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new StrStreamTitle();
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new StrStreamTitle();
            }

            return new StrStreamTitle
            {
                Title = Text(root, "title") ?? "",
                Swapped = root.TryGetProperty("swapped", out var swapped) && swapped.ValueKind == JsonValueKind.True,
                Track = Text(root, "track"),
                Artist = Text(root, "artist"),
                Art = HttpUrl(Text(root, "art")),
            };
        }
        catch (JsonException)
        {
            return new StrStreamTitle();
        }
    }

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
           !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    /// <summary>Seule une adresse http(s) est gardée pour la pochette.</summary>
    private static string? HttpUrl(string? value)
        => value is not null &&
           Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? value
            : null;
}

/// <summary>Le morceau qu'une radio annonce : son titre, et son artiste quand la station le donne.</summary>
public sealed record RadioSong(string Song, string? Artist);

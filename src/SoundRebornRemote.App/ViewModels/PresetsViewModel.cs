using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>Une des six touches, telle qu'affichée dans la grille.</summary>
public sealed partial class PresetTile : ObservableObject
{
    [ObservableProperty]
    private int _slot;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _detail = "";

    /// <summary>URL d'origine du logo, gardée pour le cache.</summary>
    [ObservableProperty]
    private string? _artUrl;

    /// <summary>Image déjà téléchargée sur le téléphone.</summary>
    [ObservableProperty]
    private ImageSource? _artSource;

    [ObservableProperty]
    private bool _hasArt;

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>
    /// Vrai pour la touche dont le contenu passe en ce moment. Sert de retour
    /// visuel immédiat à l'appui, avant même que l'enceinte n'ait confirmé.
    /// </summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Symbole de repli quand la station n'a pas de logo : jamais une tuile vide.</summary>
    [ObservableProperty]
    private string _glyph = "▢";

    /// <summary>
    /// Ce que la touche déclenche : adresse du flux, ou URI Spotify. Sert à
    /// reconnaître la touche correspondant à ce qui joue, plus sûrement que le nom.
    /// </summary>
    [ObservableProperty]
    private string _sourceKey = "";

    /// <summary>Type de la touche tel que l'agent le stocke : radio, spotify, queue…</summary>
    [ObservableProperty]
    private string _kind = "";
}

/// <summary>Une station de l'annuaire, avec son logo déjà téléchargé.</summary>
public sealed partial class RadioResult : ObservableObject
{
    public required RadioStation Station { get; init; }

    public string Name => Station.Name;

    public string Summary => Station.Summary;

    [ObservableProperty]
    private ImageSource? _logo;

    [ObservableProperty]
    private bool _hasLogo;
}

/// <summary>Une entrée des listes déroulantes de filtre.</summary>
public sealed class FilterOption
{
    public required string Label { get; init; }

    /// <summary>Valeur envoyée à radio-browser. Null pour « tous ».</summary>
    public string? Value { get; init; }
}

/// <summary>
/// Les six touches, et la recherche de stations qui permet de les garnir.
///
/// Deux sources se complètent pour les touches : le magasin de l'agent STR (nom,
/// logo, type — y compris Spotify) et les présélections matérielles telles que le
/// firmware les rapporte.
///
/// Les logos sont téléchargés par l'application (voir ArtworkCache) puis affichés
/// depuis le fichier local : les sites de stations ont trop souvent un certificat
/// périmé ou refusent une requête sans User-Agent, et le chargeur d'images
/// d'Android échoue alors sans rien dire.
/// </summary>
public sealed partial class PresetsViewModel : BaseViewModel
{
    private const string ArtCacheKey = "preset_art_cache";

    private readonly ArtworkCache _artwork;
    private readonly RadioBrowserClient _radio;

    /// <summary>Dernière URL de logo connue par touche, persistée entre deux lancements.</summary>
    private readonly Dictionary<int, string> _artCache;

    private CancellationTokenSource? _searchCts;

    /// <summary>Durée pendant laquelle un appui protège sa mise en avant.</summary>
    private static readonly TimeSpan OptimisticGrace = TimeSpan.FromSeconds(10);

    private DateTime _optimisticUntil = DateTime.MinValue;

    public PresetsViewModel(SpeakerManager speakers, ArtworkCache artwork, RadioBrowserClient radio) : base(speakers)
    {
        _artwork = artwork;
        _radio = radio;
        _artCache = LoadArtCache();

        for (var slot = 1; slot <= 6; slot++)
        {
            Presets.Add(new PresetTile
            {
                Slot = slot,
                Name = Localization.Get("L_PresetNumber", slot),
                Detail = Localization.Get("L_PresetEmpty"),
                Glyph = SlotNumber(slot),
            });
        }

        BuildFilters();

        Speakers.DeviceChanged += (_, _) => OnMainThread(() => _ = RefreshAsync());

        Localization.LanguageChanged += (_, _) => OnMainThread(() =>
        {
            RelabelEmptyTiles();
            BuildFilters();
        });
    }

    public ObservableCollection<PresetTile> Presets { get; } = new();

    [ObservableProperty]
    private bool _isRefreshing;

    // ================================================================ Les six touches

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var device = Device;

        if (device is null)
        {
            await ResetAsync().ConfigureAwait(true);
            return;
        }

        IsRefreshing = true;

        try
        {
            // Le firmware d'abord : il dit ce que les touches physiques déclenchent.
            var hardware = new Dictionary<int, PresetInfo>();
            var hardwareRead = false;

            try
            {
                foreach (var preset in await device.GetPresetsAsync().ConfigureAwait(true))
                {
                    hardware[preset.Id] = preset;
                }

                hardwareRead = true;
            }
            catch (Exception)
            {
                // Une enceinte en veille profonde refuse /presets : on continue avec STR seul.
            }

            var store = new Dictionary<int, StrPreset>();
            var storeRead = false;

            if (device.Str is not null)
            {
                try
                {
                    foreach (var preset in await device.Str.GetPresetsAsync().ConfigureAwait(true))
                    {
                        store[preset.Slot] = preset;
                    }

                    storeRead = true;
                }
                catch (Exception)
                {
                    // Agent trop ancien, appel expiré, ou magasin vide.
                }
            }

            // Une lecture QUI ÉCHOUE n'est pas une lecture QUI NE RAMÈNE RIEN.
            // Les deux échecs étant absorbés juste au-dessus, la suite prenait le
            // silence pour une grille vide et effaçait les six touches — ce qui
            // arrivait chaque fois qu'une commande tombait pendant une reconnexion.
            // Tant qu'aucune des deux sources n'a répondu, on garde l'affichage.
            if (!hardwareRead && !storeRead)
            {
                ShowInfo(Localization.Get("S_PresetsUnread"));
                return;
            }

            foreach (var tile in Presets)
            {
                await ApplyAsync(tile, hardware.GetValueOrDefault(tile.Slot), store.GetValueOrDefault(tile.Slot))
                    .ConfigureAwait(true);
            }

            SaveArtCache();

            ShowInfo(store.Count == 0 && hardware.Count == 0 ? Localization.Get("S_NoPresets") : null);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private Task RecallAsync(PresetTile? tile)
    {
        if (tile is null || tile.IsEmpty)
        {
            return Task.CompletedTask;
        }

        MarkActive(tile, optimistic: true);

        return RunAsync(d => d.RecallPresetAsync(tile.Slot), Localization.Get("S_PresetStarted", tile.Name));
    }

    /// <summary>
    /// Met en avant une touche et efface les autres.
    ///
    /// Appelée à l'appui avec <paramref name="optimistic"/> : la tuile passe au vert
    /// tout de suite, et cette mise en avant est protégée quelques secondes. Sans ce
    /// sursis, le premier rafraîchissement — où l'enceinte joue encore l'ancienne
    /// source — effacerait le vert aussitôt.
    /// </summary>
    public void MarkActive(PresetTile? active, bool optimistic = false)
    {
        foreach (var tile in Presets)
        {
            tile.IsActive = ReferenceEquals(tile, active) && !tile.IsEmpty;
        }

        _optimisticUntil = optimistic ? DateTime.UtcNow + OptimisticGrace : DateTime.MinValue;
    }

    /// <summary>
    /// Aligne la mise en avant sur ce que l'enceinte joue réellement.
    ///
    /// La correspondance se fait d'abord sur l'adresse du flux — un nom peut différer
    /// d'une source à l'autre — puis sur le nom de la station, enfin sur le type pour
    /// Spotify. Quand rien ne correspond, la mise en avant est ENLEVÉE : c'est ce qui
    /// manquait, et une touche restait verte alors que Spotify avait pris la main.
    /// </summary>
    public void SyncActive(string source, string? stationName, string? location, bool isIdle)
    {
        if (DateTime.UtcNow < _optimisticUntil)
        {
            return;
        }

        if (isIdle)
        {
            MarkActive(null);
            return;
        }

        PresetTile? match = null;

        if (!string.IsNullOrWhiteSpace(location))
        {
            match = Presets.FirstOrDefault(t =>
                !t.IsEmpty &&
                !string.IsNullOrWhiteSpace(t.SourceKey) &&
                string.Equals(t.SourceKey, location, StringComparison.OrdinalIgnoreCase));
        }

        if (match is null && !string.IsNullOrWhiteSpace(stationName))
        {
            match = Presets.FirstOrDefault(t =>
                !t.IsEmpty && string.Equals(t.Name, stationName, StringComparison.OrdinalIgnoreCase));
        }

        if (match is null && source.Equals("SPOTIFY", StringComparison.OrdinalIgnoreCase))
        {
            // Une seule touche Spotify : c'est forcément elle. Plusieurs : on ne
            // devine pas, aucune n'est mise en avant.
            var spotify = Presets
                .Where(t => !t.IsEmpty && t.Kind.Equals("spotify", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (spotify.Count == 1)
            {
                match = spotify[0];
            }
        }

        MarkActive(match);
    }

    private async Task ApplyAsync(PresetTile tile, PresetInfo? hardware, StrPreset? store)
    {
        // L'agent stocke l'art comme une CHAÎNE de candidats séparés par des « | ».
        // On concatène nos trois sources dans l'ordre de préférence et ArtworkCache
        // essaiera chaque adresse jusqu'à en trouver une qui donne une image.
        var art = JoinCandidates(store?.Art, hardware?.ArtUrl, _artCache.GetValueOrDefault(tile.Slot));

        if (store is not null)
        {
            tile.Name = store.DisplayName;
            tile.IsEmpty = false;

            tile.Detail = store.Type?.ToLowerInvariant() switch
            {
                "spotify" => string.IsNullOrWhiteSpace(store.Account) ? "Spotify" : $"Spotify · {store.Account}",
                "queue" => Localization.Get("L_SrcLibrary"),
                "radio" => store.Bitrate > 0
                    ? $"{store.Bitrate} kbit/s"
                    : Localization.Get("L_SrcRadio"),
                _ => store.Source ?? store.Type ?? "STR",
            };

            tile.Glyph = SlotNumber(tile.Slot);
            tile.Kind = store.Type ?? "";
            tile.SourceKey = FirstNonEmpty(store.StreamUrl, store.Uri) ?? "";
            await SetArtAsync(tile, art).ConfigureAwait(true);
            return;
        }

        if (hardware is { IsEmpty: false })
        {
            tile.Name = hardware.Name;
            tile.Detail = hardware.Source;
            tile.IsEmpty = false;
            tile.Glyph = SlotNumber(tile.Slot);
            tile.Kind = hardware.Source;
            tile.SourceKey = hardware.Content?.Location ?? "";
            await SetArtAsync(tile, art).ConfigureAwait(true);
            return;
        }

        // Touche réellement vide côté enceinte ET côté agent : on oublie son image.
        tile.Name = Localization.Get("L_PresetNumber", tile.Slot);
        tile.Detail = Localization.Get("L_PresetEmpty");
        tile.Glyph = SlotNumber(tile.Slot);
        tile.Kind = "";
        tile.SourceKey = "";
        tile.ArtUrl = null;
        tile.ArtSource = null;
        tile.HasArt = false;
        tile.IsActive = false;
        _artCache.Remove(tile.Slot);
    }

    private async Task SetArtAsync(PresetTile tile, string? art)
    {
        if (string.IsNullOrWhiteSpace(art))
        {
            // Pas d'image cette fois-ci : on garde celle qui est déjà affichée
            // plutôt que de vider la tuile.
            return;
        }

        var (source, used) = await _artwork.GetFromChainAsync(art).ConfigureAwait(true);

        if (source is null || used is null)
        {
            // Aucun candidat n'a donné d'image : le symbole de repli reste en place.
            return;
        }

        tile.ArtUrl = used;
        tile.ArtSource = source;
        tile.HasArt = true;

        // On mémorise l'adresse qui a réellement marché, pas toute la chaîne.
        _artCache[tile.Slot] = used;
    }

    /// <summary>Première valeur non vide parmi celles proposées, null si toutes le sont.</summary>
    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>
    /// Assemble plusieurs sources d'art en une seule chaîne de candidats, sans
    /// doublon et en gardant l'ordre de préférence.
    /// </summary>
    private static string? JoinCandidates(params string?[] sources)
    {
        var all = new List<string>();

        foreach (var source in sources)
        {
            foreach (var candidate in ArtworkCache.SplitCandidates(source))
            {
                if (!all.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    all.Add(candidate);
                }
            }
        }

        return all.Count == 0 ? null : string.Join('|', all);
    }

    private void RelabelEmptyTiles()
    {
        foreach (var tile in Presets.Where(t => t.IsEmpty))
        {
            tile.Name = Localization.Get("L_PresetNumber", tile.Slot);
            tile.Detail = Localization.Get("L_PresetEmpty");
        }
    }

    private async Task ResetAsync()
    {
        foreach (var tile in Presets)
        {
            await ApplyAsync(tile, null, null).ConfigureAwait(true);
        }
    }

    // ================================================================ Recherche de stations

    public ObservableCollection<RadioResult> Results { get; } = new();

    public ObservableCollection<string> CountryLabels { get; } = new();

    public ObservableCollection<string> LanguageLabels { get; } = new();

    private readonly List<FilterOption> _countries = new();
    private readonly List<FilterOption> _languages = new();

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private int _countryIndex;

    [ObservableProperty]
    private int _languageIndex;

    [ObservableProperty]
    private bool _boseCompatibleOnly = true;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasSearched;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _resultsHeader = "";

    [RelayCommand]
    private Task SearchAsync() => RunSearchAsync(top: false);

    [RelayCommand]
    private Task TopAsync() => RunSearchAsync(top: true);

    private async Task RunSearchAsync(bool top)
    {
        // Une frappe rapide sur « Rechercher » annule la requête précédente plutôt
        // que d'empiler deux listes de résultats.
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        IsSearching = true;
        ShowInfo(Localization.Get("S_Searching"));

        try
        {
            var options = new RadioSearchOptions
            {
                Name = top ? null : Query?.Trim(),
                CountryCode = _countries.ElementAtOrDefault(CountryIndex)?.Value,
                Language = _languages.ElementAtOrDefault(LanguageIndex)?.Value,
                BoseCompatibleOnly = BoseCompatibleOnly,
                Limit = 60,
            };

            var stations = top
                ? await _radio.TopAsync(options, ct).ConfigureAwait(true)
                : await _radio.SearchAsync(options, ct).ConfigureAwait(true);

            Results.Clear();

            foreach (var station in stations)
            {
                Results.Add(new RadioResult { Station = station });
            }

            HasSearched = true;
            HasResults = Results.Count > 0;
            ResultsHeader = Localization.Get("L_ResultsHeader", Results.Count);
            ShowInfo(Results.Count == 0 ? Localization.Get("L_NoResults") : null);

            // Les logos arrivent après la liste : l'utilisateur voit les noms tout
            // de suite, les images se remplissent au fil des téléchargements.
            await LoadLogosAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Recherche remplacée par une plus récente.
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsSearching = false;
        }
    }

    private async Task LoadLogosAsync(CancellationToken ct)
    {
        foreach (var result in Results.ToList())
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var source = await _artwork.GetImageSourceAsync(result.Station.Favicon, ct).ConfigureAwait(true);

            if (source is not null)
            {
                result.Logo = source;
                result.HasLogo = true;
            }
        }
    }

    /// <summary>
    /// Propose d'écouter la station tout de suite, ou de l'affecter à l'une des
    /// six touches. La feuille d'actions est la façon native de poser ce choix
    /// sur Android, sans encombrer la page d'un sélecteur permanent.
    /// </summary>
    [RelayCommand]
    private async Task StationActionAsync(RadioResult? result)
    {
        if (result is null)
        {
            return;
        }

        var device = Device;

        if (device is null)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_NoSpeaker");
            return;
        }

        if (device.Str is null)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_NeedAgentStation");
            return;
        }

        var listen = Localization.Get("L_Listen");

        // Chaque entrée dit ce que la touche contient déjà : « ① Remplacer Bel RTL »
        // est plus parlant que « Affecter à la touche 1 », et évite d'écraser une
        // station par erreur.
        var slots = Presets.Select(SlotActionLabel).ToArray();

        var choice = await Shell.Current.DisplayActionSheetAsync(
            result.Name,
            Localization.Get("L_Cancel"),
            null,
            new[] { listen }.Concat(slots).ToArray()).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(choice) || choice == Localization.Get("L_Cancel"))
        {
            return;
        }

        if (choice == listen)
        {
            await RunAsync(
                d => d.PlayStationAsync(result.Station),
                Localization.Get("S_StationPlaying", result.Name)).ConfigureAwait(true);

            _ = _radio.ReportClickAsync(result.Station.Uuid);
            return;
        }

        var index = Array.IndexOf(slots, choice);

        if (index < 0)
        {
            return;
        }

        var slot = index + 1;

        await RunAsync(
            d => d.SaveStationAsync(result.Station, slot),
            Localization.Get("S_StationSaved", result.Name, slot)).ConfigureAwait(true);

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Libellé d'une touche dans la feuille d'actions : le numéro cerclé, puis
    /// « Remplacer la station » quand la touche est occupée, sinon l'invitation à
    /// l'affecter.
    /// </summary>
    private static string SlotActionLabel(PresetTile tile)
    {
        var number = SlotNumber(tile.Slot);

        return tile.IsEmpty
            ? $"{number}  {Localization.Get("L_AssignEmpty")}"
            : $"{number}  {Localization.Get("L_ReplacePreset", tile.Name)}";
    }

    /// <summary>Chiffres cerclés ①..⑥ — des caractères de texte, pas des émojis.</summary>
    private static string SlotNumber(int slot) => slot switch
    {
        1 => "①",
        2 => "②",
        3 => "③",
        4 => "④",
        5 => "⑤",
        6 => "⑥",
        _ => slot.ToString(),
    };

    /// <summary>
    /// Construit les listes de pays et de langues. Les libellés viennent de .NET,
    /// donc ils suivent la langue choisie dans les réglages sans table à maintenir.
    /// </summary>
    private void BuildFilters()
    {
        var previousCountry = _countries.ElementAtOrDefault(CountryIndex)?.Value;
        var previousLanguage = _languages.ElementAtOrDefault(LanguageIndex)?.Value;

        _countries.Clear();
        _countries.Add(new FilterOption { Label = Localization.Get("L_AllCountries"), Value = null });

        // Clés littérales plutôt qu'une concaténation : une clé manquante se voit
        // alors à la relecture, pas à l'exécution.
        _countries.Add(new FilterOption { Label = Localization.Get("L_CtryBE"), Value = "BE" });
        _countries.Add(new FilterOption { Label = Localization.Get("L_CtryFR"), Value = "FR" });
        _countries.Add(new FilterOption { Label = Localization.Get("L_CtryNL"), Value = "NL" });
        _countries.Add(new FilterOption { Label = Localization.Get("L_CtryDE"), Value = "DE" });
        _countries.Add(new FilterOption { Label = Localization.Get("L_CtryLU"), Value = "LU" });
        _countries.Add(new FilterOption { Label = Localization.Get("L_CtryCH"), Value = "CH" });
        _countries.Add(new FilterOption { Label = Localization.Get("L_CtryGB"), Value = "GB" });
        _countries.Add(new FilterOption { Label = Localization.Get("L_CtryES"), Value = "ES" });
        _countries.Add(new FilterOption { Label = Localization.Get("L_CtryIT"), Value = "IT" });
        _countries.Add(new FilterOption { Label = Localization.Get("L_CtryPT"), Value = "PT" });
        _countries.Add(new FilterOption { Label = Localization.Get("L_CtryUS"), Value = "US" });
        _countries.Add(new FilterOption { Label = Localization.Get("L_CtryCA"), Value = "CA" });

        _languages.Clear();
        _languages.Add(new FilterOption { Label = Localization.Get("L_AllLanguages"), Value = null });

        // radio-browser indexe les langues par leur nom anglais en minuscules.
        foreach (var (tag, value) in new[]
                 {
                     ("fr", "french"),
                     ("nl", "dutch"),
                     ("de", "german"),
                     ("en", "english"),
                     ("es", "spanish"),
                     ("it", "italian"),
                 })
        {
            _languages.Add(new FilterOption { Label = LanguageName(tag), Value = value });
        }

        CountryLabels.Clear();

        foreach (var option in _countries)
        {
            CountryLabels.Add(option.Label);
        }

        LanguageLabels.Clear();

        foreach (var option in _languages)
        {
            LanguageLabels.Add(option.Label);
        }

        CountryIndex = Math.Max(0, _countries.FindIndex(o => o.Value == previousCountry));
        LanguageIndex = Math.Max(0, _languages.FindIndex(o => o.Value == previousLanguage));
    }

    private static string LanguageName(string tag)
    {
        try
        {
            var name = CultureInfo.GetCultureInfo(tag).DisplayName;
            return name.Length == 0 ? tag : char.ToUpper(name[0], CultureInfo.CurrentUICulture) + name[1..];
        }
        catch (Exception)
        {
            return tag;
        }
    }

    // ================================================================ Cache des logos

    private static Dictionary<int, string> LoadArtCache()
    {
        try
        {
            var raw = Preferences.Default.Get(ArtCacheKey, string.Empty);

            if (!string.IsNullOrWhiteSpace(raw))
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<int, string>>(raw);

                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }
        catch (Exception)
        {
            // Cache illisible : on repart de zéro, ce n'est que du confort.
        }

        return new Dictionary<int, string>();
    }

    private void SaveArtCache()
    {
        try
        {
            Preferences.Default.Set(ArtCacheKey, JsonSerializer.Serialize(_artCache));
        }
        catch (Exception)
        {
            // Sans importance : les logos seront relus au prochain rafraîchissement.
        }
    }
}

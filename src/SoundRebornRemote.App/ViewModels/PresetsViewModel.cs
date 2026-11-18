using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>
/// Numérotation des douze emplacements de l'agent STR.
///
/// 1 à 6 sont les touches de l'enceinte. 7 à 12 forment la seconde rangée : la
/// touche n pressée deux fois donne l'emplacement 6 + n. À l'écran on parle donc
/// toujours de la touche 1 à 6, en précisant « ×2 » pour la seconde rangée.
/// </summary>
public static class PresetKeys
{
    public const int PerRow = 6;

    public const int Total = 12;

    /// <summary>Vrai pour un emplacement de la seconde rangée (7 à 12).</summary>
    public static bool InSecondRow(int slot) => slot > PerRow;

    /// <summary>Numéro de la touche physique, 1 à 6, quelle que soit la rangée.</summary>
    public static int Number(int slot) => InSecondRow(slot) ? slot - PerRow : slot;

    /// <summary>« 3 », ou « 3 ×2 » en seconde rangée : la touche telle qu'on l'écrit dans une phrase.</summary>
    public static string Label(int slot)
    {
        var number = Number(slot).ToString(CultureInfo.InvariantCulture);
        return InSecondRow(slot) ? number + " ×2" : number;
    }
}

/// <summary>
/// Une des six touches, telle qu'affichée dans la grille. Son emplacement vaut 1 à
/// 6, ou 7 à 12 quand la grille montre la seconde rangée.
/// </summary>
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

    /// <summary>
    /// Emplacement auquel appartient l'image affichée, 0 sans image. Les six tuiles
    /// servent aux deux rangées : après un changement de rangée, l'image encore à
    /// l'écran est celle de l'autre rangée tant que cette valeur diffère de Slot.
    /// </summary>
    public int ArtSlot { get; set; }
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

    /// <summary>
    /// Photo de la dernière lecture réussie, tous emplacements confondus. C'est ce
    /// qui permet de passer d'une rangée à l'autre sans refaire l'aller-retour, et
    /// de dire ce que porte une touche qui n'est pas à l'écran.
    /// </summary>
    private Dictionary<int, PresetInfo> _lastHardware = new();

    private Dictionary<int, StrPreset> _lastStore = new();

    /// <summary>Enceinte à laquelle appartient cette photo. Null tant que rien n'a été lu.</summary>
    private string? _snapshotHost;

    /// <summary>Dernière enceinte dont l'agent a annoncé la seconde rangée.</summary>
    private string? _secondRowHost;

    /// <summary>Dernier état de lecture reçu, pour replacer la mise en avant après un changement de rangée.</summary>
    private (string Source, string? Station, string? Location, bool IsIdle)? _lastSync;

    /// <summary>La carte « Copier les touches » (agent 1.6.5 et suivants) : sa propre vue-modèle.</summary>
    public PresetCopyViewModel Copy { get; }

    public PresetsViewModel(SpeakerManager speakers, ArtworkCache artwork, RadioBrowserClient radio) : base(speakers)
    {
        _artwork = artwork;
        _radio = radio;
        _artCache = LoadArtCache();
        Copy = new PresetCopyViewModel(speakers);

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

    /// <summary>
    /// Vrai quand l'agent de l'enceinte gère la seconde rangée (touches pressées
    /// deux fois). Sans cela le sélecteur de rangée reste caché et rien ne change.
    /// </summary>
    [ObservableProperty]
    private bool _hasSecondRow;

    /// <summary>Vrai quand la grille montre la seconde rangée, emplacements 7 à 12.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstRow))]
    private bool _isSecondRow;

    public bool IsFirstRow => !IsSecondRow;

    // ================================================================ Les six touches

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var device = Device;

        if (device is null)
        {
            // Sans enceinte, pas de seconde rangée : on revient aux six touches.
            ApplyRowSupport(false);
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

            // Agent v1.6.3 et suivants : une touche qui ne porte une station que sur
            // son double appui reçoit côté enceinte une présélection factice (« 2x »),
            // sans quoi l'enceinte ne signalerait pas l'appui. Ce n'est pas une
            // station : l'agent la marque, et la touche « une fois » reste vide.
            if (hardwareRead && hardware.Count > 0 && device.Str is not null)
            {
                try
                {
                    foreach (var marked in await device.Str.GetBoxPresetsAsync().ConfigureAwait(true))
                    {
                        if (marked.IsRow2Placeholder)
                        {
                            hardware.Remove(marked.Slot);
                        }
                    }
                }
                catch (Exception)
                {
                    // Agent plus ancien ou silencieux : il ne pose pas cette touche
                    // factice, la liste du firmware vaut telle quelle.
                }
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

            // Même règle quand c'est l'AGENT qui se tait — enceinte qui se réveille ou
            // redémarre. La liste du firmware seule n'a ni les noms ni les logos que
            // STR range, et elle peut être vide à ce moment-là : reconstruire les
            // touches dessus effaçait leurs images, et le cache des logos avec. On
            // garde donc l'affichage connu de cette enceinte jusqu'à la prochaine
            // lecture complète. Sans affichage connu, le firmware reste mieux que rien.
            if (device.Str is not null && !storeRead && IsSnapshotOf(device))
            {
                ShowInfo(Localization.Get("S_PresetsUnread"));
                return;
            }

            // On garde ce qui vient d'être lu. Une source restée muette ne remplace
            // pas ce que l'on savait d'elle — sauf si l'enceinte a changé entre-temps,
            // auquel cas l'ancienne photo ne vaut plus rien.
            var sameSpeaker = IsSnapshotOf(device);

            if (hardwareRead || !sameSpeaker)
            {
                _lastHardware = hardware;
            }

            if (storeRead || !sameSpeaker)
            {
                _lastStore = store;
            }

            _snapshotHost = device.Host;

            ApplyRowSupport(await DetectSecondRowAsync(device, store, storeRead).ConfigureAwait(true));

            // La seconde rangée ne vit que dans l'agent. S'il n'a pas répondu cette
            // fois-ci, ses touches restent telles qu'elles sont : même règle que
            // plus haut, un silence n'est pas une grille vide.
            var secondRowUnread = false;

            foreach (var tile in Presets)
            {
                if (PresetKeys.InSecondRow(tile.Slot) && !storeRead)
                {
                    secondRowUnread = true;
                    ClearArtOfAnotherSlot(tile);
                    continue;
                }

                await ApplyAsync(tile, hardware.GetValueOrDefault(tile.Slot), store.GetValueOrDefault(tile.Slot))
                    .ConfigureAwait(true);
            }

            SaveArtCache();

            if (secondRowUnread)
            {
                ShowInfo(Localization.Get("S_PresetsUnread"));
            }
            else
            {
                ShowInfo(store.Count == 0 && hardware.Count == 0 ? Localization.Get("S_NoPresets") : null);
            }
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
        _lastSync = (source, stationName, location, isIdle);

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
        // IsEmpty doit redevenir vrai : une tuile réutilisée — touche vidée, ou
        // changement de rangée — resterait sinon cliquable sur un emplacement vide.
        tile.IsEmpty = true;
        tile.Name = Localization.Get("L_PresetNumber", PresetKeys.Number(tile.Slot));
        tile.Detail = Localization.Get("L_PresetEmpty");
        tile.Glyph = SlotNumber(tile.Slot);
        tile.Kind = "";
        tile.SourceKey = "";
        tile.ArtUrl = null;
        tile.ArtSource = null;
        tile.HasArt = false;
        tile.ArtSlot = 0;
        tile.IsActive = false;
        _artCache.Remove(tile.Slot);
    }

    /// <summary>
    /// Pose l'image d'une tuile.
    ///
    /// Une image déjà affichée n'est jamais retirée pour être reposée aussitôt :
    /// quand la nouvelle est disponible tout de suite — c'est le cas dès qu'elle
    /// est dans le cache — elle REMPLACE l'ancienne en une seule affectation.
    /// Vider puis remplir dans le même passage d'interface est précisément ce qui
    /// laissait la tuile sans logo après un changement de rangée.
    /// </summary>
    private async Task SetArtAsync(PresetTile tile, string? art)
    {
        // L'emplacement est relevé AVANT toute attente : si l'utilisateur change de
        // rangée pendant le téléchargement, cette image n'est plus celle de la tuile.
        var slot = tile.Slot;

        ImageSource? source = null;
        string? used = null;

        if (!string.IsNullOrWhiteSpace(art))
        {
            var pending = _artwork.GetFromChainAsync(art);

            if (!pending.IsCompleted)
            {
                // Il faut télécharger. Le logo de l'autre rangée ne doit pas rester
                // sous le nom de cette touche pendant ce temps.
                ClearArtOfAnotherSlot(tile);
            }

            (source, used) = await pending.ConfigureAwait(true);

            if (tile.Slot != slot)
            {
                return;
            }
        }

        if (source is null || used is null)
        {
            // Pas d'image cette fois-ci : on garde celle qui est affichée plutôt que
            // de vider la tuile — à condition que ce soit bien celle de cette touche.
            ClearArtOfAnotherSlot(tile);
            return;
        }

        tile.ArtUrl = used;
        tile.ArtSource = source;
        tile.HasArt = true;
        tile.ArtSlot = slot;

        // On mémorise l'adresse qui a réellement marché, pas toute la chaîne.
        _artCache[slot] = used;
    }

    /// <summary>
    /// Retire l'image d'une tuile quand elle appartient à un autre emplacement que
    /// celui que la tuile porte maintenant. Sans effet sinon.
    /// </summary>
    private static void ClearArtOfAnotherSlot(PresetTile tile)
    {
        if (tile.ArtSlot == tile.Slot)
        {
            return;
        }

        tile.ArtUrl = null;
        tile.ArtSource = null;
        tile.HasArt = false;
        tile.ArtSlot = 0;
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
            tile.Name = Localization.Get("L_PresetNumber", PresetKeys.Number(tile.Slot));
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

    // ================================================================ Seconde rangée

    /// <summary>Commande du sélecteur « Une fois / Deux fois ». Le paramètre vaut "1" ou "2".</summary>
    [RelayCommand]
    private Task ShowRowAsync(string? row) => SelectRowAsync(row == "2");

    /// <summary>
    /// Fait montrer à la grille la première ou la seconde rangée.
    ///
    /// Ce sont les six mêmes tuiles : seul leur emplacement change, 1 à 6 ou 7 à 12.
    /// Les deux onglets partagent la collection, ils suivent donc ensemble.
    /// </summary>
    public async Task SelectRowAsync(bool second)
    {
        second = second && HasSecondRow;

        if (second == IsSecondRow)
        {
            return;
        }

        try
        {
            IsSecondRow = second;
            RetargetTiles();

            var device = Device;

            if (device is null || !IsSnapshotOf(device))
            {
                // Rien de lu pour cette enceinte : les tuiles restent neutres, sans
                // le logo de l'autre rangée, le temps d'une vraie lecture.
                foreach (var tile in Presets)
                {
                    ClearArtOfAnotherSlot(tile);
                }

                await RefreshAsync().ConfigureAwait(true);
                return;
            }

            // Les six tuiles sont garnies avant la moindre attente : chaque appel
            // pose le texte tout de suite et ne rend la main que si un logo est à
            // télécharger. Aucune tuile n'attend donc le téléchargement d'une autre.
            var pending = new List<Task>(Presets.Count);

            foreach (var tile in Presets)
            {
                pending.Add(ApplyAsync(tile, _lastHardware.GetValueOrDefault(tile.Slot), _lastStore.GetValueOrDefault(tile.Slot)));
            }

            await Task.WhenAll(pending).ConfigureAwait(true);

            SaveArtCache();

            if (_lastSync is { } last)
            {
                SyncActive(last.Source, last.Station, last.Location, last.IsIdle);
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>
    /// Après un enregistrement : montre la rangée de la touche concernée, puis
    /// relit la grille pour afficher ce que l'enceinte a réellement retenu.
    /// </summary>
    public async Task RevealSlotAsync(int slot)
    {
        await SelectRowAsync(PresetKeys.InSecondRow(slot)).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Renumérote les six tuiles pour la rangée affichée et les remet à l'état
    /// neutre, d'un seul geste et sans attente.
    ///
    /// Une tuile ne doit jamais garder le nom d'une rangée avec l'emplacement de
    /// l'autre, même un instant : un appui lancerait alors une autre station que
    /// celle affichée. Neutre veut dire vide, donc inerte, jusqu'à ce que le
    /// contenu de la nouvelle rangée y soit posé.
    ///
    /// L'image, elle, n'est pas touchée ici. Elle appartient désormais à un autre
    /// emplacement (ArtSlot ≠ Slot) et sera remplacée ou retirée par SetArtAsync,
    /// en une seule fois : la retirer ici pour la reposer dans la foulée faisait
    /// disparaître le logo des touches garnies dans les deux rangées.
    /// </summary>
    private void RetargetTiles()
    {
        var offset = IsSecondRow ? PresetKeys.PerRow : 0;

        // La mise en avant appartient à un emplacement, pas à une tuile.
        _optimisticUntil = DateTime.MinValue;

        for (var index = 0; index < Presets.Count; index++)
        {
            var tile = Presets[index];

            tile.Slot = offset + index + 1;
            tile.IsEmpty = true;
            tile.IsActive = false;
            tile.Name = Localization.Get("L_PresetNumber", PresetKeys.Number(tile.Slot));
            tile.Detail = Localization.Get("L_PresetEmpty");
            tile.Glyph = SlotNumber(tile.Slot);
            tile.Kind = "";
            tile.SourceKey = "";
        }
    }

    /// <summary>
    /// Retient si l'enceinte gère la seconde rangée. Quand elle ne la gère pas
    /// alors que la grille la montrait — autre enceinte, agent plus ancien — on
    /// revient à la première : ses touches n'existent pas là-bas.
    /// </summary>
    private void ApplyRowSupport(bool supported)
    {
        HasSecondRow = supported;

        if (!supported && IsSecondRow)
        {
            IsSecondRow = false;
            RetargetTiles();
        }
    }

    /// <summary>
    /// L'agent annonce la seconde rangée par « presetRows » dans sa version. Un
    /// magasin qui porte déjà un emplacement au-delà du sixième le prouve aussi.
    /// </summary>
    private async Task<bool> DetectSecondRowAsync(SpeakerDevice device, Dictionary<int, StrPreset> store, bool storeRead)
    {
        if (device.Str is null)
        {
            return false;
        }

        if (store.Keys.Any(PresetKeys.InSecondRow) || device.Agent?.HasSecondPresetRow == true)
        {
            _secondRowHost = device.Host;
            return true;
        }

        // La version a été lue à la connexion ; l'agent a pu être mis à jour depuis.
        // On ne repose la question que s'il vient de répondre : inutile d'attendre
        // un agent muet une seconde fois.
        if (storeRead)
        {
            try
            {
                var info = await device.Str.GetAgentInfoAsync().ConfigureAwait(true);

                if (info is not null)
                {
                    if (info.HasSecondPresetRow)
                    {
                        _secondRowHost = device.Host;
                    }
                    else if (SameHost(_secondRowHost, device.Host))
                    {
                        _secondRowHost = null;
                    }

                    return info.HasSecondPresetRow;
                }
            }
            catch (Exception)
            {
                // Lecture ratée : on s'en tient à ce que l'on savait de cette enceinte.
            }
        }

        return SameHost(_secondRowHost, device.Host);
    }

    private bool IsSnapshotOf(SpeakerDevice device) => SameHost(_snapshotHost, device.Host);

    private static bool SameHost(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    // ================================================================ Choix d'une touche

    /// <summary>
    /// Les touches proposées quand il faut en choisir une pour y ranger quelque
    /// chose : six, ou douze quand la seconde rangée existe. Chaque libellé dit ce
    /// que la touche porte déjà, y compris pour la rangée qui n'est pas à l'écran.
    /// </summary>
    private List<(int Slot, string Label)> SlotChoices()
    {
        var count = HasSecondRow ? PresetKeys.Total : PresetKeys.PerRow;
        var choices = new List<(int Slot, string Label)>(count);

        for (var slot = 1; slot <= count; slot++)
        {
            choices.Add((slot, SlotActionLabel(slot, OccupantOf(slot))));
        }

        return choices;
    }

    /// <summary>Nom de ce que porte un emplacement, null s'il est libre.</summary>
    private string? OccupantOf(int slot)
    {
        // La tuile affichée fait foi pour sa rangée : c'est ce que l'utilisateur voit.
        var tile = Presets.FirstOrDefault(t => t.Slot == slot);

        if (tile is not null)
        {
            return tile.IsEmpty ? null : tile.Name;
        }

        if (_lastStore.TryGetValue(slot, out var stored))
        {
            return stored.DisplayName;
        }

        return _lastHardware.TryGetValue(slot, out var hardware) && !hardware.IsEmpty ? hardware.Name : null;
    }

    /// <summary>
    /// Demande sur quelle touche ranger quelque chose. Renvoie l'emplacement, 1 à
    /// 12, ou null si l'utilisateur a renoncé.
    /// </summary>
    public async Task<int?> PickSlotAsync(string title)
    {
        var device = Device;

        if (device is not null && !IsSnapshotOf(device))
        {
            // Les libellés disent ce que chaque touche porte : autant qu'ils soient
            // ceux de cette enceinte.
            await RefreshAsync().ConfigureAwait(true);
        }

        var choices = SlotChoices();
        var cancel = Localization.Get("L_Cancel");

        var choice = await Shell.Current.DisplayActionSheetAsync(
            title,
            cancel,
            null,
            choices.Select(c => c.Label).ToArray()).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(choice) || choice == cancel)
        {
            return null;
        }

        foreach (var candidate in choices)
        {
            if (candidate.Label == choice)
            {
                return candidate.Slot;
            }
        }

        return null;
    }

    /// <summary>
    /// Logo d'une station rangée sur une touche qui n'est pas à l'écran. L'onglet
    /// Lecture s'en sert quand ce qui joue vient de l'autre rangée.
    /// </summary>
    public string? ArtOfStoredStation(string? stationName)
    {
        var device = Device;

        if (string.IsNullOrWhiteSpace(stationName) || device is null || !IsSnapshotOf(device))
        {
            return null;
        }

        foreach (var (slot, stored) in _lastStore)
        {
            if (!string.Equals(stored.DisplayName, stationName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var art = JoinCandidates(_artCache.GetValueOrDefault(slot), stored.Art);

            if (art is not null)
            {
                return art;
            }
        }

        return null;
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
    /// touches — six, ou douze avec la seconde rangée. La feuille d'actions est la façon native de poser ce choix
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

        if (!IsSnapshotOf(device))
        {
            // Les libellés ci-dessous décrivent les touches de CETTE enceinte.
            await RefreshAsync().ConfigureAwait(true);
        }

        // Chaque entrée dit ce que la touche contient déjà : « ① Remplacer Bel RTL »
        // est plus parlant que « Affecter à la touche 1 », et évite d'écraser une
        // station par erreur.
        var choices = SlotChoices();
        var slots = choices.Select(c => c.Label).ToArray();

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

        var slot = choices[index].Slot;
        var saved = Localization.Get("S_StationSaved", result.Name, PresetKeys.Label(slot));

        await RunAsync(d => d.SaveStationAsync(result.Station, slot), saved).ConfigureAwait(true);

        if (IsError)
        {
            // Refusé — la station est déjà sur une autre touche, par exemple. Rien
            // n'a changé, et relire la grille effacerait la raison du refus.
            return;
        }

        await RevealSlotAsync(slot).ConfigureAwait(true);

        if (!IsError)
        {
            // La relecture remet le message à blanc : on redit ce qui a été fait.
            ShowInfo(saved);
        }
    }

    /// <summary>
    /// Libellé d'une touche dans la feuille d'actions : le numéro cerclé, puis
    /// « Remplacer la station » quand la touche est occupée, sinon l'invitation à
    /// l'affecter.
    /// </summary>
    private static string SlotActionLabel(int slot, string? occupant)
    {
        // « ①×2 » pour la seconde rangée : la même touche, pressée deux fois.
        var number = PresetKeys.InSecondRow(slot) ? SlotNumber(slot) + "×2" : SlotNumber(slot);

        return occupant is null
            ? $"{number}  {Localization.Get("L_AssignEmpty")}"
            : $"{number}  {Localization.Get("L_ReplacePreset", occupant)}";
    }

    /// <summary>
    /// Chiffres cerclés ①..⑥ — des caractères de texte, pas des émojis. Toujours le
    /// numéro de la touche physique : l'emplacement 8 s'affiche ②.
    /// </summary>
    private static string SlotNumber(int slot) => PresetKeys.Number(slot) switch
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

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

    /// <summary>« Touche 2 » : le nom de la touche physique, au-dessus de la station, dans les deux rangées.</summary>
    [ObservableProperty]
    private string _header = "";

    /// <summary>
    /// Vrai quand la touche peut être renommée ou effacée : elle est garnie dans le
    /// magasin de l'agent, et l'agent est à la version minimale (1.8.0).
    /// </summary>
    [ObservableProperty]
    private bool _canEdit;

    partial void OnSlotChanged(int value) => RefreshHeader();

    /// <summary>Recalcule « Touche n » : au changement d'emplacement ou de langue.</summary>
    public void RefreshHeader() => Header = Localization.Get("L_KeyNumber", PresetKeys.Number(Slot));

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

    /// <summary>Remplace le résumé de l'annuaire : « Adresse collée » pour une adresse hors annuaire.</summary>
    public string? Note { get; init; }

    public string Summary => Note ?? Station.Summary;

    /// <summary>Signalée hors service au dernier contrôle de l'annuaire (visible sans « En service uniquement »).</summary>
    public bool IsUnreachable => Station.IsCheckedBroken;

    /// <summary>Le logo a déjà été demandé : un changement de filtre ne le retélécharge pas.</summary>
    public bool LogoTried { get; set; }

    [ObservableProperty]
    private ImageSource? _logo;

    [ObservableProperty]
    private bool _hasLogo;
}

/// <summary>Une pastille de genre de la recherche, ou la pastille « Plus de genres ».</summary>
public sealed partial class GenreChip : ObservableObject
{
    /// <summary>Étiquette radio-browser ; null pour « Tous ».</summary>
    public string? Key { get; init; }

    public string Label { get; init; } = "";

    /// <summary>La pastille qui ouvre ou referme la longue traîne des genres.</summary>
    public bool IsMore { get; init; }

    [ObservableProperty]
    private bool _isSelected;
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

    /// <summary>
    /// Les présélections « Local », gardées dans le téléphone : leur propre
    /// vue-modèle, partagée avec l'onglet Lecture.
    /// </summary>
    public LocalPresetsViewModel Local { get; }

    public PresetsViewModel(SpeakerManager speakers, ArtworkCache artwork, RadioBrowserClient radio) : base(speakers)
    {
        _artwork = artwork;
        _radio = radio;
        _artCache = LoadArtCache();
        Copy = new PresetCopyViewModel(speakers);
        Local = new LocalPresetsViewModel(speakers, artwork);

        // Le sélecteur « Une fois / Deux fois / Local » suit la liste locale.
        Local.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocalPresetsViewModel.IsShown))
            {
                OnPropertyChanged(nameof(OnceSelected));
                OnPropertyChanged(nameof(TwiceSelected));
                OnPropertyChanged(nameof(ShowKeys));
            }
        };

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

            foreach (var tile in Presets)
            {
                tile.RefreshHeader();
            }
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
    [NotifyPropertyChangedFor(nameof(ShowSecondRowCount))]
    private bool _hasSecondRow;

    /// <summary>Nombre de touches garnies dans la seconde rangée (7 à 12), pour la pastille de « Deux fois ».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSecondRowCount))]
    private int _secondRowCount;

    /// <summary>La pastille n'apparaît qu'avec la seconde rangée et au moins une touche garnie.</summary>
    public bool ShowSecondRowCount => HasSecondRow && SecondRowCount > 0;

    /// <summary>Nombre de touches garnies dans la première rangée (1 à 6), pour la pastille de « Une fois ».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFirstRowCount))]
    private int _firstRowCount;

    /// <summary>La pastille de « Une fois » n'apparaît qu'avec au moins une touche garnie.</summary>
    public bool ShowFirstRowCount => FirstRowCount > 0;

    /// <summary>Vrai quand la grille montre la seconde rangée, emplacements 7 à 12.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstRow), nameof(OnceSelected), nameof(TwiceSelected))]
    private bool _isSecondRow;

    public bool IsFirstRow => !IsSecondRow;

    /// <summary>Le sélecteur « Une fois » est en avant : première rangée, liste locale fermée.</summary>
    public bool OnceSelected => !IsSecondRow && !Local.IsShown;

    /// <summary>Le sélecteur « Deux fois » est en avant : seconde rangée, liste locale fermée.</summary>
    public bool TwiceSelected => IsSecondRow && !Local.IsShown;

    /// <summary>La grille des touches de l'enceinte est montrée : la liste locale ne l'a pas remplacée.</summary>
    public bool ShowKeys => !Local.IsShown;

    /// <summary>Le sélecteur « Local » : la liste des présélections du téléphone remplace la grille.</summary>
    [RelayCommand]
    private void ShowLocal() => Local.IsShown = true;

    // ================================================================ Les six touches

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var device = Device;

        if (device is null)
        {
            // Sans enceinte, pas de seconde rangée : on revient aux six touches, et
            // la pastille de « Une fois » ne compte plus rien.
            ApplyRowSupport(false);
            await ResetAsync().ConfigureAwait(true);
            FirstRowCount = 0;
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
            UpdateSecondRowCount();

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

    // ================================================================ Renommer, effacer (agent 1.8.0)

    /// <summary>
    /// Renomme une touche du magasin de l'agent. Seul le nom change : la station,
    /// son image et la playlist d'une touche Spotify restent. Annuler, garder le
    /// même nom ou ne rien taper n'envoie rien.
    /// </summary>
    [RelayCommand]
    private async Task RenameKeyAsync(PresetTile? tile)
    {
        if (tile is null || !tile.CanEdit || Device is not { CanEditPresets: true, Str: { } str } || IsBusy)
        {
            return;
        }

        var slot = tile.Slot;
        var current = tile.Name;

        var typed = await Shell.Current.DisplayPromptAsync(
            Localization.Get("L_KeyRenameTitle", PresetKeys.Label(slot)),
            Localization.Get("L_KeyRenameMessage"),
            Localization.Get("L_KeyRenameGo"),
            Localization.Get("L_Cancel"),
            maxLength: StrApiClient.MaxPresetNameLength,
            initialValue: current).ConfigureAwait(true);

        var name = (typed ?? "").Trim();

        if (name.Length == 0 || string.Equals(name, current, StringComparison.Ordinal))
        {
            return;
        }

        await EditKeyAsync(
            slot,
            () => str.RenamePresetAsync(slot, name),
            Localization.Get("S_KeyRenamed", PresetKeys.Label(slot))).ConfigureAwait(true);
    }

    /// <summary>Efface une touche du magasin de l'agent, après confirmation.</summary>
    [RelayCommand]
    private async Task ClearKeyAsync(PresetTile? tile)
    {
        if (tile is null || !tile.CanEdit || Device is not { CanEditPresets: true, Str: { } str } || IsBusy)
        {
            return;
        }

        var slot = tile.Slot;
        var go = Localization.Get("L_KeyClearGo");

        var answer = await Shell.Current.DisplayActionSheetAsync(
            Localization.Get("L_KeyClearConfirm", PresetKeys.Label(slot), tile.Name),
            Localization.Get("L_Cancel"),
            null,
            go).ConfigureAwait(true);

        if (answer != go)
        {
            return;
        }

        await EditKeyAsync(
            slot,
            () => str.DeletePresetAsync(slot),
            Localization.Get("S_KeyCleared", PresetKeys.Label(slot))).ConfigureAwait(true);
    }

    /// <summary>
    /// Envoie la modification, puis relit les touches : la grille, la pastille et
    /// l'onglet Lecture montrent ce que l'agent a réellement retenu.
    /// </summary>
    private async Task EditKeyAsync(int slot, Func<Task> send, string done)
    {
        IsBusy = true;
        string? message = null;
        var failed = false;

        try
        {
            await send().ConfigureAwait(true);
            message = done;
        }
        catch (SpeakerException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // La touche a été vidée ailleurs entre-temps (autre téléphone, appli PC).
            failed = true;
            message = Localization.Get("S_KeyGone", PresetKeys.Label(slot));
        }
        catch (Exception ex)
        {
            IsBusy = false;
            ShowError(ex);
            return;
        }

        IsBusy = false;
        await RefreshAsync().ConfigureAwait(true);

        IsError = failed;
        StatusMessage = message;
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
        Local.SyncActive(stationName, isIdle);

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
        // On concatène les sources dans l'ordre de préférence et ArtworkCache
        // essaiera chaque adresse jusqu'à en trouver une qui donne une image.
        //
        // Le logo retenu la dernière fois pour cet emplacement ne sert que quand ni
        // l'agent ni l'enceinte n'en proposent. Ajouté en fin de chaîne, il prenait
        // la relève dès que le logo de la station ne s'affichait pas, alors qu'il
        // peut dater d'une station rangée là auparavant : la touche montrait le
        // logo d'une autre station.
        var art = JoinCandidates(store?.Art, hardware?.ArtUrl) ?? _artCache.GetValueOrDefault(tile.Slot);

        if (store is not null)
        {
            tile.Name = store.DisplayName;
            tile.IsEmpty = false;
            tile.CanEdit = Device?.CanEditPresets == true;

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
            // Une touche que seul le firmware connaît n'est pas dans le magasin de
            // l'agent : il ne saurait ni la renommer ni l'effacer.
            tile.CanEdit = false;
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
        tile.CanEdit = false;
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

    /// <summary>La pastille de « Deux fois » : touches 7 à 12 garnies dans le dernier magasin lu.</summary>
    private void UpdateSecondRowCount()
    {
        SecondRowCount = HasSecondRow ? _lastStore.Keys.Count(PresetKeys.InSecondRow) : 0;

        // Première rangée : une touche compte dès que l'agent ou le firmware y range
        // une station. Les présélections factices « 2x » ont déjà été retirées de la
        // liste du firmware à la lecture.
        FirstRowCount = Enumerable.Range(1, PresetKeys.PerRow).Count(slot =>
            _lastStore.ContainsKey(slot) ||
            (_lastHardware.TryGetValue(slot, out var hardware) && !hardware.IsEmpty));
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

        // « Une fois » ou « Deux fois » referme la liste locale, même sur la
        // rangée déjà affichée : c'est le seul moyen de revenir aux touches.
        Local.IsShown = false;

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
            tile.CanEdit = false;
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
        UpdateSecondRowCount();

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
    //
    // Reprise de la recherche de l'application PC de STR : pastilles de genres,
    // tri, débit minimal, « en service uniquement », « Voir plus », et l'invitation
    // à ajouter soi-même une station qui manque à l'annuaire.
    //
    // Deux sortes de filtres :
    //   – ceux qui changent la question posée à l'annuaire (pays, langue, genre,
    //     tri, en service) relancent la dernière recherche ;
    //   – ceux qui ne regardent que la réponse (compatibles Bose, débit) filtrent
    //     la liste déjà reçue, sans réseau : décocher « Bose » ramène aussitôt les
    //     stations écartées.

    /// <summary>Ce que la dernière recherche a demandé.</summary>
    private enum SearchMode
    {
        None,

        /// <summary>Recherche par nom (le champ de saisie).</summary>
        Name,

        /// <summary>Top liste : les plus votées, avec les filtres.</summary>
        Top,

        /// <summary>Une adresse de flux collée dans le champ.</summary>
        Url,
    }

    /// <summary>Taille d'une page de résultats.</summary>
    private const int SearchPage = 60;

    /// <summary>Nombre de genres demandés à l'annuaire pour « Plus de genres ».</summary>
    private const int GenreTagsLimit = 60;

    /// <summary>Plafond de la liste affichée : « Voir plus » s'arrête là.</summary>
    private const int MaxResults = 600;

    /// <summary>Ordres de tri, dans l'ordre de la liste déroulante.</summary>
    private static readonly string[] SortValues =
    {
        RadioOrder.Votes, RadioOrder.Clicks, RadioOrder.Trend, RadioOrder.Name, RadioOrder.Bitrate,
    };

    /// <summary>Débits minimaux proposés, en kbit/s ; 0 = tous.</summary>
    private static readonly int[] BitrateValues = { 0, 64, 128, 192, 256, 320 };

    /// <summary>Libellés des genres de la rangée fixe ; les autres s'affichent tels quels, capitalisés.</summary>
    private static readonly Dictionary<string, string> GenreKeys = new(StringComparer.Ordinal)
    {
        ["pop"] = "L_GenrePop",
        ["rock"] = "L_GenreRock",
        ["hits"] = "L_GenreHits",
        ["oldies"] = "L_GenreOldies",
        ["jazz"] = "L_GenreJazz",
        ["classical"] = "L_GenreClassical",
        ["chillout"] = "L_GenreChill",
        ["dance"] = "L_GenreDance",
        ["electronic"] = "L_GenreElectronic",
        ["house"] = "L_GenreHouse",
        ["hip hop"] = "L_GenreHipHop",
        ["latin"] = "L_GenreLatin",
        ["80s"] = "L_Genre80s",
        ["90s"] = "L_Genre90s",
        ["news"] = "L_GenreNews",
    };

    public ObservableCollection<RadioResult> Results { get; } = new();

    public ObservableCollection<string> CountryLabels { get; } = new();

    public ObservableCollection<string> LanguageLabels { get; } = new();

    public ObservableCollection<string> SortLabels { get; } = new();

    public ObservableCollection<string> BitrateLabels { get; } = new();

    /// <summary>Les pastilles de genres, « Tous » en tête et « Plus de genres » au bout.</summary>
    public ObservableCollection<GenreChip> Genres { get; } = new();

    private readonly List<FilterOption> _countries = new();
    private readonly List<FilterOption> _languages = new();

    /// <summary>Tout ce que l'annuaire a donné pour la recherche en cours, avant les filtres Bose et débit.</summary>
    private readonly List<RadioResult> _found = new();

    private SearchMode _mode;

    /// <summary>Nom cherché par la liste affichée, pour « Voir plus ».</summary>
    private string _lastName = "";

    /// <summary>Position de la page suivante dans l'annuaire, pour « Voir plus ».</summary>
    private int _nextOffset;

    /// <summary>Genre choisi (étiquette radio-browser) ; null = tous.</summary>
    private string? _genre;

    private bool _showMoreGenres;

    /// <summary>Genres de l'annuaire, lus une fois ; null tant qu'ils ne sont pas arrivés.</summary>
    private IReadOnlyList<RadioTag>? _liveTags;

    private bool _tagsRequested;

    /// <summary>Vrai pendant la reconstruction des listes : les sélecteurs bougent sans que l'utilisateur y touche.</summary>
    private bool _buildingFilters;

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private int _countryIndex;

    [ObservableProperty]
    private int _languageIndex;

    [ObservableProperty]
    private int _sortIndex;

    [ObservableProperty]
    private int _bitrateIndex;

    [ObservableProperty]
    private bool _boseCompatibleOnly = true;

    /// <summary>« En service uniquement » : écarte les stations trouvées hors service par l'annuaire.</summary>
    [ObservableProperty]
    private bool _onlyWorking = true;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasSearched;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _resultsHeader = "";

    /// <summary>« 12 stations masquées par les filtres » ; vide quand rien n'est masqué.</summary>
    [ObservableProperty]
    private string _filteredNote = "";

    /// <summary>L'annuaire a d'autres stations : « Voir plus » est proposé.</summary>
    [ObservableProperty]
    private bool _canLoadMore;

    /// <summary>La liste montre des stations signalées hors service, faute d'autres.</summary>
    [ObservableProperty]
    private bool _isRelaxed;

    [RelayCommand]
    private Task SearchAsync() => RunSearchAsync(SearchMode.Name);

    [RelayCommand]
    private Task TopAsync() => RunSearchAsync(SearchMode.Top);

    /// <summary>« Voir plus » : la page suivante de la même recherche, ajoutée au bout.</summary>
    [RelayCommand]
    private Task LoadMoreAsync() => CanLoadMore && !IsSearching ? RunSearchAsync(_mode, append: true) : Task.CompletedTask;

    /// <summary>Une pastille de genre : la choisit, ou ouvre / referme « Plus de genres ».</summary>
    [RelayCommand]
    private Task SelectGenreAsync(GenreChip? chip)
    {
        if (chip is null)
        {
            return Task.CompletedTask;
        }

        if (chip.IsMore)
        {
            _showMoreGenres = !_showMoreGenres;
            BuildGenres();
            return Task.CompletedTask;
        }

        _genre = chip.Key;

        foreach (var g in Genres)
        {
            g.IsSelected = !g.IsMore && g.Key == _genre;
        }

        // Le genre précise la recherche par nom en cours ; sans nom, il parcourt
        // l'annuaire, comme la Top liste.
        return RunSearchAsync(_mode == SearchMode.Name && !string.IsNullOrWhiteSpace(Query) ? SearchMode.Name : SearchMode.Top);
    }

    /// <summary>
    /// « Une station manque ? » : ouvre radio-browser.info, où chacun peut ajouter
    /// une station. Elle apparaît ensuite ici, et dans toutes les applications qui
    /// utilisent l'annuaire.
    /// </summary>
    [RelayCommand]
    private async Task AddMissingStationAsync()
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri("https://www.radio-browser.info/")).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>
    /// Lit une fois les genres de l'annuaire, pour « Plus de genres ». Appelé à
    /// l'ouverture de l'onglet ; un échec est sans conséquence (les genres fixes
    /// restent) et sera retenté à la prochaine ouverture.
    /// </summary>
    public async Task LoadGenresAsync()
    {
        if (_tagsRequested)
        {
            return;
        }

        _tagsRequested = true;

        try
        {
            _liveTags = await _radio.TagsAsync(GenreTagsLimit).ConfigureAwait(true);
            BuildGenres();
        }
        catch (Exception)
        {
            _tagsRequested = false;
        }
    }

    // Les filtres qui changent la question relancent la dernière recherche ; les
    // valeurs négatives viennent d'un sélecteur dont la liste est en reconstruction.
    partial void OnCountryIndexChanged(int value)
    {
        if (value < 0 || _buildingFilters)
        {
            return;
        }

        // Les deux genres mis en avant dépendent du pays.
        _showMoreGenres = false;
        BuildGenres();
        Rerun();
    }

    partial void OnLanguageIndexChanged(int value)
    {
        if (value >= 0)
        {
            Rerun();
        }
    }

    partial void OnSortIndexChanged(int value)
    {
        if (value >= 0)
        {
            Rerun();
        }
    }

    partial void OnOnlyWorkingChanged(bool value) => Rerun();

    // Ceux qui ne regardent que la réponse refiltrent la liste reçue.
    partial void OnBoseCompatibleOnlyChanged(bool value) => RefilterLocally();

    partial void OnBitrateIndexChanged(int value)
    {
        if (value >= 0)
        {
            RefilterLocally();
        }
    }

    /// <summary>Relance la dernière recherche avec les nouveaux filtres, s'il y en a eu une.</summary>
    private void Rerun()
    {
        if (_buildingFilters || _mode is SearchMode.None or SearchMode.Url)
        {
            return;
        }

        _ = RunSearchAsync(_mode);
    }

    private void RefilterLocally()
    {
        if (_buildingFilters || !HasSearched)
        {
            return;
        }

        ApplyLocalFilters();
        _ = LoadLogosAsync(_searchCts?.Token ?? CancellationToken.None);
    }

    private async Task RunSearchAsync(SearchMode mode, bool append = false)
    {
        // Une frappe rapide sur « Rechercher » annule la requête précédente plutôt
        // que d'empiler deux listes de résultats. L'ancienne source n'est pas
        // libérée : la requête qu'elle annule peut encore lire son jeton.
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var ct = cts.Token;

        // « Voir plus » continue la recherche affichée, même si le champ a changé depuis.
        var name = append ? _lastName : Query?.Trim() ?? "";

        if (!append)
        {
            // Champ vide : la recherche devient une Top liste filtrée. Une adresse
            // de flux n'est pas un nom : elle est cherchée telle quelle.
            mode = mode == SearchMode.Name && name.Length == 0 ? SearchMode.Top
                : mode == SearchMode.Name && IsPastedUrl(name) ? SearchMode.Url
                : mode;
        }

        IsSearching = true;
        ShowInfo(Localization.Get("S_Searching"));

        try
        {
            if (mode == SearchMode.Url)
            {
                await SearchByUrlAsync(name, ct).ConfigureAwait(true);
                return;
            }

            var page = await _radio.SearchAsync(new RadioSearchOptions
            {
                Name = mode == SearchMode.Name ? name : null,
                Tag = _genre,
                CountryCode = _countries.ElementAtOrDefault(CountryIndex)?.Value,
                Language = _languages.ElementAtOrDefault(LanguageIndex)?.Value,
                Order = SortValues.ElementAtOrDefault(SortIndex) ?? RadioOrder.Votes,
                Limit = SearchPage,
                Offset = append ? _nextOffset : 0,
                HideBroken = OnlyWorking,

                // Filtré ici, sur la liste reçue : cocher ou décocher ne relance rien.
                BoseCompatibleOnly = false,
            }, ct).ConfigureAwait(true);

            ct.ThrowIfCancellationRequested();

            if (!append)
            {
                _found.Clear();
                IsRelaxed = false;
            }

            _mode = mode;
            _lastName = name;
            AddFound(page.Stations.Select(s => new RadioResult { Station = s }));
            _nextOffset = page.NextOffset;
            CanLoadMore = page.HasMore && _found.Count < MaxResults;
            IsRelaxed = IsRelaxed || page.Relaxed;
            HasSearched = true;

            ApplyLocalFilters();

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
            if (!ct.IsCancellationRequested)
            {
                ShowError(ex);
            }
        }
        finally
        {
            // Une recherche remplacée ne doit pas éteindre l'indicateur de la suivante.
            if (ReferenceEquals(_searchCts, cts))
            {
                IsSearching = false;
            }
        }
    }

    /// <summary>
    /// Une adresse collée : la station de l'annuaire qui a exactement ce flux (avec
    /// son nom et son logo), sinon une ligne « adresse collée » qu'on peut écouter,
    /// garder dans Local ou mettre sur une touche comme les autres.
    /// </summary>
    private async Task SearchByUrlAsync(string url, CancellationToken ct)
    {
        IReadOnlyList<RadioStation> known;

        try
        {
            known = await _radio.ByUrlAsync(url, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Annuaire injoignable : l'adresse reste utilisable telle quelle.
            known = Array.Empty<RadioStation>();
        }

        ct.ThrowIfCancellationRequested();

        _found.Clear();
        _mode = SearchMode.Url;
        IsRelaxed = false;
        CanLoadMore = false;

        if (known.Count > 0)
        {
            AddFound(known.Select(s => new RadioResult { Station = s }));
        }
        else
        {
            var uri = new Uri(url);

            AddFound(new[]
            {
                new RadioResult
                {
                    Station = new RadioStation { Name = uri.Host, Url = url },
                    Note = Localization.Get("L_PastedAddress"),
                },
            });
        }

        HasSearched = true;
        ApplyLocalFilters(ignoreFilters: true);
        await LoadLogosAsync(ct).ConfigureAwait(true);
    }

    /// <summary>Une adresse http(s) complète, de longueur raisonnable : pas un nom de station.</summary>
    private static bool IsPastedUrl(string text)
        => text.Length <= LocalPresetList.MaxUrlLength &&
           (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
           LocalPresetList.IsStreamUrl(text);

    /// <summary>Ajoute des résultats en écartant un flux déjà dans la liste (pages qui se chevauchent).</summary>
    private void AddFound(IEnumerable<RadioResult> results)
    {
        var known = new HashSet<string>(_found.Select(r => r.Station.PlayableUrl.Trim()), StringComparer.OrdinalIgnoreCase);

        foreach (var result in results)
        {
            if (_found.Count >= MaxResults)
            {
                break;
            }

            if (known.Add(result.Station.PlayableUrl.Trim()))
            {
                _found.Add(result);
            }
        }
    }

    /// <summary>
    /// Construit la liste affichée à partir de ce que l'annuaire a donné : filtres
    /// « Compatibles Bose » et débit minimal, compte des stations masquées.
    /// </summary>
    /// <param name="ignoreFilters">Une adresse collée s'affiche toujours : c'est l'utilisateur qui l'a choisie.</param>
    private void ApplyLocalFilters(bool ignoreFilters = false)
    {
        ignoreFilters |= _mode == SearchMode.Url;
        var minBitrate = BitrateValues.ElementAtOrDefault(BitrateIndex);

        // Une station sans débit connu reste : c'est le cas de beaucoup d'entrées de
        // l'annuaire, et le filtre ne doit pas vider la liste pour autant.
        var shown = _found
            .Where(r => ignoreFilters || !BoseCompatibleOnly || r.Station.IsBoseCompatible)
            .Where(r => ignoreFilters || minBitrate <= 0 || r.Station.Bitrate <= 0 || r.Station.Bitrate >= minBitrate)
            .ToList();

        Results.Clear();

        foreach (var result in shown)
        {
            Results.Add(result);
        }

        var hidden = _found.Count - shown.Count;

        HasResults = Results.Count > 0;
        ResultsHeader = Localization.Get("L_ResultsHeader", Results.Count);
        FilteredNote = hidden > 0 ? Localization.Get("L_ResultsFiltered", hidden) : "";

        ShowInfo(Results.Count > 0 ? null
            : _found.Count > 0 ? Localization.Get("S_AllFiltered")
            : Localization.Get("L_NoResults"));
    }

    private async Task LoadLogosAsync(CancellationToken ct)
    {
        foreach (var result in Results.ToList())
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // Une ligne déjà essayée (avant un changement de filtre) ne se retélécharge pas.
            if (result.LogoTried)
            {
                continue;
            }

            result.LogoTried = true;

            var source = await _artwork.GetImageSourceAsync(result.Station.Favicon, ct).ConfigureAwait(true);

            if (source is not null)
            {
                result.Logo = source;
                result.HasLogo = true;
            }
        }
    }

    /// <summary>Reconstruit les pastilles de genres : pays, genres de l'annuaire, choix en cours.</summary>
    private void BuildGenres()
    {
        var country = _countries.ElementAtOrDefault(CountryIndex)?.Value;
        var chips = RadioGenres.Chips(country, _liveTags, _showMoreGenres, _genre, out var tail);

        Genres.Clear();

        foreach (var chip in chips)
        {
            Genres.Add(new GenreChip { Key = chip.Key, Label = GenreLabel(chip.Key), IsSelected = chip.Key == _genre });
        }

        if (tail > 0 || _showMoreGenres)
        {
            Genres.Add(new GenreChip
            {
                IsMore = true,
                Label = _showMoreGenres ? Localization.Get("L_FewerGenres") : Localization.Get("L_MoreGenres", tail),
            });
        }
    }

    /// <summary>Libellé d'une pastille : traduit pour la rangée fixe, sinon l'étiquette capitalisée.</summary>
    public static string GenreLabel(string? key)
    {
        if (key is null)
        {
            return Localization.Get("L_AllGenres");
        }

        if (GenreKeys.TryGetValue(key, out var locKey))
        {
            return Localization.Get(locKey);
        }

        if (key == "rnb")
        {
            return "R&B";
        }

        // « disco polo » → « Disco Polo », « j-pop » → « J-Pop ».
        var chars = key.ToCharArray();

        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 0 || chars[i - 1] is ' ' or '-')
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
            }
        }

        return new string(chars);
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
        var addLocal = Localization.Get("L_AddLocal");

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
            new[] { listen, addLocal }.Concat(slots).ToArray()).ConfigureAwait(true);

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

        if (choice == addLocal)
        {
            // La liste locale s'ouvre en haut de la page ; le message est redit ici,
            // près de la recherche, où l'on se trouve.
            Local.AddStation(result.Station);
            IsError = Local.IsError;
            StatusMessage = Local.StatusMessage;
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
    /// Construit les listes de pays, de langues, de tri et de débit, et les
    /// pastilles de genres. Les noms de langue viennent de .NET ; ceux des pays sont
    /// traduits à la main (voir les clés L_Ctry…). Appelée au démarrage et à chaque
    /// changement de langue : les choix en cours sont gardés, et rien n'est relancé.
    /// </summary>
    private void BuildFilters()
    {
        _buildingFilters = true;

        try
        {
            var previousCountry = _countries.ElementAtOrDefault(CountryIndex)?.Value;
            var previousLanguage = _languages.ElementAtOrDefault(LanguageIndex)?.Value;
            var previousSort = Math.Max(0, SortIndex);
            var previousBitrate = Math.Max(0, BitrateIndex);

            _countries.Clear();
            _countries.Add(new FilterOption { Label = Localization.Get("L_AllCountries"), Value = null });

            // Clés littérales plutôt qu'une concaténation : une clé manquante se voit
            // alors à la relecture, pas à l'exécution. Les pays proches d'abord, dans
            // l'ordre de toujours ; les autres ensuite, par ordre alphabétique.
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

            var more = new List<FilterOption>
            {
                new() { Label = Localization.Get("L_CtryAT"), Value = "AT" },
                new() { Label = Localization.Get("L_CtryIE"), Value = "IE" },
                new() { Label = Localization.Get("L_CtryDK"), Value = "DK" },
                new() { Label = Localization.Get("L_CtrySE"), Value = "SE" },
                new() { Label = Localization.Get("L_CtryNO"), Value = "NO" },
                new() { Label = Localization.Get("L_CtryFI"), Value = "FI" },
                new() { Label = Localization.Get("L_CtryPL"), Value = "PL" },
                new() { Label = Localization.Get("L_CtryCZ"), Value = "CZ" },
                new() { Label = Localization.Get("L_CtryGR"), Value = "GR" },
                new() { Label = Localization.Get("L_CtryTR"), Value = "TR" },
                new() { Label = Localization.Get("L_CtryMA"), Value = "MA" },
                new() { Label = Localization.Get("L_CtryBR"), Value = "BR" },
                new() { Label = Localization.Get("L_CtryMX"), Value = "MX" },
                new() { Label = Localization.Get("L_CtryAR"), Value = "AR" },
                new() { Label = Localization.Get("L_CtryAU"), Value = "AU" },
                new() { Label = Localization.Get("L_CtryJP"), Value = "JP" },
            };

            var culture = Localization.CultureOf(Localization.Current);
            _countries.AddRange(more.OrderBy(o => o.Label, StringComparer.Create(culture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace)));

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
                         ("pt", "portuguese"),
                     })
            {
                _languages.Add(new FilterOption { Label = LanguageName(tag), Value = value });
            }

            Refill(CountryLabels, _countries.Select(o => o.Label));
            Refill(LanguageLabels, _languages.Select(o => o.Label));

            Refill(SortLabels, new[]
            {
                Localization.Get("L_SortVotes"),
                Localization.Get("L_SortClicks"),
                Localization.Get("L_SortTrend"),
                Localization.Get("L_SortName"),
                Localization.Get("L_SortBitrate"),
            });

            Refill(BitrateLabels, BitrateValues.Select(v => v == 0
                ? Localization.Get("L_BitrateAll")
                : Localization.Get("L_BitrateMin", v)));

            CountryIndex = Math.Max(0, _countries.FindIndex(o => o.Value == previousCountry));
            LanguageIndex = Math.Max(0, _languages.FindIndex(o => o.Value == previousLanguage));
            SortIndex = Math.Min(previousSort, SortValues.Length - 1);
            BitrateIndex = Math.Min(previousBitrate, BitrateValues.Length - 1);

            BuildGenres();
        }
        finally
        {
            _buildingFilters = false;
        }
    }

    private static void Refill(ObservableCollection<string> target, IEnumerable<string> labels)
    {
        target.Clear();

        foreach (var label in labels)
        {
            target.Add(label);
        }
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

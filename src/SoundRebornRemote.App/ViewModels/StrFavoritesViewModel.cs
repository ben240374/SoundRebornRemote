using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>Un favori STR tel qu'affiché dans la liste.</summary>
public sealed partial class StrFavoriteItem : ObservableObject
{
    public required StrFavorite Favorite { get; init; }

    public string Name => Favorite.DisplayName;

    /// <summary>Pays, format et débit quand l'annuaire les donne ; sinon l'hôte du flux.</summary>
    public required string Detail { get; init; }

    public bool CanPlay => Favorite.CanPlay;

    [ObservableProperty]
    private ImageSource? _artSource;

    [ObservableProperty]
    private bool _hasArt;

    /// <summary>Vrai pour la station que l'enceinte joue en ce moment.</summary>
    [ObservableProperty]
    private bool _isActive;
}

/// <summary>
/// Les favoris STR : les stations étoilées de l'application STR sur ordinateur, que
/// l'agent garde sur chaque enceinte STR (agent récent). Ce sont les mêmes partout :
/// un favori ajouté ou retiré ici l'est aussi sur les autres enceintes et dans
/// l'application sur ordinateur, quelques secondes plus tard.
///
/// - La liste se lit sur l'enceinte sélectionnée ; un agent qui ne connaît pas les
///   favoris (404) cache le sélecteur « ★ STR ».
/// - Toucher un favori le lance sur l'enceinte sélectionnée, comme « Local ».
/// - « ⋯ » : le mettre sur une touche, le garder dans « Local », le retirer.
/// - On ajoute une station trouvée par la recherche, ou la radio qui joue.
/// - Jamais de PUT /api/favorites : c'est la synchronisation de l'application sur
///   ordinateur, qui défairait le changement. POST et DELETE seulement.
///
/// La liste prend la place de la grille des touches, comme « Local », quand on
/// choisit « ★ STR » à côté de « Une fois », « Deux fois » et « Local », dans les
/// onglets Lecture et Présélections. Les logos ne se chargent qu'à ce moment-là,
/// après ceux des touches.
/// </summary>
public sealed partial class StrFavoritesViewModel : BaseViewModel
{
    private readonly ArtworkCache _artwork;

    /// <summary>Six logos à la fois au plus : une longue liste ne sature pas le réseau.</summary>
    private readonly SemaphoreSlim _artGate = new(6);

    private int _loadVersion;
    private string? _host;
    private string? _playingStation;

    public StrFavoritesViewModel(SpeakerManager speakers, ArtworkCache artwork) : base(speakers)
    {
        _artwork = artwork;
        Speakers.DeviceChanged += (_, _) => OnMainThread(() => _ = LoadAsync());
    }

    public ObservableCollection<StrFavoriteItem> Items { get; } = new();

    /// <summary>L'agent de l'enceinte sélectionnée connaît les favoris : le sélecteur « ★ STR » se montre.</summary>
    [ObservableProperty]
    private bool _isAvailable;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private int _count;

    public bool IsEmpty => Count == 0;

    /// <summary>Vrai quand les onglets montrent cette liste à la place des touches de l'enceinte.</summary>
    [ObservableProperty]
    private bool _isShown;

    partial void OnIsShownChanged(bool value)
    {
        if (value)
        {
            foreach (var item in Items.Where(i => !i.HasArt))
            {
                _ = LoadArtAsync(item);
            }
        }
    }

    /// <summary>« Mettre sur une touche » : la feuille des touches de l'onglet Présélections.</summary>
    public Func<RadioStation, Task>? PickKey { get; set; }

    /// <summary>« Garder dans Local » : la liste du téléphone.</summary>
    public Func<RadioStation, bool>? KeepLocal { get; set; }

    // ================================================================ Lecture

    /// <summary>
    /// Relit les favoris sur l'enceinte sélectionnée. Une lecture dépassée par une
    /// autre (changement d'enceinte) est jetée. Un hoquet réseau sur la même
    /// enceinte garde la liste déjà montrée.
    /// </summary>
    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        var device = Device;
        var str = device?.Str;

        if (str is null)
        {
            Hide();
            return;
        }

        var sameHost = string.Equals(_host, device!.Host, StringComparison.OrdinalIgnoreCase);
        IsLoading = true;

        try
        {
            var favorites = await str.GetFavoritesAsync().ConfigureAwait(true);

            if (version != _loadVersion)
            {
                return;
            }

            _host = device.Host;
            Apply(favorites);
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            if (version != _loadVersion)
            {
                return;
            }

            if (ex is SpeakerException { StatusCode: HttpStatusCode.NotFound } || !sameHost || !IsAvailable)
            {
                // Agent sans favoris, ou autre enceinte jamais lue : pas de carte.
                Hide();
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    /// <summary>Textes traduits : à refaire quand la langue change.</summary>
    public void RebuildLabels()
    {
        StatusMessage = null;
        IsError = false;
    }

    private void Hide()
    {
        _host = null;

        // Plus de favoris sur cette enceinte : la grille des touches revient.
        IsShown = false;
        IsAvailable = false;
        Items.Clear();
        Count = 0;
    }

    private void Apply(IReadOnlyList<StrFavorite> favorites)
    {
        // Les logos déjà chargés restent : la liste se relit après chaque ajout.
        var art = Items.Where(i => i.HasArt)
            .GroupBy(i => i.Favorite.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ArtSource, StringComparer.Ordinal);

        Items.Clear();

        foreach (var favorite in favorites)
        {
            var item = new StrFavoriteItem { Favorite = favorite, Detail = DetailOf(favorite) };

            if (art.TryGetValue(favorite.Key, out var source) && source is not null)
            {
                item.ArtSource = source;
                item.HasArt = true;
            }

            Items.Add(item);
        }

        Count = Items.Count;
        MarkActive();

        if (IsShown)
        {
            foreach (var item in Items.Where(i => !i.HasArt))
            {
                _ = LoadArtAsync(item);
            }
        }
    }

    private static string DetailOf(StrFavorite favorite)
    {
        if (favorite.IsPandora)
        {
            return "Pandora";
        }

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(favorite.Country))
        {
            parts.Add(favorite.Country!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(favorite.Codec))
        {
            parts.Add(favorite.Bitrate is > 0
                ? $"{favorite.Codec!.Trim()} {favorite.Bitrate} kbps"
                : favorite.Codec!.Trim());
        }

        if (parts.Count == 0 && Uri.TryCreate(favorite.PlayableUrl, UriKind.Absolute, out var uri))
        {
            parts.Add(uri.Host);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Met en avant le favori que l'enceinte joue. Appelée avec chaque état reçu.</summary>
    public void SyncActive(string? stationName, bool isIdle)
    {
        _playingStation = isIdle ? null : stationName?.Trim();
        MarkActive();
    }

    private void MarkActive()
    {
        var playing = _playingStation;
        var match = string.IsNullOrWhiteSpace(playing)
            ? null
            : Items.FirstOrDefault(i => string.Equals(i.Name, playing, StringComparison.OrdinalIgnoreCase));

        foreach (var item in Items)
        {
            item.IsActive = ReferenceEquals(item, match);
        }
    }

    // ================================================================ Lancer, menu

    [RelayCommand]
    private async Task PlayAsync(StrFavoriteItem? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        if (!item.CanPlay)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_FavNotPlayable", item.Name);
            return;
        }

        var device = Device;

        if (device?.Str is null)
        {
            IsError = true;
            StatusMessage = Localization.Get(device is null ? "S_NoSpeaker" : "S_NeedAgentStation");
            return;
        }

        await RunAsync(d => d.PlayStationAsync(item.Favorite.ToStation()),
            Localization.Get("S_StationPlaying", item.Name)).ConfigureAwait(true);

        if (!IsError)
        {
            SyncActive(item.Name, false);
        }
    }

    /// <summary>« ⋯ » d'un favori : touche, Local, retirer.</summary>
    [RelayCommand]
    private async Task MoreAsync(StrFavoriteItem? item)
    {
        if (item is null)
        {
            return;
        }

        var onKey = Localization.Get("L_FavOnKey");
        var keepLocal = Localization.Get("L_AddLocal");
        var remove = Localization.Get("L_FavRemove");

        var choices = item.CanPlay
            ? new[] { onKey, keepLocal, remove }
            : new[] { remove };

        var choice = await Shell.Current.DisplayActionSheetAsync(
            item.Name,
            Localization.Get("L_Cancel"),
            null,
            choices).ConfigureAwait(true);

        if (choice == onKey && PickKey is { } pick)
        {
            await pick(item.Favorite.ToStation()).ConfigureAwait(true);
        }
        else if (choice == keepLocal && KeepLocal is { } keep)
        {
            keep(item.Favorite.ToStation());
        }
        else if (choice == remove)
        {
            await RemoveAsync(item).ConfigureAwait(true);
        }
    }

    // ================================================================ Retirer

    private async Task RemoveAsync(StrFavoriteItem item)
    {
        var go = Localization.Get("L_FavRemoveGo");

        var answer = await Shell.Current.DisplayActionSheetAsync(
            Localization.Get("L_FavRemoveConfirm", item.Name),
            Localization.Get("L_Cancel"),
            null,
            go).ConfigureAwait(true);

        if (answer != go)
        {
            return;
        }

        var str = Device?.Str;

        if (str is null)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_NoSpeaker");
            return;
        }

        var key = item.Favorite.Key;
        IsBusy = true;

        try
        {
            await str.RemoveFavoriteAsync(key).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);

        // La liste relue fait foi : un favori toujours là n'a pas été retiré.
        if (Items.Any(i => string.Equals(i.Favorite.Key, key, StringComparison.Ordinal)))
        {
            IsError = true;
            StatusMessage = Localization.Get("S_FavNotRemoved", item.Name);
        }
        else
        {
            ShowInfo(Localization.Get("S_FavRemoved", item.Name));
        }
    }

    // ================================================================ Ajouter

    /// <summary>Ajoute une station trouvée par la recherche. Le message est celui de cette carte.</summary>
    public Task AddStationAsync(RadioStation station) => AddAsync(StrFavorite.FromStation(station));

    /// <summary>« ★ Ajouter ce qui joue » : la radio que l'enceinte joue en ce moment.</summary>
    [RelayCommand]
    private async Task AddPlayingAsync()
    {
        var device = Device;

        if (device?.Str is not { } str)
        {
            IsError = true;
            StatusMessage = Localization.Get(device is null ? "S_NoSpeaker" : "S_NeedAgentStation");
            return;
        }

        LocalPreset? found;
        IsBusy = true;

        try
        {
            var now = await device.GetNowPlayingAsync().ConfigureAwait(true);
            var recent = await ReadOrEmpty(() => str.GetRecentAsync()).ConfigureAwait(true);
            var presets = await ReadOrEmpty(() => str.GetPresetsAsync()).ConfigureAwait(true);
            found = LocalPresetList.FromPlaying(now, recent, presets, device.Host, str.LastPlayed);
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (found is null)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_FavNothing");
            return;
        }

        await AddAsync(StrFavorite.FromLocal(found), matchName: true).ConfigureAwait(true);
    }

    /// <summary>
    /// Ajoute un favori, sauf s'il est déjà dans la liste : même adresse, ou, pour
    /// « Ajouter ce qui joue », même nom. Sans identifiant d'annuaire, la clé que
    /// l'agent calcule (« nom|url ») différerait de celle du favori existant, et il
    /// l'ajouterait une seconde fois.
    /// </summary>
    private async Task AddAsync(StrFavorite favorite, bool matchName = false)
    {
        var str = Device?.Str;

        if (str is null)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_NoSpeaker");
            return;
        }

        if (favorite.Name is not { Length: > 0 } || !favorite.CanPlay)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_LocalBadUrl");
            return;
        }

        if (FindExisting(favorite, matchName) is { } existing)
        {
            IsShown = true;
            ShowInfo(Localization.Get("S_FavAlready", existing.Name));
            return;
        }

        IsBusy = true;
        StrFavoriteAdded result;

        try
        {
            result = await str.AddFavoriteAsync(favorite).ConfigureAwait(true);
        }
        catch (SpeakerException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_FavAgentTooOld");
            return;
        }
        catch (SpeakerException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && !string.IsNullOrWhiteSpace(ex.ResponseBody))
        {
            IsError = true;
            StatusMessage = Localization.Get("S_AlarmRefused", ex.ResponseBody!.Trim());
            return;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);
        IsShown = true;
        ShowInfo(Localization.Get(result.Added ? "S_FavAdded" : "S_FavAlready", favorite.Name));
    }

    // ================================================================ Outils

    private StrFavoriteItem? FindExisting(StrFavorite favorite, bool matchName)
    {
        static bool Same(string? a, string? b)
            => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
               string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        var urls = new[] { favorite.Url, favorite.UrlResolved };

        return Items.FirstOrDefault(i =>
                   urls.Any(u => Same(u, i.Favorite.Url) || Same(u, i.Favorite.UrlResolved))) ??
               (matchName ? Items.FirstOrDefault(i => Same(i.Name, favorite.Name)) : null);
    }

    private async Task LoadArtAsync(StrFavoriteItem item)
    {
        if (item.HasArt || string.IsNullOrWhiteSpace(item.Favorite.Favicon))
        {
            return;
        }

        await _artGate.WaitAsync().ConfigureAwait(true);

        try
        {
            if (item.HasArt)
            {
                return;
            }

            var (source, _) = await _artwork.GetFromChainAsync(item.Favorite.Favicon, background: true).ConfigureAwait(true);

            if (source is not null)
            {
                item.ArtSource = source;
                item.HasArt = true;
            }
        }
        catch (Exception)
        {
            // Pas de logo : le symbole de repli reste.
        }
        finally
        {
            _artGate.Release();
        }
    }

    private static async Task<IReadOnlyList<T>> ReadOrEmpty<T>(Func<Task<IReadOnlyList<T>>> read)
    {
        try
        {
            return await read().ConfigureAwait(true);
        }
        catch (Exception)
        {
            return Array.Empty<T>();
        }
    }
}

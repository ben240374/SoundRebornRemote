using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>Une présélection « Local » telle qu'affichée dans la liste.</summary>
public sealed partial class LocalPresetItem : ObservableObject
{
    public required LocalPreset Preset { get; init; }

    public string Id => Preset.Id;

    public string Url => Preset.Url;

    [ObservableProperty]
    private string _name = "";

    /// <summary>L'hôte du flux : de quoi distinguer deux stations du même nom.</summary>
    public string Detail => Uri.TryCreate(Preset.Url, UriKind.Absolute, out var uri) ? uri.Host : Preset.Url;

    [ObservableProperty]
    private ImageSource? _artSource;

    [ObservableProperty]
    private bool _hasArt;

    /// <summary>Vrai pour la station que l'enceinte joue en ce moment.</summary>
    [ObservableProperty]
    private bool _isActive;
}

/// <summary>
/// Les présélections « Local » : des radios gardées dans le téléphone, en nombre
/// libre, en plus des douze touches de l'enceinte. Elles se lancent sur
/// l'enceinte sélectionnée (en groupe, le maître : tout le groupe suit) par
/// POST /api/play, la même requête que « Écouter » dans la recherche.
///
/// On les garnit de trois façons : la radio qui joue, une station trouvée par la
/// recherche, ou une adresse collée à la main. Une touche Spotify n'y a pas sa
/// place : l'agent ne sait lancer une playlist que depuis l'une de ses touches.
///
/// Sa propre vue-modèle, partagée par les onglets Lecture et Présélections, qui
/// la montrent chacun à la place de la grille quand on choisit « Local ».
/// </summary>
public sealed partial class LocalPresetsViewModel : BaseViewModel
{
    public const string StoreKey = "local_presets";

    private readonly ArtworkCache _artwork;
    private readonly List<LocalPreset> _presets;

    /// <summary>Dernière station annoncée par l'enceinte, pour la mise en avant.</summary>
    private string? _playingStation;

    public LocalPresetsViewModel(SpeakerManager speakers, ArtworkCache artwork) : base(speakers)
    {
        _artwork = artwork;
        _presets = Load();

        foreach (var preset in _presets)
        {
            Items.Add(NewItem(preset));
        }

        Count = Items.Count;

        foreach (var item in Items)
        {
            _ = LoadArtAsync(item);
        }
    }

    public ObservableCollection<LocalPresetItem> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(ShowCount))]
    private int _count;

    public bool IsEmpty => Count == 0;

    /// <summary>La pastille du sélecteur « Local » : le nombre de présélections, dès qu'il y en a une.</summary>
    public bool ShowCount => Count > 0;

    /// <summary>Vrai quand les onglets montrent cette liste à la place des touches de l'enceinte.</summary>
    [ObservableProperty]
    private bool _isShown;

    // ================================================================ Lancer

    [RelayCommand]
    private async Task PlayAsync(LocalPresetItem? item)
    {
        if (item is null || IsBusy)
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

        if (device.Str is not { } str)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_NeedAgentStation");
            return;
        }

        MarkActive(item);

        var preset = item.Preset;

        await RunAsync(
            _ => str.PlayUrlAsync(preset.Url, preset.Name, preset.Codec, FirstArt(preset.Art)),
            Localization.Get("S_StationPlaying", preset.Name)).ConfigureAwait(true);

        if (IsError)
        {
            MarkActive(null);
        }
    }

    /// <summary>
    /// Met en avant la présélection dont le nom est celui de la station que
    /// l'enceinte annonce. Appelée par l'onglet Présélections à chaque état reçu.
    /// </summary>
    public void SyncActive(string? stationName, bool isIdle)
    {
        _playingStation = isIdle ? null : stationName;

        var match = string.IsNullOrWhiteSpace(_playingStation)
            ? null
            : Items.FirstOrDefault(i => string.Equals(i.Name, _playingStation!.Trim(), StringComparison.OrdinalIgnoreCase));

        MarkActive(match);
    }

    private void MarkActive(LocalPresetItem? active)
    {
        foreach (var item in Items)
        {
            item.IsActive = ReferenceEquals(item, active);
        }
    }

    // ================================================================ Ajouter

    /// <summary>Garde la radio que l'enceinte joue en ce moment.</summary>
    [RelayCommand]
    private async Task AddPlayingAsync()
    {
        var device = Device;

        if (device is null)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_NoSpeaker");
            return;
        }

        if (device.Str is not { } str)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_NeedAgentStation");
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
            IsBusy = false;
            ShowError(ex);
            return;
        }

        IsBusy = false;

        if (found is null)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_LocalNothing");
            return;
        }

        Add(found);
    }

    /// <summary>Ajoute une adresse de flux tapée ou collée, puis son nom.</summary>
    [RelayCommand]
    private async Task AddUrlAsync()
    {
        var typedUrl = await Shell.Current.DisplayPromptAsync(
            Localization.Get("L_LocalUrlTitle"),
            Localization.Get("L_LocalUrlMessage"),
            Localization.Get("L_Next"),
            Localization.Get("L_Cancel"),
            placeholder: "https://",
            maxLength: 1000).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(typedUrl))
        {
            return;
        }

        var url = typedUrl.Trim();

        if (!LocalPresetList.IsStreamUrl(url))
        {
            IsError = true;
            StatusMessage = Localization.Get("S_LocalBadUrl");
            return;
        }

        var typedName = await Shell.Current.DisplayPromptAsync(
            Localization.Get("L_LocalNameTitle"),
            Localization.Get("L_LocalNameMessage"),
            Localization.Get("L_LocalAddGo"),
            Localization.Get("L_Cancel"),
            maxLength: LocalPresetList.MaxNameLength,
            initialValue: Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "").ConfigureAwait(true);

        var name = LocalPresetList.CleanName(typedName);

        if (name.Length == 0)
        {
            return;
        }

        Add(new LocalPreset { Id = LocalPresetList.NewId(), Name = name, Url = url });
    }

    /// <summary>Ajoute une station trouvée par la recherche. Vrai quand elle a été ajoutée.</summary>
    public bool AddStation(RadioStation station)
        => Add(new LocalPreset
        {
            Id = LocalPresetList.NewId(),
            Name = LocalPresetList.CleanName(station.Name),
            Url = station.PlayableUrl,
            Art = string.IsNullOrWhiteSpace(station.Favicon) ? null : station.Favicon,
            Codec = string.IsNullOrWhiteSpace(station.Codec) ? null : station.Codec,
        });

    private bool Add(LocalPreset preset)
    {
        preset.Name = LocalPresetList.CleanName(preset.Name);

        if (preset.Name.Length == 0 || !LocalPresetList.IsStreamUrl(preset.Url))
        {
            IsError = true;
            StatusMessage = Localization.Get("S_LocalBadUrl");
            return false;
        }

        var existing = _presets.FirstOrDefault(p =>
            string.Equals(p.Url.Trim(), preset.Url.Trim(), StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            // La même radio deux fois ne sert à rien : on montre celle qui est déjà là.
            IsShown = true;
            IsError = false;
            StatusMessage = Localization.Get("S_LocalAlready", existing.Name);
            return false;
        }

        if (_presets.Count >= LocalPresetList.MaxCount)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_LocalFull", LocalPresetList.MaxCount);
            return false;
        }

        _presets.Add(preset);
        Save();

        var item = NewItem(preset);
        Items.Add(item);
        Count = Items.Count;
        SyncActive(_playingStation, _playingStation is null);
        _ = LoadArtAsync(item);

        IsShown = true;
        ShowInfo(Localization.Get("S_LocalAdded", preset.Name));
        return true;
    }

    // ================================================================ Renommer, retirer

    [RelayCommand]
    private async Task RenameAsync(LocalPresetItem? item)
    {
        if (item is null)
        {
            return;
        }

        var current = item.Name;

        var typed = await Shell.Current.DisplayPromptAsync(
            Localization.Get("L_LocalRenameTitle"),
            Localization.Get("L_KeyRenameMessage"),
            Localization.Get("L_KeyRenameGo"),
            Localization.Get("L_Cancel"),
            maxLength: LocalPresetList.MaxNameLength,
            initialValue: current).ConfigureAwait(true);

        var name = LocalPresetList.CleanName(typed);

        if (name.Length == 0 || string.Equals(name, current, StringComparison.Ordinal))
        {
            return;
        }

        item.Preset.Name = name;
        item.Name = name;
        Save();
        SyncActive(_playingStation, _playingStation is null);
        ShowInfo(Localization.Get("S_LocalRenamed", name));
    }

    [RelayCommand]
    private async Task RemoveAsync(LocalPresetItem? item)
    {
        if (item is null)
        {
            return;
        }

        var go = Localization.Get("L_LocalRemoveGo");

        var answer = await Shell.Current.DisplayActionSheetAsync(
            Localization.Get("L_LocalRemoveConfirm", item.Name),
            Localization.Get("L_Cancel"),
            null,
            go).ConfigureAwait(true);

        if (answer != go)
        {
            return;
        }

        _presets.RemoveAll(p => p.Id == item.Id);
        Items.Remove(item);
        Count = Items.Count;
        Save();
        ShowInfo(Localization.Get("S_LocalRemoved", item.Name));
    }

    // ================================================================ Sauvegarde

    /// <summary>Les présélections du téléphone, pour la sauvegarde.</summary>
    public IReadOnlyList<LocalPreset> SavedPresets => _presets.ToList();

    /// <summary>
    /// Ajoute les présélections d'une sauvegarde à celles du téléphone : une adresse
    /// déjà présente est passée, et la liste ne dépasse pas son plafond. Chaque
    /// entrée reçoit un nouvel identifiant, pour qu'aucune ne puisse en masquer une
    /// autre. Rend le nombre de présélections ajoutées.
    /// </summary>
    public int Import(IEnumerable<LocalPreset> presets)
    {
        var added = new List<LocalPreset>();

        foreach (var preset in LocalPresetList.Sanitize(presets))
        {
            if (_presets.Count + added.Count >= LocalPresetList.MaxCount)
            {
                break;
            }

            if (LocalPresetList.Contains(_presets, preset.Url) || LocalPresetList.Contains(added, preset.Url))
            {
                continue;
            }

            preset.Id = LocalPresetList.NewId();
            added.Add(preset);
        }

        if (added.Count == 0)
        {
            return 0;
        }

        _presets.AddRange(added);
        Save();

        foreach (var preset in added)
        {
            var item = NewItem(preset);
            Items.Add(item);
            _ = LoadArtAsync(item);
        }

        Count = Items.Count;
        SyncActive(_playingStation, _playingStation is null);
        return added.Count;
    }

    // ================================================================ Rangement

    private static List<LocalPreset> Load()
    {
        try
        {
            return LocalPresetList.Parse(Preferences.Default.Get(StoreKey, string.Empty));
        }
        catch (Exception)
        {
            // Liste illisible : on repart d'une liste vide plutôt que de bloquer l'onglet.
            return new List<LocalPreset>();
        }
    }

    private void Save()
    {
        try
        {
            Preferences.Default.Set(StoreKey, LocalPresetList.Serialize(_presets));
        }
        catch (Exception)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_LocalNotSaved");
        }
    }

    private static LocalPresetItem NewItem(LocalPreset preset) => new()
    {
        Preset = preset,
        Name = preset.Name,
    };

    private async Task LoadArtAsync(LocalPresetItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Preset.Art))
        {
            return;
        }

        try
        {
            var (source, _) = await _artwork.GetFromChainAsync(item.Preset.Art, background: true).ConfigureAwait(true);

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
    }

    /// <summary>Le premier candidat d'une chaîne « a|b|c » : l'agent attend une seule adresse d'icône.</summary>
    private static string? FirstArt(string? art)
        => ArtworkCache.SplitCandidates(art).FirstOrDefault();

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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>Une enceinte proposée en haut de l'écran de lecture.</summary>
public sealed partial class SpeakerChoice : ObservableObject
{
    public required SpeakerEndpoint Endpoint { get; init; }

    public string Name => Endpoint.DisplayName;

    public string Host => Endpoint.Host;

    [ObservableProperty]
    private string _model = "";

    /// <summary>« v0.9.79 », vide tant que l'agent n'a pas été interrogé.</summary>
    [ObservableProperty]
    private string _agentVersion = "";

    [ObservableProperty]
    private bool _hasAgent;

    /// <summary>Vrai pour l'enceinte actuellement pilotée — la carte est alors mise en avant.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Vrai quand cette enceinte suit l'enceinte active dans un groupe.</summary>
    [ObservableProperty]
    private bool _isGrouped;
}

/// <summary>Le volume d'une enceinte du groupe, réglable individuellement.</summary>
public sealed partial class MemberVolume : ObservableObject
{
    private bool _suppress;

    public required string Ip { get; init; }

    public required string Name { get; init; }

    public required bool IsMaster { get; init; }

    /// <summary>
    /// Identifiant Bose du membre. Nécessaire pour retirer une enceinte d'une zone
    /// dont elle n'est pas le maître : le firmware veut l'identité du maître, et
    /// l'enceinte pilotée n'est pas toujours celle-là.
    /// </summary>
    public string DeviceId { get; init; } = "";

    [ObservableProperty]
    private bool _isReachable = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeLabel))]
    private double _volume;

    public string VolumeLabel => $"{(int)Math.Round(Volume)} %";

    /// <summary>Appelé quand l'utilisateur bouge le curseur, pas quand on rafraîchit.</summary>
    internal Action<MemberVolume>? Push { get; set; }

    /// <summary>Pose une valeur venue de l'enceinte sans la lui renvoyer aussitôt.</summary>
    internal void SetQuietly(double value)
    {
        _suppress = true;
        Volume = value;
        _suppress = false;
    }

    partial void OnVolumeChanged(double value)
    {
        if (!_suppress)
        {
            Push?.Invoke(this);
        }
    }

    /// <summary>
    /// Les touches « − » et « + » de la ligne. On pose la valeur comme le ferait le
    /// curseur : OnVolumeChanged se charge de l'envoyer, et les deux chemins restent
    /// identiques. Une enceinte injoignable ne se règle pas — la commande partirait
    /// pour rien et attendrait le budget entier.
    /// </summary>
    [RelayCommand]
    private void Step(string? amount)
    {
        if (!IsReachable || !int.TryParse(amount, out var step))
        {
            return;
        }

        Volume = Math.Clamp((int)Math.Round(Volume) + step, 0, 100);
    }
}

/// <summary>
/// Écran principal : l'enceinte pilotée, ce qui joue, le transport, le volume,
/// les graves, les six touches et le groupement.
///
/// L'état vient du WebSocket gabbo quand il est debout ; sinon un rafraîchissement
/// périodique prend le relais.
/// </summary>
public sealed partial class PlayerViewModel : BaseViewModel
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Cadence de relecture de la zone quand le WebSocket est debout.
    ///
    /// Une enceinte membre qu'on éteint ou qu'on rallume ne produit aucun
    /// zoneUpdated sur le maître : la composition du groupe n'a pas changé de son
    /// point de vue, seule la joignabilité d'un membre a bougé. Sans cette
    /// relecture, la ligne de l'enceinte éteinte reste affichée avec son dernier
    /// volume, et le volume d'ensemble ne revient pas quand elle se rallume.
    /// </summary>
    /// <summary>
    /// Temps laissé à l'enceinte pour appliquer un allumage ou une mise en veille.
    ///
    /// Le réveil n'est pas instantané : le firmware continue d'annoncer STANDBY
    /// pendant plusieurs secondes après avoir accepté la commande. Sans ce sursis,
    /// la première relecture reprend la main et le bouton « Allumer » réapparaît
    /// aussitôt, ce qui laisse croire que rien ne s'est passé.
    /// </summary>
    private static readonly TimeSpan PowerGrace = TimeSpan.FromSeconds(8);

    /// <summary>Nom du maître du groupe, pour l'affichage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupedWithLabel))]
    private string _groupMasterName = "";

    /// <summary>
    /// Faux quand l'enceinte pilotée est un esclave. L'interface s'en sert pour ne pas
    /// proposer des actions que cette enceinte n'a pas le pouvoir d'exécuter.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSlaveSpeaker))]
    [NotifyPropertyChangedFor(nameof(ShowUngroupHint))]
    [NotifyPropertyChangedFor(nameof(ShowGroupedBanner))]
    private bool _isMasterSpeaker = true;

    /// <summary>
    /// Vrai quand les commandes sont adressées au maître alors que l'utilisateur a
    /// désigné une autre enceinte du groupe.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGroupedBanner))]
    [NotifyPropertyChangedFor(nameof(ShowUngroupHint))]
    private bool _isFollowingMaster;

    /// <summary>
    /// Le bandeau « Groupé avec … » sert dans les deux cas : tant que la redirection
    /// n'a pas eu lieu, et une fois qu'elle a eu lieu — l'enceinte désignée reste
    /// alors une suivante, même si les commandes partent ailleurs.
    /// </summary>
    public bool ShowGroupedBanner => IsSlaveSpeaker || IsFollowingMaster;

    /// <summary>L'inverse, pour l'affichage : le projet n'a pas de convertisseur de négation.</summary>
    public bool IsSlaveSpeaker => !IsMasterSpeaker;

    /// <summary>
    /// L'invite « touche une enceinte cochée pour la retirer » ne s'adresse qu'au
    /// maître : sur une esclave, le bandeau au-dessus dit déjà quoi faire.
    /// </summary>
    public bool ShowUngroupHint => IsGroupActive && IsMasterSpeaker && !IsFollowingMaster;

    /// <summary>« Groupé avec Bose Cuisine », affiché sur l'enceinte qui suit.</summary>
    public string GroupedWithLabel => Localization.Get("S_GroupedWith", GroupMasterName);

    /// <summary>
    /// Maître retrouvé par ZoneLocator quand l'agent n'a rien su dire de la zone.
    /// Nom d'affichage et adresse, rien de plus : on ne connaît pas les volumes
    /// des autres membres par cette voie.
    /// </summary>
    private (string Name, string Ip)? _locatedMaster;

    private DateTime _powerRequestedAt = DateTime.MinValue;
    private bool _powerRequestedOn;

    private static readonly TimeSpan ZonePollInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Cadence quand aucun groupe n'est connu. Elle existe parce qu'un groupe peut
    /// naître sans nous : un groupe permanent que l'agent reforme au démarrage de la
    /// lecture, ou un groupe créé depuis l'application STR de bureau. Se fier au seul
    /// état connu rendrait l'application aveugle à ces cas — deux enceintes jouant
    /// ensemble sans que rien ne l'indique à l'écran.
    /// </summary>
    private static readonly TimeSpan IdleZonePollInterval = TimeSpan.FromSeconds(30);

    private DateTime _zonePolledAt = DateTime.MinValue;

    /// <summary>
    /// Le maître réel de la zone, tel que les enceintes le déclarent.
    ///
    /// L'application supposait jusqu'ici que l'enceinte pilotée était le maître. C'est
    /// vrai du groupe qu'on vient de former soi-même, et faux dès qu'on bascule sur un
    /// esclave, ou qu'un groupe a été formé ailleurs — depuis l'application STR de
    /// bureau, ou reformé tout seul parce qu'il est permanent. Toutes les commandes de
    /// zone réclamant l'identité du maître, s'en remettre à l'enceinte active donnait
    /// des ordres à un appareil qui n'avait pas autorité, et un affichage qui changeait
    /// selon l'enceinte regardée.
    /// </summary>
    private MemberVolume? _zoneMaster;

    /// <summary>Pendant ce délai après une action locale, les mises à jour entrantes sont ignorées.</summary>
    private static readonly TimeSpan EchoSuppression = TimeSpan.FromMilliseconds(1500);

    private readonly PresetsViewModel _presetsViewModel;
    private readonly ArtworkCache _artwork;
    private readonly ZoneLocator _zones;
    private readonly Debouncer _volumeDebouncer = new(TimeSpan.FromMilliseconds(220));
    private readonly Debouncer _bassDebouncer = new(TimeSpan.FromMilliseconds(320));
    private readonly Debouncer _zoneRefreshDebouncer = new(TimeSpan.FromMilliseconds(1200));
    private readonly Dictionary<string, Debouncer> _memberDebouncers = new(StringComparer.OrdinalIgnoreCase);

    private DateTime _volumeTouchedAt = DateTime.MinValue;
    private bool _suppressVolumePush;
    private bool _suppressBassPush;
    private IDispatcherTimer? _pollTimer;
    private SpeakerDevice? _subscribedTo;
    private NowPlaying _lastNowPlaying = NowPlaying.Standby;

    /// <summary>
    /// Le titre en direct tel que l'agent le donne (GET /api/stream/title) pendant
    /// une touche Spotify (agent 1.8.2) ou une radio : null le reste du temps, et
    /// quand il n'y a pas de morceau à montrer.
    /// </summary>
    private StrStreamTitle? _liveSong;

    /// <summary>Ce que <see cref="_liveSong"/> décrit : une touche Spotify ou une radio.</summary>
    private LiveKind _liveSongKind;

    /// <summary>Le flux pour lequel <see cref="_liveSong"/> a été lu, et quand.</summary>
    private string _liveSongFor = "";

    private DateTime _liveSongAt = DateTime.MinValue;

    /// <summary>
    /// Le titre brut du morceau affiché en ce moment (vide quand aucun ne l'est).
    /// Sert à reconnaître ce titre s'il revient juste après un changement de station.
    /// Seul un titre réellement affiché compte : celui que l'agent donne déjà pour
    /// la station suivante, avant que l'enceinte n'annonce le changement, ne doit pas
    /// être pris pour un titre périmé.
    /// </summary>
    private string _lastShownTitle = "";

    /// <summary>
    /// Titre de la station précédente, à ignorer tant que l'agent le renvoie encore :
    /// au changement de station, l'agent garde quelques secondes le titre de
    /// l'ancien flux, le temps que le nouveau envoie le sien. Sans cela, le morceau
    /// de Nostalgie restait affiché sous le logo de la station suivante.
    /// </summary>
    private string _staleTitle = "";

    private DateTime _staleUntil = DateTime.MinValue;

    /// <summary>Au-delà, un titre identique à celui de l'ancienne station est cru : c'est le même morceau.</summary>
    private static readonly TimeSpan StaleTitleWindow = TimeSpan.FromSeconds(20);

    /// <summary>Une lecture récente sert telle quelle : les notifications arrivent par rafales.</summary>
    private static readonly TimeSpan LiveSongReuse = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Le morceau change sans que l'enceinte ne l'annonce (c'est le même flux) :
    /// on le relit à chaque passage du sondage (5 s) tant qu'une touche Spotify
    /// joue. L'agent connaît le changement tout de suite ; c'est ce rythme qui
    /// fait le retard affiché, une lecture locale ne coûte presque rien.
    /// </summary>
    private static readonly TimeSpan LiveSongInterval = TimeSpan.FromSeconds(4);

    /// <summary>Identité de ce qui est affiché comme pochette, pour savoir quand la remplacer.</summary>
    private string _artKey = "";

    /// <summary>
    /// Photo des volumes au moment où l'utilisateur commence à déplacer le curseur
    /// d'ensemble. On applique l'écart à CETTE référence plutôt qu'à la valeur
    /// courante : sinon chaque cran du glissement s'ajoute au précédent et les
    /// enceintes dérivent.
    /// </summary>
    private readonly Dictionary<string, double> _memberBaseline = new(StringComparer.OrdinalIgnoreCase);

    private double _groupBaseline;

    /// <summary>« J'aime » / « Je n'aime pas » pour le titre Spotify en cours (agent 1.8.1+).</summary>
    public SpotifyLikeViewModel Like { get; }

    public PlayerViewModel(SpeakerManager speakers, PresetsViewModel presets, ArtworkCache artwork, ZoneLocator zones)
        : base(speakers)
    {
        _presetsViewModel = presets;
        _artwork = artwork;
        _zones = zones;
        Like = new SpotifyLikeViewModel(speakers);

        Speakers.DeviceChanged += (_, device) => OnMainThread(() =>
        {
            Subscribe(device);
            ReloadSpeakerList();
            _ = RefreshAsync();
            _ = RefreshSleepAsync();
        });

        // La rangée de touches affichée appartient à l'onglet Présélections ; cette
        // page la montre aussi, elle relaie donc ses changements à son sélecteur.
        _presetsViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(HasSecondRow) or nameof(IsSecondRow) or nameof(IsFirstRow)
                or nameof(SecondRowCount) or nameof(ShowSecondRowCount)
                or nameof(FirstRowCount) or nameof(ShowFirstRowCount)
                or nameof(OnceSelected) or nameof(TwiceSelected) or nameof(ShowKeys))
            {
                OnPropertyChanged(e.PropertyName);
            }
        };

        // Une enceinte oubliée ou retrouvée dans les réglages doit se voir ici
        // tout de suite, sans attendre un retour sur l'onglet.
        Speakers.KnownSpeakersChanged += (_, _) => OnMainThread(ReloadSpeakerList);

        Localization.LanguageChanged += (_, _) => OnMainThread(ApplyLanguage);

        Subscribe(Speakers.Current);
        ReloadSpeakerList();
        ApplyLanguage();
    }

    /// <summary>Les six touches, partagées avec l'onglet dédié — une seule source de vérité.</summary>
    public ObservableCollection<PresetTile> Presets => _presetsViewModel.Presets;

    /// <summary>Vrai quand l'agent gère la seconde rangée : le sélecteur apparaît.</summary>
    public bool HasSecondRow => _presetsViewModel.HasSecondRow;

    /// <summary>Vrai quand la grille montre les touches pressées deux fois.</summary>
    public bool IsSecondRow => _presetsViewModel.IsSecondRow;

    public bool IsFirstRow => _presetsViewModel.IsFirstRow;

    /// <summary>La pastille de « Deux fois » : touches garnies de la seconde rangée.</summary>
    public int SecondRowCount => _presetsViewModel.SecondRowCount;

    public bool ShowSecondRowCount => _presetsViewModel.ShowSecondRowCount;

    /// <summary>La pastille de « Une fois » : touches garnies de la première rangée.</summary>
    public int FirstRowCount => _presetsViewModel.FirstRowCount;

    public bool ShowFirstRowCount => _presetsViewModel.ShowFirstRowCount;

    /// <summary>Les présélections « Local » du téléphone, partagées avec l'onglet Présélections.</summary>
    public LocalPresetsViewModel Local => _presetsViewModel.Local;

    public bool OnceSelected => _presetsViewModel.OnceSelected;

    public bool TwiceSelected => _presetsViewModel.TwiceSelected;

    /// <summary>La grille des touches est montrée : la liste locale ne l'a pas remplacée.</summary>
    public bool ShowKeys => _presetsViewModel.ShowKeys;

    /// <summary>Sélecteur « Local » de cette page.</summary>
    [RelayCommand]
    private void ShowLocal() => _presetsViewModel.Local.IsShown = true;

    /// <summary>Les favoris STR, partagés avec l'onglet Présélections.</summary>
    public StrFavoritesViewModel Favorites => _presetsViewModel.Favorites;

    /// <summary>Sélecteur « ★ STR » de cette page.</summary>
    [RelayCommand]
    private void ShowFavorites() => _presetsViewModel.OpenFavorites();

    /// <summary>
    /// Sélecteur « Une fois / Deux fois » de cette page. Le paramètre vaut "1" ou "2".
    /// Toujours disponible, même pendant le chargement des logos (voir PresetsViewModel.ShowRowAsync).
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ShowPresetRowAsync(string? row) => _presetsViewModel.SelectRowAsync(row == "2");

    // ================================================================ Enceintes

    public ObservableCollection<SpeakerChoice> SpeakerChoices { get; } = new();

    /// <summary>Les autres enceintes, proposées pour un groupement en un geste.</summary>
    public ObservableCollection<SpeakerChoice> GroupCandidates { get; } = new();

    [ObservableProperty]
    private bool _hasSeveralSpeakers;

    /// <summary>
    /// Vrai quand il n'y a vraiment aucune carte à afficher : aucune enceinte
    /// retenue et aucune connexion. La carte montre alors l'invite au lieu d'un
    /// cadre vide, qui ne dit pas s'il faut attendre ou agir.
    ///
    /// Distinct de HasNoSpeaker de la classe de base, qui ne regarde que l'enceinte
    /// pilotée : porter le même nom masquerait le membre hérité.
    /// </summary>
    [ObservableProperty]
    private bool _speakerListEmpty = true;

    [RelayCommand]
    private async Task SelectSpeakerAsync(SpeakerChoice? choice)
    {
        if (choice is null)
        {
            return;
        }

        // Rien à faire si c'est déjà l'enceinte désignée. On compare à la sélection,
        // pas à l'enceinte commandée : dans un groupe, c'est le maître qui reçoit les
        // commandes, et toucher une suivante doit rester possible.
        var already = Speakers.SelectedEndpoint?.Host ?? Device?.Host;

        if (already is not null && string.Equals(already, choice.Endpoint.Host, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IsBusy = true;

        try
        {
            await Speakers.SelectAsync(choice.Endpoint).ConfigureAwait(true);
            ShowInfo(Localization.Get("S_Connected", choice.Endpoint.DisplayName));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Ajoute l'enceinte au groupe, ou l'en retire si elle y est déjà : la même
    /// pastille sert dans les deux sens, et la coche dit dans quel état on est.
    /// </summary>
    [RelayCommand]
    private async Task ToggleGroupAsync(SpeakerChoice? choice)
    {
        if (choice is null)
        {
            return;
        }

        var wasGrouped = choice.IsGrouped;

        // Une enceinte n'appartient qu'à un groupe à la fois, et c'est le maître qui
        // le compose. Étendre le groupe depuis une enceinte qui ne fait que suivre
        // reviendrait à en republier un autre autour d'elle, donc à déplacer la
        // diffusion de pièce. On renvoie vers le maître.
        if (!wasGrouped && IsSlaveSpeaker)
        {
            ShowInfo(Localization.Get("S_SlaveCannotForm", GroupMasterName));
            return;
        }

        // Un appui pendant qu'un groupement est en cours est ignoré : deux demandes
        // croisées publieraient deux listes de membres différentes.
        if (IsGroupWorking)
        {
            return;
        }

        // Ajouter une enceinte qui joue autre chose arrête son écoute, peut-être dans
        // une autre pièce : on le dit avant, et on n'ajoute qu'après accord.
        if (!wasGrouped && !await ConfirmJoinAsync(choice).ConfigureAwait(true))
        {
            return;
        }

        // Le groupement prend plusieurs secondes. On le dit tout de suite, dans le
        // bloc « Grouper avec » lui-même : le message de bas de page est hors de vue,
        // et sans rien de visible on croit ne pas avoir appuyé.
        IsGroupWorking = true;
        var noteVersion = ++_groupNoteVersion;
        GroupNoteIsError = false;
        GroupNote = Localization.Get(wasGrouped ? "S_Ungrouping" : "S_Grouping", choice.Name);

        string? report = null;
        var failed = false;

        try
        {
            // Relevé ici, avant tout appel réseau : étend-on un groupe qui joue, ou en
            // forme-t-on un nouveau ? La réponse décide de ce que vaut « permanent ».
            var extendsLiveGroup = IsGroupActive || HasGroupVolume || GroupCandidates.Any(c => c.IsGrouped);

            await RunAsync(async device =>
            {
                if (wasGrouped)
                {
                    var self = Speakers.SelectedEndpoint ?? device.Endpoint;
                    await UngroupAsync(device, self, choice, MasterIdentity(device)).ConfigureAwait(false);
                    return;
                }

                if (device.Str is not null)
                {
                    // Un agent trop ancien écrase le groupe permanent qu'un autre groupe
                    // recouvre, au lieu de le mettre de côté. On ne lui confie aucun groupe.
                    if (device.AgentTooOld)
                    {
                        throw new InvalidOperationException(AgentTooOldForGroups(device));
                    }

                    var slaves = GroupCandidates
                        .Where(c => c.IsGrouped || ReferenceEquals(c, choice))
                        .Select(c => new StrZoneMemberRef { DeviceId = c.Endpoint.DeviceId, Ip = c.Endpoint.Host })
                        .ToList();

                    // L'agent enregistre le document tel qu'il le reçoit : omettre
                    // « permanent » sur un groupe qui l'était le rétrograde en groupe
                    // ordinaire, qui cesse de se reformer et qu'une dissolution efface
                    // définitivement. Même chose pour la paire stéréo et son nom.
                    // On relit donc la zone avant de republier — et si cette lecture
                    // échoue, on renonce : republier à l'aveugle détruirait ce que
                    // l'utilisateur a enregistré, pour un simple hoquet réseau.
                    var facts = await device.Str.GetZoneFactsAsync().ConfigureAwait(false);

                    if (facts is null)
                    {
                        throw new InvalidOperationException(Localization.Get("S_ZoneUnreadable"));
                    }

                    // Une paire stéréo n'est pas un groupe multi-pièces, et l'agent ne
                    // sait pas encore lui adjoindre une troisième enceinte. On refuse
                    // plutôt que d'envoyer une formation dont l'issue est inconnue.
                    if (facts.Stereo)
                    {
                        throw new InvalidOperationException(Localization.Get("S_StereoCannotExtend"));
                    }

                    // Le maître est celui de la zone existante, pas l'enceinte qu'on
                    // regarde : republier avec un autre maître déplacerait la diffusion
                    // d'une pièce à l'autre sans que personne ne l'ait demandé.
                    var identity = MasterIdentity(device);
                    var master = new StrZoneMemberRef { DeviceId = identity.DeviceId, Ip = identity.Ip };

                    // L'enceinte pilotée doit figurer parmi les suivantes si elle n'est
                    // pas le maître : sinon republier la zone l'en exclurait.
                    if (!string.Equals(identity.Ip, device.Host, StringComparison.OrdinalIgnoreCase) &&
                        !slaves.Any(s => string.Equals(s.Ip, device.Host, StringComparison.OrdinalIgnoreCase)))
                    {
                        slaves.Add(new StrZoneMemberRef { DeviceId = device.Endpoint.DeviceId, Ip = device.Host });
                    }

                    // Le maître ne doit pas figurer parmi ses propres suivantes.
                    slaves.RemoveAll(s => string.Equals(s.Ip, identity.Ip, StringComparison.OrdinalIgnoreCase));

                    // « permanent » se rend tel qu'on l'a lu quand on ÉTEND le groupe qui
                    // joue : c'est le même groupe, avec une enceinte de plus.
                    //
                    // Sans groupe vivant, le drapeau lu décrit un AUTRE groupe : celui
                    // que l'agent garde en mémoire et qui attend la prochaine lecture.
                    // Le rendre ici remplacerait ce groupe durable par celui qu'on forme
                    // d'un geste. On ne le rend donc que si l'on forme exactement ce
                    // groupe-là ; sinon le nouveau groupe est ordinaire, et l'agent met
                    // le groupe durable de côté pour le rétablir ensuite.
                    var permanent = extendsLiveGroup
                        ? facts.Permanent
                        : facts.Permanent && facts.RemembersExactly(slaves.Select(s => s.Ip));

                    await device.Str.FormZoneAsync(new StrZoneFormRequest
                    {
                        Master = master,
                        Slaves = slaves,
                        Mode = "native",
                        Permanent = permanent,
                    }).ConfigureAwait(false);

                    return;
                }

                if (string.IsNullOrWhiteSpace(choice.Endpoint.DeviceId))
                {
                    throw new InvalidOperationException(Localization.Get("S_MissingDeviceId", choice.Name));
                }

                await device.Bose.AddZoneSlaveAsync(
                    device.Endpoint.DeviceId,
                    device.Host,
                    new[] { new ZoneMember { DeviceId = choice.Endpoint.DeviceId, IpAddress = choice.Endpoint.Host } })
                    .ConfigureAwait(false);
            }, Localization.Get(wasGrouped ? "S_Ungrouped" : "S_GroupedWith", choice.Name));

            report = StatusMessage;
            failed = IsError;

            // Le firmware met un instant à publier la nouvelle zone.
            await Task.Delay(600).ConfigureAwait(true);
            await LoadZoneAsync().ConfigureAwait(true);
        }
        finally
        {
            IsGroupWorking = false;
            GroupNoteIsError = failed;
            GroupNote = report ?? "";
            _ = ClearGroupNoteLaterAsync(noteVersion);
        }
    }

    /// <summary>
    /// Avant d'ajouter <paramref name="choice"/> au groupe : si elle joue (ou est en
    /// pause sur) autre chose que ce que joue le groupe, demande confirmation en
    /// disant ce qui s'arrête et ce qui va jouer. Vrai pour continuer.
    ///
    /// « Autre chose » se juge sur la source et l'adresse, pas sur le nom : deux
    /// flux du même nom peuvent être deux émissions différentes. Une enceinte en
    /// veille, à l'arrêt ou dont l'état ne se lit pas ne bloque rien : la question
    /// ne doit jamais empêcher un groupement qui aurait marché.
    /// </summary>
    private async Task<bool> ConfirmJoinAsync(SpeakerChoice choice)
    {
        NowPlaying theirs;

        try
        {
            // Court : une enceinte qui tarde ne doit pas retenir le groupement.
            using var quick = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            theirs = await new BoseApiClient(choice.Host, Speakers.Http).GetNowPlayingAsync(quick.Token).ConfigureAwait(true);
        }
        catch (Exception)
        {
            return true;
        }

        var paused = theirs.PlayStatus == PlayStatus.Pause;

        if (theirs.IsIdle || !(theirs.IsPlaying || paused || theirs.PlayStatus == PlayStatus.Buffering))
        {
            return true;
        }

        var ours = _lastNowPlaying;

        if (SameMusic(ours, theirs))
        {
            return true;
        }

        var question = Localization.Get(paused ? "L_JoinPaused" : "L_JoinPlaying", choice.Name, Describe(theirs)) + " " +
                       (ours.IsIdle
                           ? Localization.Get("L_JoinThenStops")
                           : Localization.Get("L_JoinThenPlays", Describe(ours)));

        var go = Localization.Get("L_JoinGo");

        var answer = await Shell.Current.DisplayActionSheetAsync(
            question,
            Localization.Get("L_Cancel"),
            null,
            go).ConfigureAwait(true);

        return answer == go;
    }

    /// <summary>Même source et même adresse de flux. Sans adresse des deux côtés, on ne peut pas l'affirmer.</summary>
    private static bool SameMusic(NowPlaying a, NowPlaying b)
        => !a.IsIdle && !b.IsIdle &&
           string.Equals(a.Source, b.Source, StringComparison.OrdinalIgnoreCase) &&
           !string.IsNullOrWhiteSpace(a.Content?.Location) &&
           string.Equals(a.Content?.Location, b.Content?.Location, StringComparison.OrdinalIgnoreCase);

    /// <summary>Ce qui joue, en quelques mots : la station, sinon le contenu, le titre ou la source.</summary>
    private static string Describe(NowPlaying nowPlaying)
        => new[] { nowPlaying.StationName, nowPlaying.Content?.ItemName, nowPlaying.Track }
               .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
           ?? FriendlySource(nowPlaying.Source);

    /// <summary>Le compte rendu du groupement reste quelques secondes, puis s'efface — sauf une erreur.</summary>
    private async Task ClearGroupNoteLaterAsync(int version)
    {
        await Task.Delay(TimeSpan.FromSeconds(6)).ConfigureAwait(true);

        if (version == _groupNoteVersion && !GroupNoteIsError)
        {
            GroupNote = "";
        }
    }

    /// <summary>Vrai pendant un groupement ou un dégroupement lancé depuis « Grouper avec ».</summary>
    [ObservableProperty]
    private bool _isGroupWorking;

    /// <summary>« Groupement avec Bose Salon… », puis le résultat. Vide le reste du temps.</summary>
    [ObservableProperty]
    private string _groupNote = "";

    [ObservableProperty]
    private bool _groupNoteIsError;

    private int _groupNoteVersion;

    /// <summary>
    /// Qui mène la zone, du plus sûr au moins sûr : ce que l'agent a décrit, puis ce
    /// que ZoneLocator a retrouvé, et à défaut l'enceinte pilotée — qui est alors
    /// seule, donc maître d'elle-même.
    /// </summary>
    private (string DeviceId, string Ip) MasterIdentity(SpeakerDevice device)
    {
        if (_zoneMaster is { DeviceId.Length: > 0 })
        {
            return (_zoneMaster.DeviceId, _zoneMaster.Ip);
        }

        if (_locatedMaster is { } located)
        {
            var known = Speakers.KnownSpeakers.FirstOrDefault(s =>
                string.Equals(s.Host, located.Ip, StringComparison.OrdinalIgnoreCase));

            if (known is not null && !string.IsNullOrWhiteSpace(known.DeviceId))
            {
                return (known.DeviceId, located.Ip);
            }
        }

        return (device.Endpoint.DeviceId, device.Host);
    }

    /// <summary>
    /// Les membres de la zone en cours, adresse et deviceID.
    ///
    /// La liste de l'agent est la meilleure source quand elle existe ; sinon on la
    /// reconstitue à partir des pastilles marquées groupées et de l'enceinte
    /// commandée, qui fait forcément partie du groupe qu'elle décrit.
    /// </summary>
    private List<StrZoneMemberRef> CurrentZoneMembers(SpeakerDevice device)
    {
        var members = new List<StrZoneMemberRef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string ip, string deviceId)
        {
            if (!string.IsNullOrWhiteSpace(ip) && seen.Add(ip))
            {
                members.Add(new StrZoneMemberRef { DeviceId = deviceId, Ip = ip });
            }
        }

        foreach (var row in GroupMembers)
        {
            Add(row.Ip, row.DeviceId);
        }

        if (members.Count == 0)
        {
            Add(device.Host, device.Endpoint.DeviceId);

            foreach (var candidate in GroupCandidates.Where(c => c.IsGrouped))
            {
                Add(candidate.Host, candidate.Endpoint.DeviceId);
            }
        }

        return members;
    }

    private async Task UngroupAsync(
        SpeakerDevice device, SpeakerEndpoint self, SpeakerChoice choice, (string DeviceId, string Ip) master)
    {
        var tappedTheMaster = string.Equals(choice.Host, master.Ip, StringComparison.OrdinalIgnoreCase);

        // Toucher la pastille du maître depuis une enceinte qui le suit ne veut pas
        // dire « retirer le maître » — une zone sans maître n'existe pas. Cela veut
        // dire « quitter le groupe » : c'est l'enceinte DÉSIGNÉE qu'on retire, pas
        // celle qui reçoit les commandes, qui est justement le maître dans ce cas.
        var removedIp = tappedTheMaster ? self.Host : choice.Endpoint.Host;
        var removedId = tappedTheMaster ? self.DeviceId : choice.Endpoint.DeviceId;

        await RemoveFromZoneAsync(device, removedIp, removedId, master, choice.Name).ConfigureAwait(false);
    }

    /// <summary>
    /// Retire une enceinte du groupe À LA DEMANDE de l'utilisateur : la pastille
    /// « Grouper avec ». Le groupe enregistré par l'agent suit, puisque c'est sa
    /// composition que l'utilisateur vient de changer.
    ///
    /// La mise en veille ne passe PAS par ici : elle sort l'enceinte du groupe qui
    /// joue sans rien changer au groupe enregistré. Voir
    /// <see cref="RemoveFromLiveZoneAsync"/>.
    /// </summary>
    private async Task RemoveFromZoneAsync(
        SpeakerDevice device, string removedIp, string removedId,
        (string DeviceId, string Ip) master, string removedName)
    {
        // Avec l'agent, on REPUBLIE la zone sans le membre retiré, au lieu de passer
        // par le /removeZoneSlave du firmware. Ce dernier défait bien le groupe
        // vivant, mais le groupe MÉMORISÉ par l'agent continue de lister l'enceinte :
        // à la lecture suivante il la réveille et la rattache, et l'utilisateur voit
        // revenir ce qu'il venait de retirer.
        if (device.Str is not null)
        {
            if (device.AgentTooOld)
            {
                throw new InvalidOperationException(AgentTooOldForGroups(device));
            }

            var facts = await device.Str.GetZoneFactsAsync().ConfigureAwait(false);

            if (facts is null)
            {
                throw new InvalidOperationException(Localization.Get("S_ZoneUnreadable"));
            }

            var remaining = CurrentZoneMembers(device)
                .Where(m => !string.Equals(m.Ip, removedIp, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Plus personne à grouper avec le maître : le groupe n'a plus d'objet.
            if (remaining.Count <= 1)
            {
                await DissolveWithAgentAsync(device).ConfigureAwait(false);
                return;
            }

            await device.Str.FormZoneAsync(new StrZoneFormRequest
            {
                Master = new StrZoneMemberRef { DeviceId = master.DeviceId, Ip = master.Ip },
                Slaves = remaining
                    .Where(m => !string.Equals(m.Ip, master.Ip, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
                Mode = "native",
                Permanent = facts.Permanent,
            }).ConfigureAwait(false);

            return;
        }

        // Sans agent, le firmware est la seule voie, et il réclame l'identité du
        // MAÎTRE. En cas d'échec on remonte l'erreur : l'ancien repli dissolvait le
        // groupe entier, ce qui punissait l'utilisateur d'un délai dépassé.
        if (string.IsNullOrWhiteSpace(removedId) || string.IsNullOrWhiteSpace(master.DeviceId))
        {
            throw new InvalidOperationException(Localization.Get("S_MissingDeviceId", removedName));
        }

        await device.Bose.RemoveZoneSlaveAsync(
            master.DeviceId,
            master.Ip,
            new[] { new ZoneMember { DeviceId = removedId, IpAddress = removedIp } })
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Sort une enceinte du groupe QUI JOUE, sans toucher au groupe enregistré.
    ///
    /// C'est ce qu'il faut avant une mise en veille. Éteindre une enceinte n'est
    /// pas changer la composition du groupe : republier la zone sans elle, comme
    /// le fait un retrait demandé, raccourcissait le groupe durable de
    /// l'utilisateur à chaque veille. L'agent sait très bien qu'un membre d'un
    /// groupe enregistré peut dormir ; il le laisse dormir, et le rappelle à la
    /// prochaine lecture si le groupe est permanent.
    ///
    /// On passe donc par le /removeZoneSlave du firmware, qui n'agit que sur la
    /// zone vivante, et on l'adresse au maître, seul à pouvoir l'exécuter.
    /// </summary>
    private async Task RemoveFromLiveZoneAsync(
        SpeakerDevice device, SpeakerEndpoint target,
        (string DeviceId, string Ip) master, IReadOnlyList<StrZoneMemberRef> members)
    {
        // L'identifiant que la zone elle-même donne à l'enceinte prime sur celui
        // de la découverte : certains châssis en annoncent deux, et le firmware
        // ne reconnaît que le sien.
        var listed = members.FirstOrDefault(m =>
            string.Equals(m.Ip, target.Host, StringComparison.OrdinalIgnoreCase));

        var targetId = string.IsNullOrWhiteSpace(listed?.DeviceId) ? target.DeviceId : listed!.DeviceId!;

        if (string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(master.DeviceId))
        {
            throw new InvalidOperationException(Localization.Get("S_MissingDeviceId", target.DisplayName));
        }

        var bose = string.Equals(master.Ip, device.Host, StringComparison.OrdinalIgnoreCase)
            ? device.Bose
            : new BoseApiClient(master.Ip, Speakers.Http);

        await bose.RemoveZoneSlaveAsync(
            master.DeviceId,
            master.Ip,
            new[] { new ZoneMember { DeviceId = targetId, IpAddress = target.Host } })
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Défait le groupe par l'agent et rend compte de ce qu'il a réellement fait :
    /// un 200 peut annoncer un groupe conservé, ou une paire stéréo qui subsiste.
    /// </summary>
    private async Task DissolveWithAgentAsync(SpeakerDevice device)
    {
        if (device.Str is null)
        {
            return;
        }

        var result = await device.Str.DissolveZoneAsync().ConfigureAwait(false);

        if (!result.Ok)
        {
            throw new InvalidOperationException(
                Localization.Get("S_GroupStillUp", result.Remaining));
        }

        if (result.StereoPairKept)
        {
            OnMainThread(() => ShowInfo(Localization.Get("S_StereoPairKept")));
        }
    }

    /// <summary>Recharge les cartes d'enceintes et marque celle qui est active.</summary>
    public void ReloadSpeakerList()
    {
        var device = Device;

        // La carte mise en avant est celle que l'utilisateur a DÉSIGNÉE, pas celle qui
        // reçoit les commandes. Dans un groupe les deux diffèrent, et voir la sélection
        // sauter sur une autre enceinte laisserait croire que la touche a raté.
        var host = Speakers.SelectedEndpoint?.Host ?? device?.Host;
        var grouped = GroupCandidates
            .Where(c => c.IsGrouped)
            .Select(c => c.Host)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        SpeakerChoices.Clear();
        GroupCandidates.Clear();

        // L'enceinte désignée puis celle qui reçoit les commandes, qu'elles figurent
        // ou non dans la liste retenue : l'une d'elles peut en être sortie sans que la
        // connexion tombe — un « Oublier » depuis les réglages, par exemple, ou une
        // réinstallation de l'agent — et la page de lecture se retrouvait alors sans
        // aucune carte alors qu'une enceinte répondait. Le jeu d'adresses déjà posées
        // évite qu'une enceinte apparaisse deux fois.
        var endpoints = new List<SpeakerEndpoint>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var ordered = new List<SpeakerEndpoint?> { Speakers.SelectedEndpoint, device?.Endpoint };
        ordered.AddRange(Speakers.KnownSpeakers);

        foreach (var speaker in ordered)
        {
            if (speaker is not null && seen.Add(speaker.Host))
            {
                endpoints.Add(speaker);
            }
        }

        foreach (var speaker in endpoints)
        {
            // Deux critères distincts, et c'est voulu :
            //   – la carte mise en avant est celle que l'utilisateur a DÉSIGNÉE ;
            //   – la pastille de groupement s'adresse à toutes les enceintes sauf
            //     celle qui reçoit les commandes, car c'est elle qui compose le
            //     groupe. Les confondre proposait de grouper le maître avec
            //     lui-même dès que la sélection portait sur une suivante.
            var isActive = host is not null && string.Equals(speaker.Host, host, StringComparison.OrdinalIgnoreCase);
            var isTarget = device is not null &&
                string.Equals(speaker.Host, device.Host, StringComparison.OrdinalIgnoreCase);

            var choice = new SpeakerChoice
            {
                Endpoint = speaker,
                Model = speaker.Type,
                AgentVersion = AgentLabel(speaker.AgentVersion),
                HasAgent = speaker.HasStr,
                IsActive = isActive,
                IsGrouped = grouped.Contains(speaker.Host),
            };

            SpeakerChoices.Add(choice);

            if (!isTarget)
            {
                GroupCandidates.Add(choice);
            }
        }

        HasSeveralSpeakers = SpeakerChoices.Count > 1;
        SpeakerListEmpty = SpeakerChoices.Count == 0;
    }

    // ================================================================ État affiché

    [ObservableProperty]
    private string _speakerName = "";

    [ObservableProperty]
    private string _speakerDetail = "";

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private string _liveLabel = "";

    [ObservableProperty]
    private bool _hasStrAgent;

    [ObservableProperty]
    private string _title = "—";

    [ObservableProperty]
    private string _subtitle = "";

    /// <summary>Image déjà téléchargée sur le téléphone, prête à afficher.</summary>
    [ObservableProperty]
    private ImageSource? _artSource;

    [ObservableProperty]
    private bool _hasArt;

    /// <summary>Symbole affiché à la place de la pochette quand la source n'en fournit pas.</summary>
    [ObservableProperty]
    private string _artGlyph = "♪";

    [ObservableProperty]
    private string _sourceLabel = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseGlyph))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PowerLabel))]
    private bool _isStandby = true;

    public string PlayPauseGlyph => IsPlaying ? "❚❚" : "▶";

    public string PowerLabel => Localization.Get(IsStandby ? "L_PowerOnAction" : "L_PowerOffAction");

    // ================================================================ Volume

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeLabel))]
    private double _volume;

    [ObservableProperty]
    private bool _isMuted;

    public string VolumeLabel => $"{(int)Math.Round(Volume)} %";

    partial void OnVolumeChanged(double value)
    {
        if (_suppressVolumePush)
        {
            return;
        }

        _volumeTouchedAt = DateTime.UtcNow;
        var target = (int)Math.Round(value);

        // En groupe, ce curseur règle TOUT LE MONDE : c'est la commande d'ensemble,
        // et les lignes du dessous servent à ajuster une enceinte en particulier.
        // Hors groupe, il ne règle que l'enceinte pilotée.
        if (HasGroupVolume)
        {
            // Déplacement d'ensemble : on ajoute le MÊME écart à chaque enceinte,
            // au lieu de leur imposer la même valeur. L'équilibre réglé à la main
            // est préservé — Cuisine 61 / Salon 20 devient 71 / 30 quand la moyenne
            // passe de 40 à 50, et non 50 / 50.
            var delta = target - _groupBaseline;

            foreach (var row in GroupMembers)
            {
                // Une enceinte éteinte ne se règle pas : lui envoyer la commande
                // ferait attendre le budget entier pour rien, et le curseur du
                // groupe resterait bloqué le temps de l'échec.
                if (!row.IsReachable)
                {
                    continue;
                }

                var reference = _memberBaseline.TryGetValue(row.Ip, out var saved) ? saved : row.Volume;
                var next = (int)Math.Clamp(Math.Round(reference + delta), 0, 100);

                row.SetQuietly(next);
                SendMemberVolume(row.Ip, next);
            }

            ScheduleZoneRefresh();
            return;
        }

        _volumeDebouncer.Debounce(async ct =>
        {
            var device = Device;

            if (device is not null)
            {
                await device.SetVolumeAsync(target, ct).ConfigureAwait(false);
            }
        });
    }

    /// <summary>
    /// Moyenne des enceintes joignables — c'est ce que l'agent renvoie aussi, et
    /// c'est elle que montre le curseur du haut quand un groupe est actif.
    /// </summary>
    private void RecomputeGroupAverage()
    {
        var reachable = GroupMembers.Where(m => m.IsReachable).ToList();

        if (reachable.Count == 0)
        {
            return;
        }

        _suppressVolumePush = true;
        Volume = Math.Round(reachable.Average(m => m.Volume));
        _suppressVolumePush = false;

        CaptureGroupBaseline();
    }

    /// <summary>
    /// Fige les volumes actuels comme référence des prochains déplacements
    /// d'ensemble. Appelée après chaque lecture de la zone et après un réglage
    /// individuel — jamais pendant un glissement.
    /// </summary>
    private void CaptureGroupBaseline()
    {
        _memberBaseline.Clear();

        foreach (var row in GroupMembers)
        {
            _memberBaseline[row.Ip] = row.Volume;
        }

        _groupBaseline = Volume;
    }

    /// <summary>
    /// Relit la zone un instant après une modification, pour se recaler sur ce que
    /// les enceintes ont réellement appliqué. Regroupé, sinon trois curseurs bougés
    /// coup sur coup déclencheraient trois relectures.
    /// </summary>
    private void ScheduleZoneRefresh()
        => _zoneRefreshDebouncer.Debounce(cancellationToken =>
        {
            // Le paramètre ne s'appelle surtout pas « _ » : à l'intérieur, le rejet
            // « _ = LoadZoneAsync() » viserait alors ce paramètre au lieu d'être un
            // rejet, et tenterait d'affecter une Task à un CancellationToken.
            OnMainThread(() => _ = LoadZoneAsync());
            return Task.CompletedTask;
        });

    // ---------------------------------------------------------------- Volume du groupe

    /// <summary>Une ligne par enceinte du groupe, l'active comprise.</summary>
    public ObservableCollection<MemberVolume> GroupMembers { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUngroupHint))]
    private bool _isGroupActive;

    /// <summary>
    /// Vrai seulement avec l'agent et un groupe actif. Commande alors deux choses :
    /// le curseur du haut règle l'ensemble du groupe, et la liste par enceinte
    /// apparaît en dessous.
    /// </summary>
    [ObservableProperty]
    private bool _hasGroupVolume;

    /// <summary>
    /// Aligne toutes les enceintes du groupe sur le volume d'ensemble affiché.
    ///
    /// C'est l'inverse du curseur du haut, qui déplace tout le monde du même écart
    /// pour préserver l'équilibre réglé à la main : ici on efface cet équilibre.
    /// Groupe à 25 %, tout le monde à 25 %. Une enceinte éteinte est laissée de
    /// côté — la commande partirait pour rien et bloquerait le temps du budget.
    /// </summary>
    [RelayCommand]
    private void LevelGroup()
    {
        if (!HasGroupVolume || GroupMembers.Count == 0)
        {
            return;
        }

        var target = (int)Math.Clamp(Math.Round(Volume), 0, 100);

        foreach (var row in GroupMembers)
        {
            if (!row.IsReachable)
            {
                continue;
            }

            row.SetQuietly(target);
            SendMemberVolume(row.Ip, target);
        }

        // La moyenne vaut désormais la cible : on refige la référence, sinon le
        // prochain déplacement d'ensemble repartirait de l'ancien écart.
        _suppressVolumePush = true;
        Volume = target;
        _suppressVolumePush = false;

        CaptureGroupBaseline();
        ScheduleZoneRefresh();

        ShowInfo(Localization.Get("S_GroupLevelled", target));
    }

    /// <summary>Curseur d'une enceinte déplacé à la main.</summary>
    private void PushMemberVolume(MemberVolume member)
    {
        SendMemberVolume(member.Ip, (int)Math.Round(member.Volume));

        // Régler une enceinte fait bouger la moyenne, donc le curseur du haut,
        // et redéfinit la référence des prochains déplacements d'ensemble.
        RecomputeGroupAverage();
        ScheduleZoneRefresh();
    }

    /// <summary>Envoi réseau seul, regroupé par enceinte. Ne recalcule rien.</summary>
    private void SendMemberVolume(string ip, int target)
    {
        if (!_memberDebouncers.TryGetValue(ip, out var debouncer))
        {
            debouncer = new Debouncer(TimeSpan.FromMilliseconds(260));
            _memberDebouncers[ip] = debouncer;
        }

        debouncer.Debounce(async ct =>
        {
            var device = Device;

            if (device?.Str is not null)
            {
                await device.Str.SetZoneVolumeAsync(target, ip, ct).ConfigureAwait(false);
            }
        });
    }

    // ================================================================ Graves

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BassLabel))]
    private double _bass;

    [ObservableProperty]
    private double _bassMinimum = -9;

    [ObservableProperty]
    private double _bassMaximum = 0;

    [ObservableProperty]
    private bool _bassAvailable;

    public string BassLabel => $"{(int)Math.Round(Bass)}";

    partial void OnBassChanged(double value)
    {
        if (_suppressBassPush || !BassAvailable)
        {
            return;
        }

        var target = (int)Math.Round(value);

        _bassDebouncer.Debounce(async ct =>
        {
            var device = Device;

            if (device is not null)
            {
                await device.SetBassAsync(target, ct).ConfigureAwait(false);
            }
        });
    }

    // ================================================================ Commandes

    [RelayCommand]
    private Task PlayPauseAsync() => RunAsync(d => d.PlayPauseAsync(IsPlaying));

    [RelayCommand]
    private Task NextAsync() => RunAsync(d => d.NextAsync());

    [RelayCommand]
    private Task PreviousAsync() => RunAsync(d => d.PreviousAsync());

    [RelayCommand]
    private Task StopAsync() => RunAsync(async d =>
    {
        if (d.Str is not null)
        {
            await d.Str.StopAsync().ConfigureAwait(false);
            return;
        }

        await d.Bose.PressKeyAsync(RemoteKey.Stop).ConfigureAwait(false);
    });

    [RelayCommand]
    private Task ToggleMuteAsync() => RunAsync(d => d.ToggleMuteAsync());

    /// <summary>Allume ou met en veille, selon l'état courant.</summary>
    [RelayCommand]
    private async Task PowerAsync()
    {
        var wanted = IsStandby;   // en veille : on veut allumer, et inversement

        // La veille est la seule commande qui NE suit PAS le maître du groupe.
        //
        // Tout le reste — transport, présélections, volume — s'adresse au maître,
        // puisque c'est lui qui diffuse. Mais « éteindre » vise une enceinte
        // précise : celle que l'utilisateur a désignée. Envoyer la veille au maître
        // éteignait toute la pièce d'à côté et laissait allumée l'enceinte qu'on
        // croyait éteindre.
        var target = Speakers.SelectedEndpoint ?? Device?.Endpoint;

        if (target is null)
        {
            ShowInfo(Localization.Get("S_NoSpeaker"));
            return;
        }

        // Une enceinte qu'on éteint doit d'abord quitter le groupe. Sans ça, la zone
        // garde un membre injoignable : le maître continue de lui diffuser, l'agent
        // s'épuise à le joindre, et au rallumage l'enceinte se retrouve prisonnière
        // d'un groupe qu'elle ne peut plus quitter. Elle sort donc du groupe, et si
        // elle en était le dernier membre utile, le groupe est dissous.
        if (!wanted)
        {
            await LeaveGroupBeforeStandbyAsync(target).ConfigureAwait(true);
        }

        _powerRequestedAt = DateTime.UtcNow;
        _powerRequestedOn = wanted;
        IsStandby = !wanted;      // le bouton reflète tout de suite l'intention

        await RunAsync(d => SetPowerOnEndpointAsync(d, target, wanted)).ConfigureAwait(true);

        if (IsError)
        {
            // La commande a été refusée : on ne garde pas un affichage optimiste.
            _powerRequestedAt = DateTime.MinValue;
            return;
        }

        // Allumer ou éteindre une enceinte change sa joignabilité au sein du groupe,
        // ce dont le maître ne prévient pas. On relit la zone après coup.
        ScheduleZoneRefresh();

        await VerifyPowerAsync(wanted).ConfigureAwait(true);
    }

    /// <summary>
    /// Envoie la veille ou l'allumage à une enceinte précise, qui n'est pas
    /// forcément celle à laquelle l'application est connectée.
    ///
    /// Par l'agent quand elle en a un : l'ordre y est explicite, « on : false ».
    /// Sans agent il faut la touche POWER du firmware, qui est une BASCULE — on
    /// relit donc l'état avant d'appuyer, faute de quoi éteindre une enceinte déjà
    /// éteinte la rallumerait.
    /// </summary>
    private async Task SetPowerOnEndpointAsync(SpeakerDevice device, SpeakerEndpoint target, bool on)
    {
        // L'enceinte désignée est celle qui reçoit les commandes : le chemin
        // habituel, avec ses replis éprouvés.
        if (string.Equals(target.Host, device.Host, StringComparison.OrdinalIgnoreCase))
        {
            await device.SetPowerAsync(on).ConfigureAwait(false);
            return;
        }

        if (target.StrPort is int port)
        {
            var agent = new StrApiClient(target.Host, port, Speakers.Http);
            await agent.SetPowerAsync(on).ConfigureAwait(false);
            return;
        }

        var bose = new BoseApiClient(target.Host, Speakers.Http);
        var nowPlaying = await bose.GetNowPlayingAsync().ConfigureAwait(false);

        if (nowPlaying.IsStandby != on)
        {
            return;   // déjà dans l'état voulu : appuyer la ferait basculer à l'envers
        }

        await bose.PressKeyAsync(RemoteKey.Power).ConfigureAwait(false);
    }

    /// <summary>
    /// Retire du groupe l'enceinte qu'on s'apprête à mettre en veille.
    ///
    /// L'enceinte concernée est celle que l'utilisateur a DÉSIGNÉE, pas celle qui
    /// reçoit les commandes. Depuis que les commandes suivent le maître, les deux
    /// diffèrent dès qu'on regarde une suivante : la version précédente se croyait
    /// donc toujours maître, dissolvait le groupe entier et endormait le maître,
    /// pendant que la suivante visée restait allumée et muette.
    ///
    /// Deux cas : l'enceinte visée mène le groupe, ou le groupe tomberait à un seul
    /// membre — il n'a plus d'objet, on le défait ; sinon on ne retire qu'elle.
    /// L'échec n'est jamais bloquant : la veille reste l'action demandée.
    ///
    /// Dans les deux cas, seul le groupe QUI JOUE est touché. Le groupe enregistré
    /// par l'agent reste ce que l'utilisateur en a fait : une veille n'est pas une
    /// modification du groupe. Défaire le groupe vivant garde le groupe permanent
    /// en mémoire, et retirer une suivante passe par le firmware, pas par une
    /// republication de la zone.
    /// </summary>
    private async Task LeaveGroupBeforeStandbyAsync(SpeakerEndpoint target)
    {
        var device = Device;

        if (device is null || !(IsGroupActive || HasGroupVolume))
        {
            return;
        }

        var master = MasterIdentity(device);
        var members = CurrentZoneMembers(device);

        var targetIsMaster = string.Equals(target.Host, master.Ip, StringComparison.OrdinalIgnoreCase);

        try
        {
            if (targetIsMaster || members.Count <= 2)
            {
                if (device.Str is not null)
                {
                    await DissolveWithAgentAsync(device).ConfigureAwait(true);
                }
                else
                {
                    await device.Bose.SetZoneAsync(
                        master.DeviceId, master.Ip, Array.Empty<ZoneMember>()).ConfigureAwait(true);
                }
            }
            else
            {
                await RemoveFromLiveZoneAsync(device, target, master, members).ConfigureAwait(true);
            }

            await LoadZoneAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // On met quand même l'enceinte en veille : c'est ce qui a été demandé.
        }
    }

    /// <summary>
    /// Relit l'état après le sursis. Si l'enceinte est toujours dans l'état
    /// contraire, on le dit : mieux vaut un message franc qu'un bouton qui bascule
    /// tout seul sans explication.
    /// </summary>
    private async Task VerifyPowerAsync(bool wantedOn)
    {
        await Task.Delay(PowerGrace).ConfigureAwait(true);

        _powerRequestedAt = DateTime.MinValue;

        var device = Device;

        if (device is null)
        {
            return;
        }

        try
        {
            var nowPlaying = await device.GetNowPlayingAsync().ConfigureAwait(true);
            await ApplyNowPlayingAsync(nowPlaying).ConfigureAwait(true);

            // La vérification porte sur l'enceinte VISÉE. Elle n'est pas forcément
            // celle qui reçoit les commandes : interroger le maître dirait que tout
            // va bien alors que la suivante n'a pas obéi.
            await ApplySelectedStandbyAsync().ConfigureAwait(true);

            if (wantedOn && IsStandby)
            {
                IsError = true;
                StatusMessage = Localization.Get("S_PowerOnRefused");
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>
    /// Aligne le bouton de veille sur l'enceinte DÉSIGNÉE quand les commandes
    /// suivent le maître.
    ///
    /// Sans cela le bouton annoncerait l'état du maître pour une touche qui agit
    /// sur une autre enceinte : on éteint la suivante, le maître joue toujours, et
    /// le bouton propose de l'éteindre une seconde fois.
    ///
    /// Un seul appel, et seulement en situation de redirection — hors groupe, la
    /// lecture habituelle suffit et rien de plus n'est demandé au réseau.
    /// </summary>
    private async Task ApplySelectedStandbyAsync()
    {
        var target = Speakers.SelectedEndpoint;

        if (!Speakers.IsRedirected || target is null)
        {
            return;
        }

        // Pendant le sursis, l'intention prime sur ce que l'enceinte annonce.
        if (DateTime.UtcNow - _powerRequestedAt < PowerGrace)
        {
            return;
        }

        try
        {
            var bose = new BoseApiClient(target.Host, Speakers.Http);
            var state = await bose.GetNowPlayingAsync().ConfigureAwait(true);
            IsStandby = state.IsStandby;
        }
        catch (Exception)
        {
            // Injoignable : on garde l'affichage en place plutôt que d'inventer.
        }
    }

    [RelayCommand]
    private void VolumeStep(string? amount)
    {
        if (!int.TryParse(amount, out var step))
        {
            step = 1;
        }

        // On pose simplement la nouvelle valeur : OnVolumeChanged se charge de
        // l'envoyer au bon endroit — tout le groupe, ou la seule enceinte pilotée.
        // Envoyer directement à l'enceinte ici ferait diverger les deux chemins.
        Volume = Math.Clamp((int)Math.Round(Volume) + step, 0, 100);
    }

    [RelayCommand]
    private async Task BassStepAsync(string? amount)
    {
        if (!BassAvailable || !int.TryParse(amount, out var step))
        {
            return;
        }

        var target = (int)Math.Clamp(Math.Round(Bass) + step, BassMinimum, BassMaximum);

        _suppressBassPush = true;
        Bass = target;
        _suppressBassPush = false;

        await RunAsync(d => d.SetBassAsync(target));
    }

    /// <summary>Rappel depuis une tuile de la grille de touches.</summary>
    [RelayCommand]
    private Task RecallPresetAsync(PresetTile? tile)
    {
        if (tile is null || tile.IsEmpty)
        {
            return Task.CompletedTask;
        }

        // Retour immédiat : la tuile passe au vert avant même la réponse de
        // l'enceinte, sinon l'appui semble n'avoir aucun effet.
        _presetsViewModel.MarkActive(tile, optimistic: true);

        return RunAsync(d => d.RecallPresetAsync(tile.Slot), Localization.Get("S_PresetStarted", tile.Name));
    }

    /// <summary>
    /// Garde ce qui joue sur une touche, au choix de l'utilisateur.
    ///
    /// C'est l'agent qui décide de ce qu'il enregistre — la station, le dossier ou
    /// la piste de bibliothèque — d'après ce que l'enceinte joue réellement. La
    /// commande part donc vers l'enceinte qui reçoit les ordres : en groupe, le
    /// maître, puisque c'est lui qui joue.
    /// </summary>
    [RelayCommand]
    private async Task SavePlayingAsync()
    {
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
            StatusMessage = Localization.Get("S_NeedAgentKeep");
            return;
        }

        try
        {
            var slot = await _presetsViewModel.PickSlotAsync(Localization.Get("L_KeepPlayingTitle")).ConfigureAwait(true);

            if (slot is not { } key)
            {
                return;
            }

            var kept = await device.Str.SavePlayingAsync(key).ConfigureAwait(true);

            await _presetsViewModel.RevealSlotAsync(key).ConfigureAwait(true);

            var name = FirstNonEmpty(kept.Name, Title) ?? "";

            ShowInfo(Localization.Get(kept.Unchanged ? "S_KeptAlready" : "S_Kept", name, PresetKeys.Label(key)));
        }
        catch (SpeakerException ex) when (ex.StatusCode is { } code && (int)code == 422)
        {
            // Veille, AUX, Bluetooth, Spotify : rien que l'agent sache garder.
            IsError = true;
            StatusMessage = Localization.Get("S_NothingToKeep");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>
    /// Le bouton « Rafraîchir » : relit tout, y compris la pochette et la grille
    /// des touches, puis confirme. Sans ce retour visible, l'appui donnait
    /// l'impression de ne rien faire.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        // On oublie l'identité de la pochette affichée pour forcer sa relecture.
        _artKey = "";

        if (_presetsViewModel.RefreshCommand.CanExecute(null))
        {
            await _presetsViewModel.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);

        if (!IsError)
        {
            ShowInfo(Localization.Get("S_Refreshed"));
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var device = Device;

        if (device is null)
        {
            SpeakerName = Localization.Get("L_NoSpeakerTitle");
            SpeakerDetail = Localization.Get("L_NoSpeakerDetail");
            HasStrAgent = false;
            IsLive = false;
            IsGroupActive = false;
            LiveLabel = Localization.Get("L_Polling");
            await ApplyNowPlayingAsync(NowPlaying.Standby).ConfigureAwait(true);
            return;
        }

        SpeakerName = device.Endpoint.DisplayName;
        SpeakerDetail = device.Endpoint.Summary;
        HasStrAgent = device.HasStr;
        SetLive(device.Notifications.IsConnected);

        BassAvailable = device.BassCapabilities.Available;
        BassMinimum = device.BassCapabilities.Min;
        BassMaximum = device.BassCapabilities.Max;

        IsBusy = true;

        try
        {
            var nowPlaying = await device.GetNowPlayingAsync().ConfigureAwait(true);
            await ApplyNowPlayingAsync(nowPlaying).ConfigureAwait(true);

            var volume = await device.GetVolumeAsync().ConfigureAwait(true);
            ApplyVolume(volume);

            if (BassAvailable)
            {
                var bass = await device.GetBassAsync().ConfigureAwait(true);
                ApplyBass(bass);
            }

            await LoadZoneAsync().ConfigureAwait(true);
            await ApplySelectedStandbyAsync().ConfigureAwait(true);

            ShowInfo(null);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ================================================================ Zone

    /// <summary>
    /// Relit la composition du groupe et, avec l'agent, le volume de chaque membre.
    /// Sans agent on sait seulement qui suit qui : le firmware ne publie pas les
    /// volumes des autres enceintes.
    /// </summary>
    private async Task LoadZoneAsync()
    {
        var device = Device;

        if (device is null)
        {
            IsGroupActive = false;
            HasGroupVolume = false;
            GroupMembers.Clear();
            _zoneMaster = null;
            GroupMasterName = "";
            IsMasterSpeaker = true;
            return;
        }

        var memberIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Sert à détecter la dissolution du groupe : le curseur du haut doit alors
        // revenir au volume propre de l'enceinte pilotée.
        var wasGroupVolume = HasGroupVolume;

        _locatedMaster = null;

        try
        {
            if (device.Str is not null)
            {
                var state = await device.Str.GetZoneVolumeAsync().ConfigureAwait(true);

                if (state is not null)
                {
                    IsGroupActive = state.Grouped;
                    HasGroupVolume = state.Grouped;

                    var members = state.Members
                        .Where(m => !string.IsNullOrWhiteSpace(m.Ip))
                        .ToList();

                    foreach (var member in members)
                    {
                        memberIps.Add(member.Ip!);
                    }

                    // Si la composition n'a pas bougé, on se contente de remettre les
                    // valeurs à jour : reconstruire la liste ferait sauter un curseur
                    // que l'utilisateur est peut-être en train de faire glisser.
                    var sameMembers = GroupMembers.Count == members.Count &&
                        GroupMembers.Zip(members).All(pair =>
                            string.Equals(pair.First.Ip, pair.Second.Ip, StringComparison.OrdinalIgnoreCase));

                    if (sameMembers)
                    {
                        foreach (var (row, member) in GroupMembers.Zip(members))
                        {
                            row.IsReachable = member.IsReachable;
                            row.SetQuietly(Math.Clamp(member.Volume, 0, 100));
                        }
                    }
                    else
                    {
                        GroupMembers.Clear();

                        foreach (var member in members)
                        {
                            var row = new MemberVolume
                            {
                                Ip = member.Ip!,
                                Name = member.DisplayName,
                                IsMaster = member.IsMaster,
                                DeviceId = member.DeviceId ?? "",
                                IsReachable = member.IsReachable,
                            };

                            row.SetQuietly(Math.Clamp(member.Volume, 0, 100));
                            row.Push = PushMemberVolume;
                            GroupMembers.Add(row);
                        }
                    }

                    // Le curseur du haut montre la moyenne du groupe, sauf si
                    // l'utilisateur vient tout juste d'y toucher. Hors groupe, la
                    // moyenne renvoyée par l'agent ne veut rien dire (elle vaut 0
                    // quand plus aucune zone n'existe) : on n'y touche pas.
                    if (state.Grouped && DateTime.UtcNow - _volumeTouchedAt >= EchoSuppression)
                    {
                        _suppressVolumePush = true;
                        Volume = Math.Clamp(state.Average, 0, 100);
                        _suppressVolumePush = false;

                        CaptureGroupBaseline();
                    }
                }
            }
            else
            {
                var zone = await device.GetZoneAsync().ConfigureAwait(true);
                IsGroupActive = zone.Members.Count > 1;
                HasGroupVolume = false;
                GroupMembers.Clear();

                foreach (var member in zone.Members)
                {
                    memberIps.Add(member.IpAddress);
                }
            }
        }
        catch (Exception)
        {
            // Une zone illisible ne doit pas casser la page : on la considère absente.
            IsGroupActive = false;
            HasGroupVolume = false;
            GroupMembers.Clear();
        }

        // L'agent d'une enceinte qui SUIT un groupe ne le décrit pas toujours : il
        // voit la zone depuis le maître, et le firmware d'une esclave n'est pas plus
        // bavard selon le modèle. On va donc chercher le groupe là où il est connu à
        // coup sûr — chez le maître. Sans ce recours, une enceinte esclave paraissait
        // libre : l'écran proposait de former un groupe où elle se trouvait déjà.
        if (!IsGroupActive)
        {
            var snapshot = await _zones.LocateAsync(device.Host, Speakers.KnownSpeakers).ConfigureAwait(true);

            if (snapshot.Grouped)
            {
                IsGroupActive = true;

                foreach (var ip in snapshot.MemberIps)
                {
                    memberIps.Add(ip);
                }

                _locatedMaster = (snapshot.MasterName, snapshot.MasterIp);
            }
        }

        foreach (var candidate in GroupCandidates)
        {
            candidate.IsGrouped = memberIps.Contains(candidate.Host);
        }

        // Vidage AVANT de désigner le maître : sans groupe, il n'y a pas de maître
        // à désigner. L'agent continue parfois d'annoncer un membre unique marqué
        // « master » juste après une dissolution ; le lire ici laisserait la mention
        // « ce n'est pas le maître » à l'écran alors que le groupe n'existe plus.
        if (!IsGroupActive)
        {
            GroupMembers.Clear();
        }

        _zoneMaster = IsGroupActive ? GroupMembers.FirstOrDefault(m => m.IsMaster) : null;

        if (_zoneMaster is not null)
        {
            GroupMasterName = _zoneMaster.Name;
            IsMasterSpeaker = Device is null ||
                string.Equals(_zoneMaster.Ip, Device.Host, StringComparison.OrdinalIgnoreCase);
        }
        else if (IsGroupActive && _locatedMaster is not null)
        {
            GroupMasterName = _locatedMaster.Value.Name;
            IsMasterSpeaker = Device is null ||
                string.Equals(_locatedMaster.Value.Ip, Device.Host, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            GroupMasterName = Device?.Endpoint.DisplayName ?? "";
            IsMasterSpeaker = true;
        }

        if (wasGroupVolume && !HasGroupVolume)
        {
            await ReloadOwnVolumeAsync().ConfigureAwait(true);
        }

        IsFollowingMaster = Speakers.IsRedirected;

        await FollowMasterAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Fait suivre les commandes au maître du groupe.
    ///
    /// Dans un groupe, tout est joué par le maître : ce qu'on entend, les touches de
    /// transport, les présélections, le volume d'ensemble. Commander une enceinte qui
    /// ne fait que suivre n'a donc pas de sens — et lui demander son volume propre
    /// faisait disparaître le réglage de groupe dès qu'on la regardait.
    ///
    /// Deux moments seulement : quand l'enceinte commandée s'avère être une suivante,
    /// et quand le groupe a disparu alors que les commandes partaient encore ailleurs.
    /// Hors de ces deux cas, rien n'est relu et rien ne coûte.
    /// </summary>
    private async Task FollowMasterAsync()
    {
        var needsRedirect = !IsMasterSpeaker;
        var needsReturn = Speakers.IsRedirected && !IsGroupActive;

        if (!needsRedirect && !needsReturn)
        {
            return;
        }

        try
        {
            // Un changement d'enceinte commandée déclenche DeviceChanged, donc une
            // relecture complète : on ne poursuit pas celle-ci.
            await Speakers.RetargetAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Maître injoignable : on garde l'enceinte en place plutôt que de
            // laisser l'écran sans rien.
        }
    }

    /// <summary>
    /// Après la dissolution du groupe, le curseur du haut redevient celui de
    /// l'enceinte pilotée : on relit sa valeur propre plutôt que de laisser la
    /// dernière moyenne du groupe — ou le 0 % renvoyé quand la zone a disparu.
    /// </summary>
    private async Task ReloadOwnVolumeAsync()
    {
        var device = Device;

        if (device is null)
        {
            return;
        }

        try
        {
            var state = await device.GetVolumeAsync().ConfigureAwait(true);

            IsMuted = state.IsMuted;

            // On force la valeur sans tenir compte du dernier geste : ce geste
            // portait sur le volume du groupe, qui n'existe plus.
            _volumeTouchedAt = DateTime.MinValue;

            _suppressVolumePush = true;
            Volume = Math.Clamp(state.Actual, 0, 100);
            _suppressVolumePush = false;

            CaptureGroupBaseline();
        }
        catch (Exception)
        {
            // Enceinte injoignable : mieux vaut garder l'affichage en place que
            // de le remettre à zéro.
        }
    }

    // ================================================================ Minuterie de sommeil

    /// <summary>Durées proposées, en minutes.</summary>
    private static readonly int[] SleepChoices = { 15, 30, 45, 60, 90, 120 };

    /// <summary>Relecture auprès de l'agent pendant qu'une minuterie tourne.</summary>
    private static readonly TimeSpan SleepPollInterval = TimeSpan.FromSeconds(60);

    /// <summary>Hors minuterie : juste de quoi voir celle qu'un autre appareil aurait armée.</summary>
    private static readonly TimeSpan IdleSleepPollInterval = TimeSpan.FromSeconds(120);

    private DateTime _sleepReadAt = DateTime.MinValue;
    private DateTime _sleepEndsAt = DateTime.MinValue;
    private bool _sleepForGroup;

    /// <summary>Enceinte dont la minuterie est affichée.</summary>
    private string? _sleepHost;

    /// <summary>Vrai quand l'agent de l'enceinte désignée connaît la minuterie.</summary>
    [ObservableProperty]
    private bool _sleepAvailable;

    [ObservableProperty]
    private bool _sleepActive;

    /// <summary>« Minuterie », ou « Veille dans 42 min » quand elle tourne.</summary>
    [ObservableProperty]
    private string _sleepLabel = "";

    /// <summary>
    /// L'agent qui porte la minuterie : celui de l'enceinte DÉSIGNÉE.
    ///
    /// Comme la veille, la minuterie ne suit pas le maître du groupe : elle éteint
    /// l'enceinte que l'utilisateur regarde. Null quand cette enceinte n'a pas d'agent.
    /// </summary>
    private StrApiClient? SleepAgent()
    {
        var device = Device;

        if (device is null)
        {
            return null;
        }

        var target = Speakers.SelectedEndpoint ?? device.Endpoint;

        if (string.Equals(target.Host, device.Host, StringComparison.OrdinalIgnoreCase))
        {
            return device.Str;
        }

        return target.StrPort is int port ? new StrApiClient(target.Host, port, Speakers.Http) : null;
    }

    private bool IsSleepHost(StrApiClient agent)
        => string.Equals(_sleepHost, agent.Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Relit la minuterie auprès de l'agent. Elle peut avoir été armée ou annulée
    /// ailleurs — l'application de bureau, un autre téléphone — et elle disparaît
    /// quand l'agent redémarre.
    /// </summary>
    private async Task RefreshSleepAsync()
    {
        var agent = SleepAgent();

        _sleepReadAt = DateTime.UtcNow;

        if (agent is null)
        {
            _sleepHost = null;
            SleepAvailable = false;
            ApplySleep(null);
            return;
        }

        if (!IsSleepHost(agent))
        {
            // Autre enceinte : la minuterie affichée n'est pas la sienne.
            _sleepHost = agent.Host;
            ApplySleep(null);
        }

        try
        {
            var state = await agent.GetSleepAsync().ConfigureAwait(true);

            if (!IsSleepHost(agent))
            {
                return;   // l'utilisateur a changé d'enceinte pendant la lecture
            }

            SleepAvailable = true;
            ApplySleep(state);
        }
        catch (SpeakerException ex) when (ex.StatusCode is { } code && (int)code == 404)
        {
            // Agent antérieur à la minuterie : le bouton n'a pas lieu d'être.
            if (IsSleepHost(agent))
            {
                SleepAvailable = false;
                ApplySleep(null);
            }
        }
        catch (Exception)
        {
            // Injoignable cette fois-ci : on garde l'affichage, le décompte continue.
        }
    }

    private void ApplySleep(StrSleepState? state)
    {
        if (state is { Active: true, RemainingSec: > 0 })
        {
            SleepActive = true;
            _sleepForGroup = state.Group;
            _sleepEndsAt = DateTime.UtcNow.AddSeconds(state.RemainingSec);
        }
        else
        {
            SleepActive = false;
            _sleepForGroup = false;
            _sleepEndsAt = DateTime.MinValue;
        }

        UpdateSleepLabel();
    }

    /// <summary>Le décompte se fait sur l'horloge du téléphone, entre deux relectures.</summary>
    private void UpdateSleepLabel()
    {
        if (SleepActive)
        {
            var remaining = _sleepEndsAt - DateTime.UtcNow;

            if (remaining > TimeSpan.Zero)
            {
                var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
                SleepLabel = Localization.Get(_sleepForGroup ? "L_SleepInGroup" : "L_SleepIn", minutes);
                return;
            }

            // Échéance passée : l'agent dira ce qu'il en est à la prochaine relecture.
            SleepActive = false;
            _sleepForGroup = false;
            _sleepReadAt = DateTime.MinValue;
        }

        SleepLabel = Localization.Get("L_Sleep");
    }

    /// <summary>Appelé par le minuteur de la page : décompte, et relecture quand elle est due.</summary>
    private void TickSleep()
    {
        if (SleepActive)
        {
            UpdateSleepLabel();
        }

        var due = SleepActive ? SleepPollInterval : IdleSleepPollInterval;

        if (DateTime.UtcNow - _sleepReadAt >= due)
        {
            _ = RefreshSleepAsync();
        }
    }

    /// <summary>
    /// Le bouton « Minuterie » : choisir une durée, ou annuler celle qui tourne.
    /// En groupe, on demande s'il faut éteindre tout le groupe ou cette seule enceinte.
    /// </summary>
    [RelayCommand]
    private async Task SleepAsync()
    {
        var agent = SleepAgent();

        if (agent is null)
        {
            IsError = true;
            StatusMessage = Localization.Get(Device is null ? "S_NoSpeaker" : "S_NeedAgentSleep");
            return;
        }

        try
        {
            var cancel = Localization.Get("L_Cancel");
            var stop = Localization.Get("L_SleepCancel");
            var durations = SleepChoices.Select(m => Localization.Get("L_Minutes", m)).ToArray();

            var choice = await Shell.Current.DisplayActionSheetAsync(
                Localization.Get("L_SleepTitle"),
                cancel,
                SleepActive ? stop : null,
                durations).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(choice) || choice == cancel)
            {
                return;
            }

            if (choice == stop)
            {
                await agent.CancelSleepAsync().ConfigureAwait(true);
                _sleepReadAt = DateTime.UtcNow;
                ApplySleep(null);
                ShowInfo(Localization.Get("S_SleepCancelled"));
                return;
            }

            var index = Array.IndexOf(durations, choice);

            if (index < 0)
            {
                return;
            }

            var minutes = SleepChoices[index];
            var name = (Speakers.SelectedEndpoint ?? Device?.Endpoint)?.DisplayName ?? "";
            var wholeGroup = false;

            if (IsGroupActive || HasGroupVolume)
            {
                var all = Localization.Get("L_SleepWholeGroup");
                var only = Localization.Get("L_SleepOnlyThis", name);

                var scope = await Shell.Current.DisplayActionSheetAsync(
                    Localization.Get("L_SleepScope"),
                    cancel,
                    null,
                    all,
                    only).ConfigureAwait(true);

                if (scope == all)
                {
                    wholeGroup = true;
                }
                else if (scope != only)
                {
                    return;
                }
            }

            await agent.SetSleepAsync(minutes, wholeGroup).ConfigureAwait(true);

            _sleepHost = agent.Host;
            _sleepReadAt = DateTime.UtcNow;
            SleepAvailable = true;
            ApplySleep(new StrSleepState { Active = true, RemainingSec = minutes * 60, Group = wholeGroup });

            ShowInfo(wholeGroup
                ? Localization.Get("S_SleepSetGroup", minutes)
                : Localization.Get("S_SleepSet", name, minutes));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    // ================================================================ Cycle de vie

    public void OnAppearing()
    {
        _pollTimer ??= CreatePollTimer();
        _pollTimer.Start();

        ReloadSpeakerList();
        _ = RefreshAsync();
        _ = RefreshSleepAsync();

        // La grille de cette page partage la collection de l'onglet dédié :
        // on la remplit sans attendre que l'utilisateur y aille.
        if (_presetsViewModel.RefreshCommand.CanExecute(null))
        {
            _presetsViewModel.RefreshCommand.Execute(null);
        }

        // Les enceintes ajoutées avant que la lecture de version n'existe n'ont
        // rien d'enregistré : on complète en tâche de fond.
        _ = FillAgentVersionsAsync();
    }

    public void OnDisappearing() => _pollTimer?.Stop();

    /// <summary>
    /// Le compte rendu d'un raccourci de l'icône (« Tout mettre en veille »,
    /// « Touche 1 »…), montré dans cet onglet, sur lequel l'application s'ouvre.
    /// Un message vide ne change rien. L'état de lecture suit par la relecture
    /// régulière de l'onglet.
    /// </summary>
    public void ShowShortcutResult(string message, bool isError)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (isError)
        {
            IsError = true;
            StatusMessage = message;
        }
        else
        {
            ShowInfo(message);
        }
    }

    /// <summary>
    /// « v1.6.3 » ; sous la version minimale, « v1.5.2 · trop ancien » ; plus ancien
    /// que la dernière version publiée (lue par les Réglages), « v1.8.3 · 1.9.1 dispo ».
    /// </summary>
    private static string AgentLabel(string version)
    {
        if (AgentRequirement.IsTooOld(version))
        {
            return $"{version} · {Localization.Get("L_AgentTooOldShort")}";
        }

        return AgentUpdateViewModel.NewerThan(version) is { } newer
            ? $"{version} · {Localization.Get("L_AgentNewShort", newer.Label)}"
            : version;
    }

    private async Task FillAgentVersionsAsync()
    {
        try
        {
            if (await Speakers.EnsureAgentVersionsAsync().ConfigureAwait(true))
            {
                ReloadSpeakerList();
                await LoadZoneAsync().ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            // Sans conséquence : la version restera vide jusqu'au prochain passage.
        }
    }

    private IDispatcherTimer CreatePollTimer()
    {
        var timer = Application.Current!.Dispatcher.CreateTimer();
        timer.Interval = PollInterval;

        timer.Tick += (_, _) =>
        {
            var device = Device;

            if (device is null)
            {
                return;
            }

            SetLive(device.Notifications.IsConnected);

            // La minuterie de sommeil n'est annoncée par personne : décompte local,
            // et relecture de loin en loin.
            TickSleep();

            // Le WebSocket est la source normale ; ce rafraîchissement ne sert
            // que lorsqu'il est tombé, pour ne pas matraquer le firmware.
            if (!device.Notifications.IsConnected)
            {
                _ = RefreshAsync();
                return;
            }

            // La zone fait exception : elle change sans que personne ne l'annonce,
            // et elle peut même se former sans nous. On la relit donc dans les deux
            // cas, simplement plus souvent quand un groupe est déjà connu.
            // Touche Spotify ou radio : le morceau change sans notification, on le relit.
            if (LiveKindOf(_lastNowPlaying) != LiveKind.None && DateTime.UtcNow - _liveSongAt >= LiveSongInterval)
            {
                _liveSongAt = DateTime.UtcNow;
                _ = RefreshLiveSongAsync();
            }

            var due = IsGroupActive || HasGroupVolume ? ZonePollInterval : IdleZonePollInterval;

            if (DateTime.UtcNow - _zonePolledAt >= due)
            {
                _zonePolledAt = DateTime.UtcNow;
                _ = LoadZoneAsync();
            }
        };

        return timer;
    }

    private void Subscribe(SpeakerDevice? device)
    {
        if (ReferenceEquals(_subscribedTo, device))
        {
            return;
        }

        if (_subscribedTo is not null)
        {
            _subscribedTo.Notifications.VolumeChanged -= OnVolumeNotification;
            _subscribedTo.Notifications.VolumeStale -= OnVolumeStaleNotification;
            _subscribedTo.Notifications.NowPlayingChanged -= OnNowPlayingNotification;
            _subscribedTo.Notifications.ConnectionChanged -= OnConnectionNotification;
            _subscribedTo.Notifications.ZoneChanged -= OnZoneNotification;
        }

        _subscribedTo = device;

        if (device is not null)
        {
            device.Notifications.VolumeChanged += OnVolumeNotification;
            device.Notifications.VolumeStale += OnVolumeStaleNotification;
            device.Notifications.NowPlayingChanged += OnNowPlayingNotification;
            device.Notifications.ConnectionChanged += OnConnectionNotification;
            device.Notifications.ZoneChanged += OnZoneNotification;
        }

        OnPropertyChanged(nameof(HasNoSpeaker));
    }

    private void OnVolumeNotification(object? sender, VolumeState state) => OnMainThread(() => ApplyVolume(state));

    /// <summary>
    /// Trame volumeUpdated sans contenu : l'enceinte signale le changement sans le
    /// décrire. On relit la valeur au lieu de l'ignorer.
    /// </summary>
    private void OnVolumeStaleNotification(object? sender, EventArgs e) => OnMainThread(() => _ = RereadVolumeAsync());

    private async Task RereadVolumeAsync()
    {
        var device = Device;

        if (device is null)
        {
            return;
        }

        try
        {
            ApplyVolume(await device.GetVolumeAsync().ConfigureAwait(true));
        }
        catch (Exception)
        {
            // Le sondage périodique repassera.
        }
    }

    private void OnNowPlayingNotification(object? sender, NowPlaying nowPlaying)
        => OnMainThread(() => _ = ApplyNowPlayingAsync(nowPlaying));

    private void OnZoneNotification(object? sender, EventArgs e) => OnMainThread(() => _ = LoadZoneAsync());

    private void OnConnectionNotification(object? sender, bool connected) => OnMainThread(() =>
    {
        SetLive(connected);

        if (connected)
        {
            _ = RefreshAsync();
        }
    });

    // ================================================================ Application de l'état

    private void SetLive(bool live)
    {
        IsLive = live;
        LiveLabel = Localization.Get(live ? "L_Live" : "L_Polling");
    }

    private void ApplyLanguage()
    {
        SetLive(IsLive);
        OnPropertyChanged(nameof(PowerLabel));
        OnPropertyChanged(nameof(GroupedWithLabel));
        UpdateSleepLabel();
        _artKey = "";
        _ = ApplyNowPlayingAsync(_lastNowPlaying);

        if (Device is null)
        {
            SpeakerName = Localization.Get("L_NoSpeakerTitle");
            SpeakerDetail = Localization.Get("L_NoSpeakerDetail");
        }
    }

    private async Task ApplyNowPlayingAsync(NowPlaying nowPlaying)
    {
        _lastNowPlaying = nowPlaying;
        Like.Update(Device, nowPlaying);

        ShowRegularTitle(nowPlaying);
        ArtGlyph = GlyphFor(nowPlaying.Source);
        IsPlaying = nowPlaying.IsPlaying;
        // Pendant le sursis, l'état demandé prime sur celui que l'enceinte annonce
        // encore : elle n'a pas fini de se réveiller, elle ne ment pas.
        var pendingPower = DateTime.UtcNow - _powerRequestedAt < PowerGrace;

        IsStandby = pendingPower ? !_powerRequestedOn : nowPlaying.IsIdle;

        // La touche correspondant à ce qui joue est mise en avant ; si rien ne
        // correspond — Spotify lancé depuis le téléphone, par exemple — plus aucune
        // touche n'est verte.
        _presetsViewModel.SyncActive(
            nowPlaying.Source,
            FirstNonEmpty(nowPlaying.StationName, nowPlaying.Content?.ItemName),
            nowPlaying.Content?.Location,
            nowPlaying.IsIdle);

        await ApplyLiveSongAsync(nowPlaying, force: false).ConfigureAwait(true);
        await LoadArtAsync(nowPlaying).ConfigureAwait(true);
    }

    /// <summary>Titre, sous-titre et source tels que l'enceinte les annonce.</summary>
    private void ShowRegularTitle(NowPlaying nowPlaying)
    {
        Title = nowPlaying.IsIdle && string.IsNullOrWhiteSpace(nowPlaying.Track)
            ? Localization.Get(nowPlaying.IsStandby ? "L_Standby" : "L_NothingPlaying")
            : nowPlaying.DisplayTitle;

        Subtitle = nowPlaying.DisplaySubtitle;
        SourceLabel = nowPlaying.IsIdle ? Localization.Get("L_Standby") : FriendlySource(nowPlaying.Source);
    }

    /// <summary>Ce dont l'agent peut donner le titre en direct.</summary>
    private enum LiveKind
    {
        None,
        SpotifyKey,
        Radio,
    }

    /// <summary>Sources de radio internet : la station annonce le morceau dans le flux.</summary>
    private static readonly HashSet<string> RadioSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "LOCAL_INTERNET_RADIO", "INTERNET_RADIO", "TUNEIN",
    };

    private static LiveKind LiveKindOf(NowPlaying nowPlaying)
    {
        if (IsSpotifyKey(nowPlaying))
        {
            return LiveKind.SpotifyKey;
        }

        return !nowPlaying.IsIdle && RadioSources.Contains(nowPlaying.Source)
            ? LiveKind.Radio
            : LiveKind.None;
    }

    /// <summary>
    /// Touche Spotify : l'enceinte lit le flux Spotify relayé par l'agent
    /// (« /spotify/ » dans l'adresse) et ne connaît que le nom de la playlist.
    /// Spotify Connect, lui, annonce déjà le morceau : il n'est pas concerné.
    /// </summary>
    private static bool IsSpotifyKey(NowPlaying nowPlaying)
        => !nowPlaying.IsIdle &&
           !string.Equals(nowPlaying.Source, "SPOTIFY", StringComparison.OrdinalIgnoreCase) &&
           nowPlaying.Content?.Location?.Contains("/spotify/", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Relit le titre en direct, sans attendre la réutilisation. Appelée par le
    /// sondage périodique tant qu'une touche Spotify ou une radio joue.
    /// </summary>
    public async Task RefreshLiveSongAsync()
    {
        var nowPlaying = _lastNowPlaying;

        if (LiveKindOf(nowPlaying) == LiveKind.None)
        {
            return;
        }

        await ApplyLiveSongAsync(nowPlaying, force: true).ConfigureAwait(true);
        await LoadArtAsync(nowPlaying).ConfigureAwait(true);
    }

    /// <summary>
    /// Met le morceau en cours en titre et l'artiste en sous-titre :
    ///
    ///   – touche Spotify (agent 1.8.2) : « Spotify · playlist » en source ;
    ///   – radio : le titre que la station envoie, « Bel RTL · Radio Internet » en
    ///     source. La station qui n'envoie rien garde l'affichage habituel.
    ///
    /// Un agent qui ne donne pas le titre, ou qui ne répond pas, laisse le titre
    /// habituel, sans message d'erreur : c'est un complément, pas une commande.
    /// </summary>
    private async Task ApplyLiveSongAsync(NowPlaying nowPlaying, bool force)
    {
        var device = Device;
        var kind = LiveKindOf(nowPlaying);

        if (kind == LiveKind.None || device is not { Str: { } str } || device.AgentTooOld)
        {
            // Rien dont l'agent donne le titre. Le dernier titre lu reste à ignorer
            // s'il revient à la prochaine station : l'agent peut le garder un moment.
            if (_lastShownTitle.Length > 0)
            {
                _staleTitle = _lastShownTitle;
                _staleUntil = DateTime.UtcNow + StaleTitleWindow;
            }

            _liveSong = null;
            _liveSongFor = "";
            _lastShownTitle = "";
            return;
        }

        var location = nowPlaying.Content?.Location ?? "";

        // Un autre flux, ou le même flux décrit autrement : le titre connu ne vaut
        // plus, et celui de l'ancien flux doit être ignoré s'il revient aussitôt.
        if (!string.Equals(location, _liveSongFor, StringComparison.Ordinal) || kind != _liveSongKind)
        {
            if (_liveSongFor.Length > 0 && _lastShownTitle.Length > 0)
            {
                _staleTitle = _lastShownTitle;
                _staleUntil = DateTime.UtcNow + StaleTitleWindow;
            }

            _liveSong = null;
            _liveSongFor = location;
            _liveSongKind = kind;
            _lastShownTitle = "";
        }

        // Le morceau déjà connu s'affiche tout de suite : pas de clignotement
        // entre la station et le morceau à chaque notification.
        ShowLiveSong(nowPlaying);

        if (!force && _liveSong is not null && DateTime.UtcNow - _liveSongAt < LiveSongReuse)
        {
            return;
        }

        StrStreamTitle? song;

        try
        {
            song = await str.GetStreamTitleAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            song = null;
        }

        // Une réponse tardive ne vaut plus rien si la lecture ou l'enceinte a changé.
        if (!ReferenceEquals(nowPlaying, _lastNowPlaying) || !ReferenceEquals(device, Device))
        {
            return;
        }

        var hadSong = _liveSong is not null;
        var rawTitle = (song?.Title ?? "").Trim();

        _liveSongFor = location;
        _liveSongKind = kind;
        _liveSongAt = DateTime.UtcNow;
        _liveSong = song is not null && !IsStaleTitle(rawTitle) && HasLiveSong(kind, song, nowPlaying) ? song : null;
        _lastShownTitle = _liveSong is null ? "" : rawTitle;

        if (_liveSong is null)
        {
            ShowRegularTitle(nowPlaying);

            // Le morceau Spotify a disparu alors que l'enceinte annonçait encore la
            // touche : elle est sans doute passée à autre chose (une radio) sans que
            // sa notification soit arrivée. On relit son état plutôt que d'afficher
            // le nom de la playlist en attendant. Une radio, elle, se tait souvent
            // entre deux morceaux (publicité, annonce) : rien à relire.
            if (hadSong && kind == LiveKind.SpotifyKey)
            {
                _ = RefreshAsync();
            }
        }
        else
        {
            ShowLiveSong(nowPlaying);
        }
    }

    /// <summary>
    /// Vrai pour le titre de la station précédente que l'agent renvoie encore. Dès
    /// qu'un autre titre arrive (ou que le délai est passé), plus rien n'est ignoré.
    /// </summary>
    private bool IsStaleTitle(string rawTitle)
    {
        if (_staleTitle.Length == 0)
        {
            return false;
        }

        if (DateTime.UtcNow < _staleUntil && string.Equals(rawTitle, _staleTitle, StringComparison.Ordinal))
        {
            return true;
        }

        _staleTitle = "";
        return false;
    }

    private static bool HasLiveSong(LiveKind kind, StrStreamTitle song, NowPlaying nowPlaying) => kind switch
    {
        LiveKind.SpotifyKey => song.HasSong,

        // Une radio ne donne jamais les champs Spotify ; s'ils sont là, l'agent
        // parle encore de la touche Spotify qu'on vient de quitter.
        LiveKind.Radio => !song.HasSong && song.RadioSongFor(StationOf(nowPlaying)) is not null,
        _ => false,
    };

    private void ShowLiveSong(NowPlaying nowPlaying)
    {
        if (_liveSong is not { } song)
        {
            return;
        }

        if (_liveSongKind == LiveKind.SpotifyKey && song.HasSong)
        {
            var playlist = nowPlaying.DisplayTitle;
            Title = song.Track!;
            Subtitle = song.Artist ?? "";
            SourceLabel = string.IsNullOrWhiteSpace(playlist) ? "Spotify" : $"Spotify · {playlist}";
            return;
        }

        if (_liveSongKind == LiveKind.Radio && song.RadioSongFor(StationOf(nowPlaying)) is { } radio)
        {
            var station = StationOf(nowPlaying);
            var source = FriendlySource(nowPlaying.Source);
            Title = radio.Song;
            Subtitle = radio.Artist ?? "";
            SourceLabel = string.IsNullOrWhiteSpace(station) ? source : $"{station} · {source}";
        }
    }

    /// <summary>Le nom de la station qui joue, tel que l'enceinte l'annonce.</summary>
    private static string? StationOf(NowPlaying nowPlaying)
        => FirstNonEmpty(nowPlaying.StationName, nowPlaying.Content?.ItemName);

    /// <summary>
    /// Charge la pochette de ce qui joue.
    ///
    /// L'identité retenue est la STATION, pas le titre : sur une radio, le morceau
    /// change toutes les trois minutes alors que le logo ne bouge pas. Quand cette
    /// identité change, l'ancienne image est effacée AVANT d'essayer la nouvelle —
    /// sinon le logo de la station précédente restait affiché, ce qui est pire que
    /// pas d'image du tout.
    ///
    /// Si la source ne fournit rien d'exploitable, on va chercher le logo de la
    /// touche qui porte le même nom : c'est presque toujours la même station, et
    /// son image est déjà sur le téléphone.
    /// </summary>
    private async Task LoadArtAsync(NowPlaying nowPlaying)
    {
        var station = FirstNonEmpty(nowPlaying.StationName, nowPlaying.Content?.ItemName);

        // Touche Spotify : la pochette du morceau en cours passe avant tout le reste,
        // et elle fait partie de l'identité — elle change à chaque morceau.
        var songArt = IsSpotifyKey(nowPlaying) ? _liveSong?.Art : null;

        var key = string.Join(
            '|',
            nowPlaying.Source,
            nowPlaying.Content?.Location ?? "",
            station ?? "",
            songArt ?? "");

        if (string.Equals(key, _artKey, StringComparison.Ordinal) && HasArt)
        {
            return;
        }

        _artKey = key;

        // La station a changé : on repart d'une vignette vide.
        ArtSource = null;
        HasArt = false;

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(songArt))
        {
            candidates.Add(songArt);
        }

        foreach (var candidate in ArtworkCache.SplitCandidates(nowPlaying.ArtUrl))
        {
            candidates.Add(candidate);
        }

        if (!string.IsNullOrWhiteSpace(station))
        {
            var tile = Presets.FirstOrDefault(t =>
                !t.IsEmpty && string.Equals(t.Name, station, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(tile?.ArtUrl))
            {
                candidates.Add(tile!.ArtUrl!);
            }

            if (tile is null)
            {
                // La station peut être rangée sur une touche de la rangée qui
                // n'est pas à l'écran : son logo est connu quand même.
                var stored = _presetsViewModel.ArtOfStoredStation(station);

                if (stored is not null)
                {
                    candidates.Add(stored);
                }
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var (source, _) = await _artwork.GetFromChainAsync(string.Join('|', candidates)).ConfigureAwait(true);

        if (source is null)
        {
            return;
        }

        // Une réponse tardive ne doit pas coller le logo d'une station déjà quittée.
        if (!string.Equals(key, _artKey, StringComparison.Ordinal))
        {
            return;
        }

        ArtSource = source;
        HasArt = true;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private void ApplyVolume(VolumeState state)
    {
        IsMuted = state.IsMuted;

        // En groupe, le curseur du haut affiche la moyenne du groupe : le volume
        // propre à l'enceinte pilotée ne doit pas venir l'écraser.
        if (HasGroupVolume)
        {
            return;
        }

        // Un écho de notre propre réglage arriverait après coup et ferait sauter
        // le curseur sous le doigt : on l'ignore le temps que l'enceinte se cale.
        if (DateTime.UtcNow - _volumeTouchedAt < EchoSuppression)
        {
            return;
        }

        _suppressVolumePush = true;
        Volume = state.Actual;
        _suppressVolumePush = false;
    }

    private void ApplyBass(BassState state)
    {
        _suppressBassPush = true;
        Bass = state.Actual;
        _suppressBassPush = false;
    }

    /// <summary>
    /// Repli quand la source ne fournit pas de pochette. Des caractères de texte,
    /// pas des émojis : ces derniers sont rendus par la police emoji du système,
    /// en couleur et différemment d'un appareil à l'autre.
    /// </summary>
    private static string GlyphFor(string source) => source.ToUpperInvariant() switch
    {
        "STANDBY" or "INVALID_SOURCE" => "○",
        _ => "♪",
    };

    private static string FriendlySource(string source) => source.ToUpperInvariant() switch
    {
        "SPOTIFY" => "Spotify",
        "BLUETOOTH" => "Bluetooth",
        "AUX" or "PRODUCT" => Localization.Get("L_SrcAux"),
        "LOCAL_INTERNET_RADIO" or "INTERNET_RADIO" or "TUNEIN" => Localization.Get("L_SrcRadio"),
        "UPNP" or "STORED_MUSIC" => Localization.Get("L_SrcLibrary"),
        "AIRPLAY" => "AirPlay",
        "DEEZER" => "Deezer",
        "NOTIFICATION" => Localization.Get("L_SrcNotification"),
        _ => source,
    };
}

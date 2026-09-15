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

    public PlayerViewModel(SpeakerManager speakers, PresetsViewModel presets, ArtworkCache artwork, ZoneLocator zones)
        : base(speakers)
    {
        _presetsViewModel = presets;
        _artwork = artwork;
        _zones = zones;

        Speakers.DeviceChanged += (_, device) => OnMainThread(() =>
        {
            Subscribe(device);
            ReloadSpeakerList();
            _ = RefreshAsync();
        });

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
                var slaves = GroupCandidates
                    .Where(c => c.IsGrouped || ReferenceEquals(c, choice))
                    .Select(c => new StrZoneMemberRef { DeviceId = c.Endpoint.DeviceId, Ip = c.Endpoint.Host })
                    .ToList();

                // L'agent enregistre le document tel qu'il le reçoit : omettre
                // « permanent » sur un groupe qui l'était le rétrograde en groupe
                // ordinaire, qui cesse de se reformer et qu'une dissolution efface
                // définitivement. On relit donc le drapeau avant de republier.
                var permanent = await device.Str.GetZonePermanentAsync().ConfigureAwait(false);

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

        // Le firmware met un instant à publier la nouvelle zone.
        await Task.Delay(600).ConfigureAwait(true);
        await LoadZoneAsync().ConfigureAwait(true);
    }

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

    private static async Task UngroupAsync(
        SpeakerDevice device, SpeakerEndpoint self, SpeakerChoice choice, (string DeviceId, string Ip) master)
    {
        // Retirer un seul membre n'existe pas côté agent : on passe par le firmware,
        // qui exige l'identité du MAÎTRE de la zone. Ce n'est pas forcément l'enceinte
        // pilotée : on peut très bien regarder un esclave.
        var tappedTheMaster = string.Equals(choice.Host, master.Ip, StringComparison.OrdinalIgnoreCase);

        // Toucher la pastille du maître depuis une enceinte qui le suit ne veut pas
        // dire « retirer le maître » — une zone sans maître n'existe pas. Cela veut
        // dire « quitter le groupe » : c'est l'enceinte DÉSIGNÉE qu'on retire, pas
        // celle qui reçoit les commandes, qui est justement le maître dans ce cas.
        // L'ordre, lui, s'adresse toujours au maître.
        var removedId = tappedTheMaster ? self.DeviceId : choice.Endpoint.DeviceId;
        var removedIp = tappedTheMaster ? self.Host : choice.Endpoint.Host;

        if (!string.IsNullOrWhiteSpace(removedId) && !string.IsNullOrWhiteSpace(master.DeviceId))
        {
            try
            {
                await device.Bose.RemoveZoneSlaveAsync(
                    master.DeviceId,
                    master.Ip,
                    new[] { new ZoneMember { DeviceId = removedId, IpAddress = removedIp } })
                    .ConfigureAwait(false);

                return;
            }
            catch (Exception)
            {
                // On retombe sur la dissolution complète.
            }
        }

        if (device.Str is not null)
        {
            await device.Str.DissolveZoneAsync().ConfigureAwait(false);
            return;
        }

        await device.Bose.DissolveZoneAsync(master.DeviceId, master.Ip).ConfigureAwait(false);
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
                AgentVersion = speaker.AgentVersion,
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

        // Une enceinte qu'on éteint doit d'abord quitter le groupe. Sans ça, la zone
        // garde un membre injoignable : le maître continue de lui diffuser, l'agent
        // s'épuise à le joindre, et au rallumage l'enceinte se retrouve prisonnière
        // d'un groupe qu'elle ne peut plus quitter. Elle sort donc du groupe, et si
        // elle en était le dernier membre utile, le groupe est dissous.
        if (!wanted)
        {
            await LeaveGroupBeforeStandbyAsync().ConfigureAwait(true);
        }

        _powerRequestedAt = DateTime.UtcNow;
        _powerRequestedOn = wanted;
        IsStandby = !wanted;      // le bouton reflète tout de suite l'intention

        await RunAsync(d => d.SetPowerAsync(wanted)).ConfigureAwait(true);

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
    /// Retire l'enceinte pilotée du groupe avant sa mise en veille.
    ///
    /// Trois cas : elle est le maître, et le groupe entier n'a plus de raison
    /// d'exister ; elle est le dernier esclave, même conclusion ; sinon on ne retire
    /// qu'elle. L'échec n'est jamais bloquant — la mise en veille reste l'action
    /// demandée, et un groupe mal défait se rattrape à la relecture suivante.
    /// </summary>
    private async Task LeaveGroupBeforeStandbyAsync()
    {
        var device = Device;

        if (device is null || !(IsGroupActive || HasGroupVolume) || GroupMembers.Count == 0)
        {
            return;
        }

        var self = GroupMembers.FirstOrDefault(
            m => string.Equals(m.Ip, device.Host, StringComparison.OrdinalIgnoreCase));

        var master = GroupMembers.FirstOrDefault(m => m.IsMaster);
        var slaves = GroupMembers.Count(m => !m.IsMaster);

        try
        {
            if (self is null || self.IsMaster || slaves <= 1)
            {
                if (device.Str is not null)
                {
                    await device.Str.DissolveZoneAsync().ConfigureAwait(true);
                }
                else
                {
                    await device.Bose.SetZoneAsync(
                        device.Endpoint.DeviceId, device.Host, Array.Empty<ZoneMember>()).ConfigureAwait(true);
                }
            }
            else if (master is not null && !string.IsNullOrWhiteSpace(master.DeviceId))
            {
                // Le retrait passe par le firmware et réclame l'identité du MAÎTRE,
                // qui n'est pas forcément l'enceinte pilotée.
                await device.Bose.RemoveZoneSlaveAsync(
                    master.DeviceId,
                    master.Ip,
                    new[] { new ZoneMember { DeviceId = self.DeviceId, IpAddress = self.Ip } })
                    .ConfigureAwait(true);
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

    // ================================================================ Cycle de vie

    public void OnAppearing()
    {
        _pollTimer ??= CreatePollTimer();
        _pollTimer.Start();

        ReloadSpeakerList();
        _ = RefreshAsync();

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

        Title = nowPlaying.IsIdle && string.IsNullOrWhiteSpace(nowPlaying.Track)
            ? Localization.Get(nowPlaying.IsStandby ? "L_Standby" : "L_NothingPlaying")
            : nowPlaying.DisplayTitle;

        Subtitle = nowPlaying.DisplaySubtitle;
        ArtGlyph = GlyphFor(nowPlaying.Source);
        IsPlaying = nowPlaying.IsPlaying;
        // Pendant le sursis, l'état demandé prime sur celui que l'enceinte annonce
        // encore : elle n'a pas fini de se réveiller, elle ne ment pas.
        var pendingPower = DateTime.UtcNow - _powerRequestedAt < PowerGrace;

        IsStandby = pendingPower ? !_powerRequestedOn : nowPlaying.IsIdle;
        SourceLabel = nowPlaying.IsIdle ? Localization.Get("L_Standby") : FriendlySource(nowPlaying.Source);

        // La touche correspondant à ce qui joue est mise en avant ; si rien ne
        // correspond — Spotify lancé depuis le téléphone, par exemple — plus aucune
        // touche n'est verte.
        _presetsViewModel.SyncActive(
            nowPlaying.Source,
            FirstNonEmpty(nowPlaying.StationName, nowPlaying.Content?.ItemName),
            nowPlaying.Content?.Location,
            nowPlaying.IsIdle);

        await LoadArtAsync(nowPlaying).ConfigureAwait(true);
    }

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

        var key = string.Join(
            '|',
            nowPlaying.Source,
            nowPlaying.Content?.Location ?? "",
            station ?? "");

        if (string.Equals(key, _artKey, StringComparison.Ordinal) && HasArt)
        {
            return;
        }

        _artKey = key;

        // La station a changé : on repart d'une vignette vide.
        ArtSource = null;
        HasArt = false;

        var candidates = new List<string>();

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

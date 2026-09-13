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

    /// <summary>Pendant ce délai après une action locale, les mises à jour entrantes sont ignorées.</summary>
    private static readonly TimeSpan EchoSuppression = TimeSpan.FromMilliseconds(1500);

    private readonly PresetsViewModel _presetsViewModel;
    private readonly ArtworkCache _artwork;
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

    public PlayerViewModel(SpeakerManager speakers, PresetsViewModel presets, ArtworkCache artwork) : base(speakers)
    {
        _presetsViewModel = presets;
        _artwork = artwork;

        Speakers.DeviceChanged += (_, device) => OnMainThread(() =>
        {
            Subscribe(device);
            ReloadSpeakerList();
            _ = RefreshAsync();
        });

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

    [RelayCommand]
    private async Task SelectSpeakerAsync(SpeakerChoice? choice)
    {
        if (choice is null)
        {
            return;
        }

        // Rien à faire si c'est déjà l'enceinte active.
        if (Device is not null && string.Equals(Device.Host, choice.Endpoint.Host, StringComparison.OrdinalIgnoreCase))
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

        await RunAsync(async device =>
        {
            if (wasGrouped)
            {
                await UngroupAsync(device, choice).ConfigureAwait(false);
                return;
            }

            if (device.Str is not null)
            {
                var slaves = GroupCandidates
                    .Where(c => c.IsGrouped || ReferenceEquals(c, choice))
                    .Select(c => new StrZoneMemberRef { DeviceId = c.Endpoint.DeviceId, Ip = c.Endpoint.Host })
                    .ToList();

                await device.Str.FormZoneAsync(new StrZoneFormRequest
                {
                    Master = new StrZoneMemberRef { DeviceId = device.Endpoint.DeviceId, Ip = device.Host },
                    Slaves = slaves,
                    Mode = "native",
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

    private static async Task UngroupAsync(SpeakerDevice device, SpeakerChoice choice)
    {
        // Retirer un seul membre n'existe pas côté agent : on passe par le firmware,
        // et on dissout quand il ne resterait plus personne à suivre.
        if (!string.IsNullOrWhiteSpace(choice.Endpoint.DeviceId))
        {
            try
            {
                await device.Bose.RemoveZoneSlaveAsync(
                    device.Endpoint.DeviceId,
                    device.Host,
                    new[] { new ZoneMember { DeviceId = choice.Endpoint.DeviceId, IpAddress = choice.Endpoint.Host } })
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

        await device.Bose.DissolveZoneAsync(device.Endpoint.DeviceId, device.Host).ConfigureAwait(false);
    }

    /// <summary>Recharge les cartes d'enceintes et marque celle qui est active.</summary>
    public void ReloadSpeakerList()
    {
        var host = Device?.Host;
        var grouped = GroupCandidates
            .Where(c => c.IsGrouped)
            .Select(c => c.Host)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        SpeakerChoices.Clear();
        GroupCandidates.Clear();

        foreach (var speaker in Speakers.KnownSpeakers)
        {
            var isActive = host is not null && string.Equals(speaker.Host, host, StringComparison.OrdinalIgnoreCase);

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

            if (!isActive)
            {
                GroupCandidates.Add(choice);
            }
        }

        HasSeveralSpeakers = SpeakerChoices.Count > 1;
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
    private bool _isGroupActive;

    /// <summary>
    /// Vrai seulement avec l'agent et un groupe actif. Commande alors deux choses :
    /// le curseur du haut règle l'ensemble du groupe, et la liste par enceinte
    /// apparaît en dessous.
    /// </summary>
    [ObservableProperty]
    private bool _hasGroupVolume;

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
    private Task PowerAsync() => RunAsync(d => d.SetPowerAsync(IsStandby));

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
            return;
        }

        var memberIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Sert à détecter la dissolution du groupe : le curseur du haut doit alors
        // revenir au volume propre de l'enceinte pilotée.
        var wasGroupVolume = HasGroupVolume;

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

        foreach (var candidate in GroupCandidates)
        {
            candidate.IsGrouped = memberIps.Contains(candidate.Host);
        }

        if (!IsGroupActive)
        {
            GroupMembers.Clear();
        }

        if (wasGroupVolume && !HasGroupVolume)
        {
            await ReloadOwnVolumeAsync().ConfigureAwait(true);
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
            _subscribedTo.Notifications.NowPlayingChanged -= OnNowPlayingNotification;
            _subscribedTo.Notifications.ConnectionChanged -= OnConnectionNotification;
            _subscribedTo.Notifications.ZoneChanged -= OnZoneNotification;
        }

        _subscribedTo = device;

        if (device is not null)
        {
            device.Notifications.VolumeChanged += OnVolumeNotification;
            device.Notifications.NowPlayingChanged += OnNowPlayingNotification;
            device.Notifications.ConnectionChanged += OnConnectionNotification;
            device.Notifications.ZoneChanged += OnZoneNotification;
        }

        OnPropertyChanged(nameof(HasNoSpeaker));
    }

    private void OnVolumeNotification(object? sender, VolumeState state) => OnMainThread(() => ApplyVolume(state));

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
        IsStandby = nowPlaying.IsIdle;
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

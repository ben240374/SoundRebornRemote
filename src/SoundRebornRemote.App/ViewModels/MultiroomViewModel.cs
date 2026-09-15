using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>Une enceinte candidate à un groupe.</summary>
public sealed partial class GroupCandidate : ObservableObject
{
    public required SpeakerEndpoint Endpoint { get; init; }

    public string Name => Endpoint.DisplayName;

    public string Host => Endpoint.Host;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isInGroup;

    [ObservableProperty]
    private string _state = "";
}

/// <summary>
/// Groupe multi-pièces. L'enceinte active est le maître : c'est elle qui diffuse
/// le flux, les autres le suivent. Avec l'agent STR on passe par /api/box/zone,
/// qui mémorise le groupe et sait le reformer ; sinon on écrit directement
/// /setZone sur le firmware.
/// </summary>
public sealed partial class MultiroomViewModel : BaseViewModel
{
    private readonly ZoneLocator _zones;

    public MultiroomViewModel(SpeakerManager speakers, ZoneLocator zones) : base(speakers)
    {
        _zones = zones;
        Speakers.DeviceChanged += (_, _) => OnMainThread(() => _ = RefreshAsync());
    }

    public ObservableCollection<GroupCandidate> Candidates { get; } = new();

    public ObservableCollection<StrZoneMemberVolume> GroupVolumes { get; } = new();

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _masterName = "—";

    /// <summary>
    /// Faux quand l'enceinte pilotée est un esclave du groupe. L'écran s'en sert pour
    /// dire qui diffuse réellement, au lieu de laisser croire que c'est celle-ci.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSlaveSpeaker))]
    private bool _isMasterSpeaker = true;

    /// <summary>L'inverse, pour l'affichage : le projet n'a pas de convertisseur de négation.</summary>
    public bool IsSlaveSpeaker => !IsMasterSpeaker;

    [ObservableProperty]
    private bool _isGrouped;

    [ObservableProperty]
    private bool _canUseGroupVolume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupVolumeLabel))]
    private double _groupVolume;

    public string GroupVolumeLabel => $"{(int)Math.Round(GroupVolume)} %";

    [ObservableProperty]
    private bool _makePermanent;

    /// <summary>Faux quand aucune autre enceinte n'est connue : on affiche alors l'invite.</summary>
    [ObservableProperty]
    private bool _hasCandidates;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var device = Device;

        Candidates.Clear();
        GroupVolumes.Clear();

        if (device is null)
        {
            MasterName = "—";
            IsMasterSpeaker = true;
            IsGrouped = false;
            CanUseGroupVolume = false;
            HasCandidates = false;
            return;
        }

        // Point de départ de chaque relecture : sans groupe, l'enceinte pilotée est
        // à elle-même son propre maître. La lecture de la zone, plus bas, corrigera
        // si un vrai maître existe. Sans cette remise à zéro, la mention ambre
        // « ce n'est pas le maître » survit à la dissolution du groupe.
        MasterName = device.Endpoint.DisplayName;
        IsMasterSpeaker = true;
        CanUseGroupVolume = device.HasStr;
        IsRefreshing = true;

        try
        {
            // Le groupe est cherché là où il est réellement décrit : l'enceinte
            // pilotée d'abord, puis les autres si elle ne déclare rien. Une esclave
            // ne sait pas toujours qu'elle suit quelqu'un ; le maître, lui, le sait.
            var zone = await _zones.LocateAsync(device.Host, Speakers.KnownSpeakers).ConfigureAwait(true);
            var memberIps = zone.MemberIps.ToHashSet(StringComparer.OrdinalIgnoreCase);

            IsGrouped = zone.Grouped;

            if (zone.Grouped)
            {
                MasterName = zone.MasterName;
                IsMasterSpeaker = zone.IsMaster(device.Host);
            }

            foreach (var endpoint in Speakers.KnownSpeakers)
            {
                if (string.Equals(endpoint.Host, device.Host, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var inGroup = memberIps.Contains(endpoint.Host);

                Candidates.Add(new GroupCandidate
                {
                    Endpoint = endpoint,
                    IsSelected = inGroup,
                    IsInGroup = inGroup,
                    State = endpoint.Summary,
                });
            }

            HasCandidates = Candidates.Count > 0;

            if (Candidates.Count == 0)
            {
                ShowInfo(Localization.Get("S_OnlyOneSpeaker"));
            }
            else
            {
                ShowInfo(null);
            }

            if (device.Str is not null)
            {
                await LoadGroupVolumeAsync().ConfigureAwait(true);
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

    private async Task LoadGroupVolumeAsync()
    {
        var device = Device;

        if (device?.Str is null)
        {
            return;
        }

        try
        {
            var state = await device.Str.GetZoneVolumeAsync().ConfigureAwait(true);

            if (state is null)
            {
                return;
            }

            GroupVolumes.Clear();

            foreach (var member in state.Members)
            {
                GroupVolumes.Add(member);
            }

            GroupVolume = state.Average;

            // L'agent ne voit la zone que depuis le maître : vu d'une esclave, il la
            // dit parfois absente. Le relevé du firmware, fait juste avant, prime donc
            // quand il a trouvé un groupe. L'inverse serait une régression.
            IsGrouped = IsGrouped || state.Grouped;

            // Le maître est celui que la zone déclare, pas l'enceinte qu'on regarde.
            // Basculer sur un esclave ne doit pas donner l'impression que la diffusion
            // a changé de pièce.
            var master = state.Members.FirstOrDefault(m => m.IsMaster);

            if (master is not null && state.Grouped)
            {
                MasterName = master.DisplayName;
                IsMasterSpeaker = string.Equals(master.Ip, device.Host, StringComparison.OrdinalIgnoreCase);
            }
            else if (!IsGrouped)
            {
                // Plus de zone du tout : l'enceinte pilotée redevient la référence,
                // et la mention ambre disparaît. Si le firmware, lui, voit encore un
                // groupe, on garde ce qu'il a désigné.
                MasterName = device.Endpoint.DisplayName;
                IsMasterSpeaker = true;
            }
        }
        catch (Exception)
        {
            // L'agent peut ne pas exposer cette route selon sa version.
            CanUseGroupVolume = false;
        }
    }

    [RelayCommand]
    private async Task FormGroupAsync()
    {
        // Une enceinte n'appartient qu'à un groupe à la fois, et seul le maître le
        // compose. Depuis une esclave, former un groupe reviendrait à en créer un
        // second qui défait le premier : on renvoie vers le maître.
        if (IsSlaveSpeaker)
        {
            ShowInfo(Localization.Get("S_SlaveCannotForm", MasterName));
            return;
        }

        var selected = Candidates.Where(c => c.IsSelected).ToList();

        if (selected.Count == 0)
        {
            ShowInfo(Localization.Get("S_CheckOne"));
            return;
        }

        await RunAsync(async device =>
        {
            if (device.Str is not null)
            {
                var request = new StrZoneFormRequest
                {
                    Master = new StrZoneMemberRef { DeviceId = device.Endpoint.DeviceId, Ip = device.Host },
                    Slaves = selected
                        .Select(c => new StrZoneMemberRef { DeviceId = c.Endpoint.DeviceId, Ip = c.Endpoint.Host })
                        .ToList(),
                    Mode = "native",
                    Permanent = MakePermanent,
                };

                await device.Str.FormZoneAsync(request).ConfigureAwait(false);
            }
            else
            {
                // Sans agent, le firmware exige le deviceID de chaque membre.
                var missing = selected.Where(c => string.IsNullOrWhiteSpace(c.Endpoint.DeviceId)).ToList();

                if (missing.Count > 0)
                {
                    throw new InvalidOperationException(
                        Localization.Get("S_MissingDeviceId", string.Join(", ", missing.Select(m => m.Name))));
                }

                var members = selected.Select(c => new ZoneMember
                {
                    DeviceId = c.Endpoint.DeviceId,
                    IpAddress = c.Endpoint.Host,
                });

                await device.Bose.SetZoneAsync(device.Endpoint.DeviceId, device.Host, members).ConfigureAwait(false);
            }
        }, Localization.Get("S_GroupFormed"));

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DissolveGroupAsync()
    {
        await RunAsync(async device =>
        {
            if (device.Str is not null)
            {
                await device.Str.DissolveZoneAsync().ConfigureAwait(false);
                return;
            }

            await device.Bose.DissolveZoneAsync(device.Endpoint.DeviceId, device.Host).ConfigureAwait(false);
        }, Localization.Get("S_GroupDissolved"));

        await RefreshAsync();
    }

    [RelayCommand]
    private Task ApplyGroupVolumeAsync() => RunAsync(async device =>
    {
        if (device.Str is null)
        {
            throw new InvalidOperationException(Localization.Get("S_NeedAgentGroupVolume"));
        }

        await device.Str.SetZoneVolumeAsync((int)Math.Round(GroupVolume)).ConfigureAwait(false);
    }, Localization.Get("S_GroupVolumeApplied"));

    [RelayCommand]
    private Task SetMemberVolumeAsync(StrZoneMemberVolume? member)
    {
        if (member?.Ip is null)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async device =>
        {
            if (device.Str is null)
            {
                throw new InvalidOperationException(Localization.Get("S_NeedAgentGroupVolume"));
            }

            await device.Str.SetZoneVolumeAsync(member.Volume, member.Ip).ConfigureAwait(false);
        });
    }
}

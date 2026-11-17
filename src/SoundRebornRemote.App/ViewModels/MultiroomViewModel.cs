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
    [NotifyPropertyChangedFor(nameof(ShowGroupVolume))]
    private bool _isGrouped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGroupVolume))]
    private bool _canUseGroupVolume;

    /// <summary>
    /// Le bloc « Volume du groupe » n'a de sens qu'avec un groupe : sans lui, l'agent
    /// refuse le réglage (409) et l'utilisateur voyait une erreur en rouge.
    /// </summary>
    public bool ShowGroupVolume => CanUseGroupVolume && IsGrouped;

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

        var device = Device;

        if (device is null)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_NoSpeaker");
            return;
        }

        // Ce que vaudra « permanent » dans la requête. Sans agent, la case seule.
        var permanent = MakePermanent;

        if (device.Str is not null)
        {
            // Un agent trop ancien ne met pas de côté le groupe permanent qu'un
            // autre groupe recouvre : il l'écrase. On ne lui confie aucun groupe.
            if (device.AgentTooOld)
            {
                IsError = true;
                StatusMessage = AgentTooOldForGroups(device);
                return;
            }

            // On relit la zone AVANT d'envoyer quoi que ce soit, et on renonce si
            // cette lecture échoue : republier à l'aveugle détruirait ce que
            // l'utilisateur a enregistré, pour un simple hoquet réseau.
            var facts = await device.Str.GetZoneFactsAsync().ConfigureAwait(true);

            if (facts is null)
            {
                IsError = true;
                StatusMessage = Localization.Get("S_ZoneUnreadable");
                return;
            }

            // Une paire stéréo n'est pas un groupe multi-pièces, et l'agent ne sait
            // pas encore lui adjoindre une enceinte. Retirer un membre d'un groupe
            // existant reste permis ; en ajouter un, non.
            if (facts.Stereo && selected.Any(c => !c.IsInGroup))
            {
                IsError = true;
                StatusMessage = Localization.Get("S_StereoCannotExtend");
                return;
            }

            if (IsGrouped)
            {
                // On étend ou on réduit le groupe qui joue : c'est le même groupe.
                // S'il était durable il le reste ; la case peut le rendre durable,
                // jamais le rétrograder.
                permanent = facts.Permanent || MakePermanent;
            }
            else if (facts.Permanent)
            {
                // Aucun groupe ne joue, mais l'agent en garde un, durable, qui
                // attend la prochaine lecture. Rien ne le montre sur cet écran :
                // l'envoyer promener d'un appui sur « Grouper » serait le perdre
                // sans l'avoir su.
                if (facts.RemembersExactly(selected.Select(c => c.Host)))
                {
                    // Les mêmes enceintes : on FORME ce groupe, on ne le remplace
                    // pas. Il reste donc durable, quelle que soit la case.
                    permanent = true;
                }
                else if (!await ConfirmOverSavedGroupAsync(facts.Remembered).ConfigureAwait(true))
                {
                    return;
                }
            }
            else if (MakePermanent && facts.Remembered.Count > 0)
            {
                // Aucun groupe ne joue, et l'agent garde un groupe ORDINAIRE. S'il a
                // été formé par-dessus un groupe permanent, l'agent a mis ce dernier
                // de côté sans le signaler, et « permanent:true » le remplacerait.
                // L'application ne peut pas savoir s'il existe : elle le dit.
                if (!await ConfirmAsync(Localization.Get("L_SetAsideAsk")).ConfigureAwait(true))
                {
                    return;
                }
            }
        }

        await RunAsync(async target =>
        {
            // La lecture ci-dessus vaut pour CETTE enceinte. Si l'enceinte pilotée a
            // changé pendant la question posée à l'utilisateur, on ne publie rien.
            if (!string.Equals(target.Host, device.Host, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(Localization.Get("S_ZoneUnreadable"));
            }

            if (target.Str is not null)
            {
                // Jamais de « stereo » ici : pour l'agent ce serait l'ordre de faire
                // une paire de ces enceintes, pas un attribut du groupe.
                var request = new StrZoneFormRequest
                {
                    Master = new StrZoneMemberRef { DeviceId = target.Endpoint.DeviceId, Ip = target.Host },
                    Slaves = selected
                        .Select(c => new StrZoneMemberRef { DeviceId = c.Endpoint.DeviceId, Ip = c.Endpoint.Host })
                        .ToList(),
                    Mode = "native",
                    Permanent = permanent,
                };

                await target.Str.FormZoneAsync(request).ConfigureAwait(false);
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

                await target.Bose.SetZoneAsync(target.Endpoint.DeviceId, target.Host, members).ConfigureAwait(false);
            }
        }, Localization.Get("S_GroupFormed"));

        await RefreshKeepingStatusAsync();
    }

    /// <summary>
    /// Demande s'il faut former un groupe par-dessus le groupe durable que l'agent
    /// garde en mémoire. Le groupe enregistré est nommé, pour que l'utilisateur
    /// sache de quoi on parle : rien d'autre sur cet écran ne le montre.
    /// </summary>
    private async Task<bool> ConfirmOverSavedGroupAsync(IReadOnlyList<StrRememberedMember> remembered)
    {
        var names = string.Join(", ", remembered.Select(SavedMemberName).Where(n => n.Length > 0));

        var question = names.Length == 0
            ? Localization.Get("L_SavedGroupAsk")
            : Localization.Get("L_SavedGroupAskNamed", names);

        return await ConfirmAsync(question).ConfigureAwait(true);
    }

    /// <summary>« Former ce groupe quand même » ou « Annuler ». Vrai seulement pour le premier.</summary>
    private static async Task<bool> ConfirmAsync(string question)
    {
        var proceed = Localization.Get("L_FormAnyway");

        var choice = await Shell.Current.DisplayActionSheetAsync(
            question,
            Localization.Get("L_Cancel"),
            null,
            proceed).ConfigureAwait(true);

        return choice == proceed;
    }

    /// <summary>Le nom d'une suiveuse enregistrée : celui de l'agent, sinon celui que l'application lui connaît.</summary>
    private string SavedMemberName(StrRememberedMember member)
    {
        if (!string.IsNullOrWhiteSpace(member.Name))
        {
            return member.Name!.Trim();
        }

        var known = Speakers.KnownSpeakers.FirstOrDefault(k =>
            string.Equals(k.Host, member.Ip, StringComparison.OrdinalIgnoreCase));

        return known?.DisplayName ?? member.Ip;
    }

    /// <summary>
    /// Relit l'écran après une commande sans effacer son compte rendu. La relecture
    /// remet le message à zéro ; appelée telle quelle, elle faisait disparaître
    /// aussitôt « Groupe formé » comme le refus de l'agent, et l'utilisateur ne
    /// voyait rien. Seule une relecture en échec après une commande réussie
    /// garde son propre message.
    /// </summary>
    private async Task RefreshKeepingStatusAsync()
    {
        var commandFailed = IsError;
        var report = StatusMessage;

        await RefreshAsync();

        if (!string.IsNullOrEmpty(report) && (commandFailed || !IsError))
        {
            IsError = commandFailed;
            StatusMessage = report;
        }
    }

    [RelayCommand]
    private async Task DissolveGroupAsync()
    {
        var kept = false;

        await RunAsync(async device =>
        {
            if (device.Str is not null)
            {
                // Un 200 ne vaut pas succès : l'agent répond 200 avec « ok : false »
                // quand l'enceinte a pris la demande et gardé le groupe. Afficher la
                // confirmation sur cette réponse, c'est cocher un groupe qui joue.
                var result = await device.Str.DissolveZoneAsync().ConfigureAwait(false);

                if (!result.Ok)
                {
                    throw new InvalidOperationException(
                        Localization.Get("S_GroupStillUp", result.Remaining));
                }

                kept = result.StereoPairKept;
                return;
            }

            await device.Bose.DissolveZoneAsync(device.Endpoint.DeviceId, device.Host).ConfigureAwait(false);
        }, Localization.Get("S_GroupDissolved"));

        // La paire stéréo n'est pas un groupe multi-pièces : la défaire demande une
        // autre requête. On le dit plutôt que de laisser croire que tout est séparé.
        if (kept && !IsError)
        {
            ShowInfo(Localization.Get("S_StereoPairKept"));
        }

        await RefreshKeepingStatusAsync();
    }

    [RelayCommand]
    private Task ApplyGroupVolumeAsync() => !IsGrouped ? Task.CompletedTask : RunAsync(async device =>
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
        if (member?.Ip is null || !IsGrouped)
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

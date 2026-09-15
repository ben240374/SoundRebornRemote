using SoundReborn.Core;
using SoundReborn.Core.Models;

namespace SoundRebornRemote.App.Services;

/// <summary>
/// Ce que l'application sait du groupe en cours, indépendamment de l'enceinte
/// par laquelle on l'a appris.
/// </summary>
public sealed class ZoneSnapshot
{
    public bool Grouped { get; init; }

    /// <summary>Adresse de l'enceinte qui mène le groupe. Vide quand il n'y en a pas.</summary>
    public string MasterIp { get; init; } = "";

    /// <summary>Nom d'affichage du maître, ou son adresse si elle n'est pas connue.</summary>
    public string MasterName { get; init; } = "";

    public string MasterDeviceId { get; init; } = "";

    public IReadOnlyList<string> MemberIps { get; init; } = Array.Empty<string>();

    public static ZoneSnapshot None { get; } = new();

    public bool Contains(string ip)
        => MemberIps.Any(m => string.Equals(m, ip, StringComparison.OrdinalIgnoreCase));

    public bool IsMaster(string ip)
        => !Grouped || string.Equals(MasterIp, ip, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Retrouve le groupe auquel une enceinte appartient.
///
/// L'agent STR décrit la zone telle que le MAÎTRE la voit : interrogé sur une
/// enceinte qui ne fait que suivre, il répond volontiers qu'il n'y a pas de
/// groupe. Le firmware d'une esclave n'est pas toujours plus bavard — selon le
/// modèle, /getZone y renvoie une zone vide.
///
/// D'où cette recherche en deux temps : on interroge l'enceinte pilotée, puis, si
/// elle ne déclare rien, les autres enceintes connues. Celle qui mène un groupe
/// le décrit toujours, membres compris ; si notre enceinte y figure, on tient la
/// réponse et l'identité du maître avec.
/// </summary>
public sealed class ZoneLocator
{
    /// <summary>
    /// Budget par enceinte interrogée. Court : une enceinte éteinte ne doit pas
    /// retarder l'affichage, et cette recherche tourne à chaque relecture de zone.
    /// </summary>
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(4);

    private readonly HttpClient _http;

    /// <summary>
    /// Dépend du seul HttpClient, pas de SpeakerManager : c'est ce dernier qui
    /// s'appuie sur ce service pour rediriger les commandes vers le maître, et deux
    /// services qui se tiennent l'un l'autre ne se construisent pas.
    /// </summary>
    public ZoneLocator(HttpClient http) => _http = http;

    public async Task<ZoneSnapshot> LocateAsync(
        string host, IReadOnlyList<SpeakerEndpoint> known, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return ZoneSnapshot.None;
        }

        // 1. Ce que l'enceinte elle-même dit de sa zone.
        var own = await TryReadAsync(new BoseApiClient(host, _http), known, ct).ConfigureAwait(false);

        if (own is not null)
        {
            return own;
        }

        // 2. Sinon, les voisines. On s'arrête à la première qui nous compte parmi
        //    ses membres : une enceinte n'appartient qu'à un groupe à la fois.
        foreach (var peer in known.ToList())
        {
            if (string.Equals(peer.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var snapshot = await TryReadAsync(new BoseApiClient(peer.Host, _http), known, ct).ConfigureAwait(false);

            if (snapshot is not null && snapshot.Contains(host))
            {
                return snapshot;
            }
        }

        return ZoneSnapshot.None;
    }

    /// <summary>
    /// Lit /getZone sur une enceinte. Renvoie null si elle ne déclare aucun groupe.
    ///
    /// Le nombre de &lt;member&gt; ne suffit pas à trancher : selon le modèle et selon
    /// qu'on interroge le maître ou une suivante, le firmware renvoie la liste
    /// complète, la seule liste des suivantes, ou rien du tout — mais dès qu'un
    /// groupe existe, l'attribut « master » est là. C'est donc lui le signal, et la
    /// liste des membres n'est qu'un complément. Une enceinte seule répond
    /// &lt;zone /&gt; : ni maître, ni membre.
    /// </summary>
    private static async Task<ZoneSnapshot?> TryReadAsync(
        BoseApiClient bose, IReadOnlyList<SpeakerEndpoint> known, CancellationToken ct)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(PeerTimeout);

            var zone = await bose.GetZoneAsync(budget.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(zone.MasterDeviceId) && zone.Members.Count <= 1)
            {
                return null;
            }

            // Le maître figure souvent parmi les membres : son deviceID est celui que
            // porte l'attribut « master ». À défaut, l'adresse de l'expéditeur, que le
            // firmware d'une suivante renseigne avec celle du maître.
            var masterMember = zone.Members.FirstOrDefault(m =>
                !string.IsNullOrWhiteSpace(zone.MasterDeviceId) &&
                string.Equals(m.DeviceId, zone.MasterDeviceId, StringComparison.OrdinalIgnoreCase));

            var masterIp = masterMember?.IpAddress ?? zone.SenderIp ?? "";

            // Faute d'adresse dans la réponse, on cherche le deviceID parmi les
            // enceintes connues : l'application les a toutes relevées au balayage.
            if (string.IsNullOrWhiteSpace(masterIp) && !string.IsNullOrWhiteSpace(zone.MasterDeviceId))
            {
                masterIp = known.FirstOrDefault(s =>
                    string.Equals(s.DeviceId, zone.MasterDeviceId, StringComparison.OrdinalIgnoreCase))?.Host ?? "";
            }

            if (string.IsNullOrWhiteSpace(masterIp))
            {
                return null;
            }

            // L'enceinte interrogée et le maître font partie du groupe, que le
            // firmware ait pris la peine de les lister ou non.
            var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { bose.Host, masterIp };

            foreach (var member in zone.Members)
            {
                ips.Add(member.IpAddress);
            }

            // Deux adresses distinctes au minimum : sinon l'enceinte est seule et
            // se déclare maître d'elle-même, ce qui n'est pas un groupe.
            if (ips.Count < 2)
            {
                return null;
            }

            var masterKnown = known.FirstOrDefault(s =>
                string.Equals(s.Host, masterIp, StringComparison.OrdinalIgnoreCase));

            return new ZoneSnapshot
            {
                Grouped = true,
                MasterIp = masterIp,
                MasterName = masterKnown?.DisplayName ?? masterIp,
                MasterDeviceId = masterMember?.DeviceId ?? zone.MasterDeviceId ?? "",
                MemberIps = ips.ToList(),
            };
        }
        catch (Exception)
        {
            // Enceinte éteinte, injoignable, ou zone illisible : elle ne nous
            // apprend rien, on passe à la suivante.
            return null;
        }
    }

    /// <summary>
    /// Rapport lisible de ce que chaque enceinte dit de la zone. Sert au diagnostic :
    /// quand l'affichage ne correspond pas à la réalité, c'est ici qu'on voit
    /// laquelle des deux se trompe.
    /// </summary>
    public async Task<IReadOnlyList<string>> DescribeAsync(
        string host, IReadOnlyList<SpeakerEndpoint> known, CancellationToken ct = default)
    {
        var lines = new List<string>();

        foreach (var peer in known.ToList())
        {
            var bose = new BoseApiClient(peer.Host, _http);

            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(PeerTimeout);

                var zone = await bose.GetZoneAsync(budget.Token).ConfigureAwait(false);
                var members = zone.Members.Count == 0
                    ? "—"
                    : string.Join(", ", zone.Members.Select(m => m.IpAddress));

                lines.Add($"{peer.DisplayName} : master={zone.MasterDeviceId ?? "—"}, " +
                          $"sender={zone.SenderIp ?? "—"}, members={members}");
            }
            catch (Exception ex)
            {
                lines.Add($"{peer.DisplayName} : {ex.GetType().Name}");
            }
        }

        var located = await LocateAsync(host, known, ct).ConfigureAwait(false);

        lines.Add(located.Grouped
            ? $"=> {located.MasterName} ({located.MasterIp}) + {located.MemberIps.Count} membre(s)"
            : "=> aucun groupe");

        return lines;
    }
}

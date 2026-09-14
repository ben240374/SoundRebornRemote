using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SoundReborn.Core;

/// <summary>
/// Découverte par mDNS : une question, et on écoute qui répond.
///
/// Les enceintes publient <c>_soundtouch._tcp</c> et l'agent STR publie
/// <c>_streborn._tcp</c>. Une seule question suffit donc à obtenir la liste, au lieu
/// des 254 requêtes HTTP du balayage.
///
/// Le protocole complet (RFC 6762) n'est pas implémenté, et ce n'est pas utile ici :
/// on envoie une question PTR depuis un port éphémère, ce qui en fait une
/// « requête unicast héritée » au sens du §6.7 — les répondeurs y répondent alors
/// directement à ce port, et non au groupe multicast. Deux conséquences heureuses :
/// aucun MulticastLock n'est nécessaire sous Android, puisqu'on ne reçoit pas de
/// multicast ; et il suffit de relever l'adresse source des réponses, sans avoir à
/// décoder les enregistrements. L'identité de l'appareil sera de toute façon
/// confirmée juste après par <c>GET :8090/info</c>.
///
/// Reste les cas où le mDNS ne donne rien : box qui filtre le multicast, enceinte en
/// veille profonde, réseau invité. D'où le balayage conservé en repli.
/// </summary>
public static class MdnsProbe
{
    private const string MulticastAddress = "224.0.0.251";
    private const int MulticastPort = 5353;

    /// <summary>Service publié par le firmware de l'enceinte.</summary>
    public const string SpeakerService = "_soundtouch._tcp.local";

    /// <summary>Service publié par l'agent STR lui-même.</summary>
    public const string AgentService = "_streborn._tcp.local";

    /// <summary>
    /// Pose la question et renvoie les adresses qui ont répondu pendant la fenêtre
    /// d'écoute. Ne lève jamais : un réseau qui refuse le multicast renvoie une
    /// liste vide, à charge de l'appelant de se rabattre sur le balayage.
    /// </summary>
    public static async Task<IReadOnlyList<string>> FindHostsAsync(
        TimeSpan window,
        CancellationToken ct = default)
    {
        var hosts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        UdpClient client;

        try
        {
            client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            client.Ttl = 255;                 // exigé par la RFC pour le mDNS
            client.MulticastLoopback = false; // inutile d'entendre sa propre question
        }
        catch (Exception)
        {
            return hosts;
        }

        using (client)
        {
            var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
            var target = new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort);

            foreach (var service in new[] { AgentService, SpeakerService })
            {
                var query = BuildQuery(id, service);

                try
                {
                    // Surcharge moderne (ReadOnlyMemory + jeton) : celle qui prend une
                    // longueur séparée appartient à l'ancienne génération d'API.
                    await client.SendAsync(query, target, ct).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Interface sans multicast : l'autre question a peut-être sa chance.
                }
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(window);

            while (!cts.IsCancellationRequested)
            {
                UdpReceiveResult result;

                try
                {
                    result = await client.ReceiveAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    break;
                }

                if (!IsAnswerTo(id, result.Buffer))
                {
                    continue;
                }

                var address = result.RemoteEndPoint.Address.ToString();

                if (seen.Add(address))
                {
                    hosts.Add(address);
                }
            }
        }

        return hosts;
    }

    /// <summary>Question DNS minimale : un seul enregistrement PTR demandé.</summary>
    private static byte[] BuildQuery(ushort id, string service)
    {
        var buffer = new List<byte>(64)
        {
            (byte)(id >> 8), (byte)(id & 0xFF),
            0x00, 0x00,   // indicateurs : question standard
            0x00, 0x01,   // QDCOUNT : une question
            0x00, 0x00,   // ANCOUNT
            0x00, 0x00,   // NSCOUNT
            0x00, 0x00,   // ARCOUNT
        };

        foreach (var label in service.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);

            buffer.Add((byte)bytes.Length);
            buffer.AddRange(bytes);
        }

        buffer.Add(0x00);              // fin du nom
        buffer.AddRange(new byte[] { 0x00, 0x0C });   // QTYPE = PTR
        buffer.AddRange(new byte[] { 0x00, 0x01 });   // QCLASS = IN

        return buffer.ToArray();
    }

    /// <summary>
    /// Vrai si le datagramme est une réponse à notre question : même identifiant,
    /// indicateur de réponse posé, et au moins un enregistrement. On ne décode pas
    /// plus loin, l'adresse source suffit.
    /// </summary>
    private static bool IsAnswerTo(ushort id, byte[] data)
    {
        if (data.Length < 12)
        {
            return false;
        }

        var responseId = (ushort)((data[0] << 8) | data[1]);
        var isResponse = (data[2] & 0x80) != 0;
        var answers = (data[6] << 8) | data[7];

        return responseId == id && isResponse && answers > 0;
    }
}

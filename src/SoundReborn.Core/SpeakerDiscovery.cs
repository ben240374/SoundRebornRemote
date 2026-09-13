using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Xml.Linq;
using SoundReborn.Core.Internal;
using SoundReborn.Core.Models;

namespace SoundReborn.Core;

/// <summary>
/// Recherche des enceintes sur le réseau local.
///
/// Le mDNS (_soundtouch._tcp / _streborn._tcp) serait plus élégant, mais sur Android
/// il exige un MulticastLock, il est filtré par beaucoup de box opérateur et il reste
/// muet quand l'enceinte est en veille profonde. Le balayage direct du /24 sur le port
/// 8090 est plus lourd mais il marche partout, et il est rapide en parallèle : 254
/// adresses avec un délai d'attente court tiennent en quelques secondes.
/// </summary>
public sealed class SpeakerDiscovery
{
    private readonly HttpClient _http;

    public SpeakerDiscovery(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <summary>Nombre de sondes simultanées. Au-delà, Android sature sa table de sockets.</summary>
    public int Parallelism { get; set; } = 48;

    /// <summary>Délai d'attente par adresse pendant le balayage.</summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromMilliseconds(900);

    /// <summary>Délai d'attente pour la détection de l'agent STR, une fois l'enceinte trouvée.</summary>
    public TimeSpan StrProbeTimeout { get; set; } = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// Balaie les sous-réseaux /24 des interfaces actives et renvoie les enceintes trouvées.
    /// <paramref name="onFound"/> est appelé au fil de l'eau, pour alimenter l'interface
    /// avant la fin du balayage.
    /// </summary>
    public async Task<IReadOnlyList<SpeakerEndpoint>> ScanAsync(
        Action<SpeakerEndpoint>? onFound = null,
        CancellationToken ct = default)
    {
        var found = new List<SpeakerEndpoint>();
        var gate = new SemaphoreSlim(Parallelism);
        var sync = new object();

        var candidates = EnumerateCandidateAddresses().ToList();

        var tasks = candidates.Select(async host =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                var speaker = await ProbeAsync(host, ct).ConfigureAwait(false);

                if (speaker is null)
                {
                    return;
                }

                lock (sync)
                {
                    if (found.Contains(speaker))
                    {
                        return;
                    }

                    found.Add(speaker);
                }

                onFound?.Invoke(speaker);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        lock (sync)
        {
            return found.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    /// <summary>
    /// Interroge une adresse précise. Renvoie null si ce n'est pas une SoundTouch.
    /// Sert aussi à valider une adresse saisie à la main dans les réglages.
    /// </summary>
    public async Task<SpeakerEndpoint?> ProbeAsync(string host, CancellationToken ct = default)
    {
        DeviceInfo info;

        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(ProbeTimeout);

            try
            {
                using var response = await _http
                    .GetAsync($"http://{host}:{BoseApiClient.DefaultPort}/info", HttpCompletionOption.ResponseContentRead, cts.Token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                var root = XDocument.Parse(text).Root;

                if (root is null || !root.Name.LocalName.Equals("info", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                info = FirmwareXml.ParseInfo(root);
            }
            catch (Exception)
            {
                // Adresse libre, autre appareil, XML illisible : on ignore.
                return null;
            }
        }

        var speaker = new SpeakerEndpoint
        {
            Host = host,
            Name = info.Name,
            DeviceId = info.DeviceId,
            Type = info.Type,
        };

        speaker.StrPort = await StrApiClient.ProbeAsync(host, _http, StrProbeTimeout, ct).ConfigureAwait(false);

        // La version de l'agent est lue tout de suite : sans cela, seule l'enceinte
        // connectée affichait la sienne, et les autres cartes restaient muettes.
        if (speaker.StrPort is { } port)
        {
            try
            {
                var agent = await new StrApiClient(host, port, _http).GetAgentInfoAsync(ct).ConfigureAwait(false);
                speaker.AgentVersion = agent?.DisplayVersion ?? "";
            }
            catch (Exception)
            {
                // Agent trop ancien pour ce point de terminaison : on laisse vide.
            }
        }

        return speaker;
    }

    /// <summary>
    /// Liste les adresses à sonder : tout le /24 de chaque interface IPv4 active,
    /// hors boucle locale et hors adresse de réseau/diffusion.
    /// </summary>
    public static IEnumerable<string> EnumerateCandidateAddresses()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prefix in EnumerateLocalPrefixes())
        {
            for (var host = 1; host <= 254; host++)
            {
                var address = prefix + host.ToString();

                if (seen.Add(address))
                {
                    yield return address;
                }
            }
        }
    }

    /// <summary>Préfixes « 192.168.1. » des interfaces IPv4 opérationnelles.</summary>
    public static IEnumerable<string> EnumerateLocalPrefixes()
    {
        NetworkInterface[] interfaces;

        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            yield break;
        }

        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties;

            try
            {
                properties = nic.GetIPProperties();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(unicast.Address))
                {
                    continue;
                }

                var octets = unicast.Address.GetAddressBytes();

                // Écarte les adresses APIPA 169.254.x.x, qui ne mènent nulle part.
                if (octets[0] == 169 && octets[1] == 254)
                {
                    continue;
                }

                yield return $"{octets[0]}.{octets[1]}.{octets[2]}.";
            }
        }
    }
}

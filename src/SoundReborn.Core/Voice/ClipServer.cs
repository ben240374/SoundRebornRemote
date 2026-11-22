using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SoundReborn.Core.Voice;

/// <summary>
/// Sert un seul clip audio en HTTP depuis le téléphone, le temps qu'un agent STR
/// vienne le chercher.
///
/// C'est ce que demande POST /api/announce avec « url » : l'agent télécharge le
/// clip lui-même, en entier, avant de le jouer. Le téléphone n'a pas de pare-feu
/// entrant sur le Wi-Fi ; seul un réseau qui isole les appareils l'en empêche, et
/// sur un tel réseau la télécommande n'atteindrait pas l'enceinte non plus.
///
/// Un serveur minimal, écrit à la main sur un TcpListener plutôt que HttpListener
/// (dont la prise en charge sur Android est incertaine) : une seule adresse,
/// impossible à deviner, GET et HEAD, plages d'octets, et rien d'autre. Il se
/// ferme à la fin de l'annonce.
///
/// Précautions : il n'écoute que sur l'adresse du réseau de l'enceinte (jamais sur
/// les données mobiles), le chemin porte 128 bits de hasard, les en-têtes sont
/// bornés en taille et en temps, et le nombre de connexions simultanées est
/// plafonné : un appareil malveillant du réseau ne peut ni deviner le clip ni
/// épuiser le téléphone pendant les quelques secondes où le serveur existe.
/// </summary>
public sealed class ClipServer : IDisposable
{
    private const int MaxHeaderBytes = 8 * 1024;

    /// <summary>Connexions servies en même temps ; au-delà, elles sont fermées aussitôt.</summary>
    private const int MaxConnections = 8;

    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(10);

    private readonly TcpListener _listener;
    private readonly byte[] _clip;
    private readonly string _contentType;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _served = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _slots = new(MaxConnections, MaxConnections);
    private int _requests;

    private ClipServer(byte[] clip, string contentType, string extension, IPAddress bindAddress)
    {
        _clip = clip;
        _contentType = contentType;
        Path = $"/voice-{Guid.NewGuid():N}.{extension.TrimStart('.')}";

        _listener = new TcpListener(bindAddress, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _ = AcceptLoopAsync();
    }

    /// <summary>Le port choisi par le système.</summary>
    public int Port { get; }

    /// <summary>Le chemin du clip, par exemple « /voice-3f2a….aac ».</summary>
    public string Path { get; }

    /// <summary>Nombre de requêtes reçues pour le clip, quelle qu'en soit l'issue.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <summary>Terminée dès que le clip a été envoyé en entier une première fois.</summary>
    public Task Served => _served.Task;

    /// <summary>
    /// Démarre le serveur sur un port libre de l'adresse donnée — celle que
    /// <see cref="LocalAddressFor"/> trouve vers l'enceinte.
    /// </summary>
    public static ClipServer Start(byte[] clip, string contentType, string extension, IPAddress bindAddress)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(bindAddress);
        return new ClipServer(clip, contentType, extension, bindAddress);
    }

    /// <summary>L'adresse à donner à l'agent, avec l'adresse du téléphone sur le réseau de l'enceinte.</summary>
    public string UrlFor(IPAddress localAddress)
        => $"http://{localAddress}:{Port.ToString(CultureInfo.InvariantCulture)}{Path}";

    /// <summary>
    /// L'adresse IPv4 du téléphone sur le réseau qui mène à l'enceinte. Aucun
    /// paquet ne part : « connecter » un socket UDP fait seulement choisir au
    /// système l'interface de sortie. Null quand aucune route n'y mène.
    /// </summary>
    public static IPAddress? LocalAddressFor(string speakerHost, int port = 8090)
    {
        try
        {
            if (!IPAddress.TryParse(speakerHost, out var target))
            {
                target = Dns.GetHostAddresses(speakerHost)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            }

            if (target is null || target.AddressFamily != AddressFamily.InterNetwork)
            {
                return null;
            }

            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(target, port));

            return socket.LocalEndPoint is IPEndPoint { Address: var local } &&
                   !local.Equals(IPAddress.Any)
                ? local
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        _stop.Cancel();

        try
        {
            _listener.Stop();
        }
        catch (Exception)
        {
            // Déjà fermé.
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            // Plusieurs requêtes à la fois : un agent peut demander une plage pendant
            // qu'une autre connexion se termine. Au-delà du plafond, on ferme.
            if (!_slots.Wait(0))
            {
                client.Dispose();
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleAsync(client).ConfigureAwait(false);
                }
                finally
                {
                    _slots.Release();
                }
            });
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();

                var request = await ReadHeaderAsync(stream).ConfigureAwait(false);

                if (request is null)
                {
                    return;
                }

                var (method, path, range) = request.Value;

                if (!string.Equals(path, Path, StringComparison.Ordinal))
                {
                    await WriteStatusAsync(stream, 404, "Not Found").ConfigureAwait(false);
                    return;
                }

                if (method is not ("GET" or "HEAD"))
                {
                    await WriteStatusAsync(stream, 405, "Method Not Allowed").ConfigureAwait(false);
                    return;
                }

                Interlocked.Increment(ref _requests);

                var (start, end, partial) = ParseRange(range, _clip.Length);

                if (start < 0)
                {
                    var refused = $"HTTP/1.1 416 Range Not Satisfiable\r\nContent-Range: bytes */{_clip.Length}\r\n" +
                                  "Content-Length: 0\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(refused)).ConfigureAwait(false);
                    return;
                }

                var length = end - start + 1;
                var header = new StringBuilder()
                    .Append(partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n")
                    .Append("Content-Type: ").Append(_contentType).Append("\r\n")
                    .Append("Content-Length: ").Append(length.ToString(CultureInfo.InvariantCulture)).Append("\r\n")
                    .Append("Accept-Ranges: bytes\r\n");

                if (partial)
                {
                    header.Append("Content-Range: bytes ")
                          .Append(start.ToString(CultureInfo.InvariantCulture)).Append('-')
                          .Append(end.ToString(CultureInfo.InvariantCulture)).Append('/')
                          .Append(_clip.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                }

                header.Append("Cache-Control: no-store\r\nConnection: close\r\n\r\n");

                await stream.WriteAsync(Encoding.ASCII.GetBytes(header.ToString())).ConfigureAwait(false);

                if (method == "GET" && length > 0)
                {
                    await stream.WriteAsync(_clip.AsMemory(start, length)).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);

                    if (start == 0 && end == _clip.Length - 1)
                    {
                        _served.TrySetResult();
                    }
                }
            }
            catch (Exception)
            {
                // Connexion coupée par l'autre côté : rien à faire.
            }
        }
    }

    /// <summary>Lit la ligne de requête et les en-têtes. Null quand ce n'est pas une requête HTTP lisible.</summary>
    private static async Task<(string Method, string Path, string? Range)?> ReadHeaderAsync(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(HeaderTimeout);
        var buffer = new byte[MaxHeaderBytes];
        var filled = 0;

        while (filled < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), timeout.Token).ConfigureAwait(false);

            if (read == 0)
            {
                return null;
            }

            filled += read;

            var text = Encoding.ASCII.GetString(buffer, 0, filled);
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);

            if (end < 0)
            {
                continue;
            }

            var lines = text[..end].Split("\r\n");
            var parts = lines[0].Split(' ');

            if (parts.Length < 2)
            {
                return null;
            }

            string? range = null;

            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':');

                if (colon > 0 && line[..colon].Trim().Equals("Range", StringComparison.OrdinalIgnoreCase))
                {
                    range = line[(colon + 1)..].Trim();
                }
            }

            // La requête peut porter une chaîne de requête : seul le chemin compte.
            var path = parts[1];
            var query = path.IndexOf('?');

            return (parts[0].ToUpperInvariant(), query >= 0 ? path[..query] : path, range);
        }

        return null;
    }

    /// <summary>
    /// Une seule plage « bytes=a-b », « bytes=a- » ou « bytes=-n ». Sans plage, ou
    /// avec une forme non prise en charge, tout le clip. Début négatif : plage
    /// impossible (416).
    /// </summary>
    internal static (int Start, int End, bool Partial) ParseRange(string? range, int length)
    {
        if (length == 0)
        {
            return (0, -1, false);
        }

        if (string.IsNullOrWhiteSpace(range) ||
            !range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) ||
            range.Contains(','))
        {
            return (0, length - 1, false);
        }

        var spec = range[6..].Trim();
        var dash = spec.IndexOf('-');

        if (dash < 0)
        {
            return (0, length - 1, false);
        }

        var first = spec[..dash].Trim();
        var last = spec[(dash + 1)..].Trim();

        if (first.Length == 0)
        {
            // Les n derniers octets.
            if (!int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix <= 0)
            {
                return (0, length - 1, false);
            }

            return (Math.Max(0, length - suffix), length - 1, true);
        }

        if (!int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var start))
        {
            return (0, length - 1, false);
        }

        if (start >= length)
        {
            return (-1, -1, true);
        }

        var end = length - 1;

        if (last.Length > 0 && int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var to))
        {
            end = Math.Min(to, length - 1);
        }

        return end < start ? (-1, -1, true) : (start, end, true);
    }

    private static Task WriteStatusAsync(NetworkStream stream, int code, string reason)
        => stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {code} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")).AsTask();
}

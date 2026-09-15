using System.Net.WebSockets;
using System.Text;
using System.Xml.Linq;
using SoundReborn.Core.Internal;
using SoundReborn.Core.Models;

namespace SoundReborn.Core;

/// <summary>
/// Connexion au bus de notifications de l'enceinte : WebSocket sur le port 8080,
/// sous-protocole « gabbo ». Sans ce sous-protocole, le firmware refuse la poignée
/// de main. Le flux est en lecture seule ici : l'enceinte y pousse ses changements
/// d'état, ce qui évite d'interroger /now_playing en boucle.
///
/// Le firmware ferme une connexion inactive au bout d'environ 5 minutes, d'où le
/// ping périodique et la reconnexion automatique avec attente croissante.
/// </summary>
public sealed class GabboSocket : IAsyncDisposable
{
    public const int DefaultPort = 8080;

    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Délai accordé au Pong. Sans lui, .NET envoie des Ping sans jamais vérifier
    /// qu'on lui répond : après une mise en veille du téléphone, ReceiveAsync reste
    /// bloqué indéfiniment, l'interface continue d'afficher « direct », et le repli
    /// par sondage ne démarre pas puisqu'il dépend du même indicateur.
    /// </summary>
    private static readonly TimeSpan PongTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly string _host;
    private readonly int _port;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public GabboSocket(string host, int port = DefaultPort)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
    }

    /// <summary>Volume mis à jour par l'enceinte (ou par une autre télécommande).</summary>
    public event EventHandler<VolumeState>? VolumeChanged;

    /// <summary>
    /// « Le volume a changé, relis-le. » Certains firmwares (27.0.6 au moins)
    /// envoient un volumeUpdated vide : il annonce l'évènement sans le décrire.
    /// On le traite comme presetsUpdated, en invitant à relire plutôt qu'en
    /// prétendant connaître la nouvelle valeur.
    /// </summary>
    public event EventHandler? VolumeStale;

    /// <summary>Nouveau morceau, nouvelle source, pause, ...</summary>
    public event EventHandler<NowPlaying>? NowPlayingChanged;

    /// <summary>Une présélection a été enregistrée ou effacée : la liste est à relire.</summary>
    public event EventHandler? PresetsChanged;

    /// <summary>La zone multi-pièces a changé : la composition du groupe est à relire.</summary>
    public event EventHandler? ZoneChanged;

    /// <summary>Passe à vrai dès que la poignée de main a abouti, à faux à chaque coupure.</summary>
    public event EventHandler<bool>? ConnectionChanged;

    public bool IsConnected { get; private set; }

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);

        try
        {
            if (_loop is not null)
            {
                await _loop.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt demandé, rien à signaler.
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _loop = null;
            SetConnected(false);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);

        while (!ct.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            socket.Options.AddSubProtocol("gabbo");
            socket.Options.KeepAliveInterval = PingInterval;
            socket.Options.KeepAliveTimeout = PongTimeout;

            try
            {
                await socket.ConnectAsync(new Uri($"ws://{_host}:{_port}/"), ct).ConfigureAwait(false);
                SetConnected(true);
                backoff = TimeSpan.FromSeconds(1);

                await ReceiveLoopAsync(socket, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Enceinte en veille, Wi-Fi coupé, agent redémarré : on retente.
            }
            finally
            {
                SetConnected(false);
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, MaxBackoff.TotalMilliseconds));
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var message = new StringBuilder();

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (!result.EndOfMessage)
            {
                continue;
            }

            var payload = message.ToString();
            message.Clear();

            try
            {
                Dispatch(payload);
            }
            catch (Exception)
            {
                // Une trame inattendue ne doit pas tuer la connexion.
            }
        }
    }

    /// <summary>
    /// Les trames ont la forme :
    /// &lt;updates deviceID="..."&gt;&lt;volumeUpdated&gt;&lt;volume&gt;...&lt;/volume&gt;&lt;/volumeUpdated&gt;&lt;/updates&gt;
    /// </summary>
    private void Dispatch(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        var root = XDocument.Parse(payload).Root;

        if (root is null)
        {
            return;
        }

        // Le message « userActivityUpdate » et les accusés de réception n'ont pas d'enfant utile.
        var updates = root.Name.LocalName.Equals("updates", StringComparison.OrdinalIgnoreCase)
            ? root.Elements()
            : new[] { root }.AsEnumerable();

        foreach (var update in updates)
        {
            switch (update.Name.LocalName)
            {
                case "volumeUpdated":
                    var volume = update.Element("volume");

                    if (volume is not null && volume.HasElements)
                    {
                        VolumeChanged?.Invoke(this, FirmwareXml.ParseVolume(volume));
                    }
                    else
                    {
                        // Trame sans contenu : on ne sait que « ça a bougé ».
                        VolumeStale?.Invoke(this, EventArgs.Empty);
                    }

                    break;

                case "nowPlayingUpdated":
                    var nowPlaying = update.Element("nowPlaying");
                    if (nowPlaying is not null)
                    {
                        NowPlayingChanged?.Invoke(this, FirmwareXml.ParseNowPlaying(nowPlaying));
                    }

                    break;

                case "presetsUpdated":
                case "presetUpdated":
                    PresetsChanged?.Invoke(this, EventArgs.Empty);
                    break;

                case "zoneUpdated":
                    ZoneChanged?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
    }

    private void SetConnected(bool connected)
    {
        if (IsConnected == connected)
        {
            return;
        }

        IsConnected = connected;
        ConnectionChanged?.Invoke(this, connected);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiffieHellmanP2PChat
{

    public class NewPeerConnectedEventArgs : EventArgs
    {
        public string PeerName { get; init; }
        public string PeerAddress { get; init; }
    }

    public class NewChatMessageEventArgs : EventArgs
    {
        public string PeerName { get; init; }
        public string Message { get; init; }
    }

    public class Chat
    {
        private List<Peer> peerConnections = new List<Peer>();
        private readonly CancellationToken ct;
        private readonly string localIpAddress;
        private int? myIndex;

        public Chat(string localIpAddress, CancellationToken ct)
        {
            this.localIpAddress = localIpAddress;
            this.ct = ct;
        }

        public IEnumerable<Peer> PeerConnections => peerConnections;
        public string MyIpAddress => localIpAddress;
        public int? MyIndex { get => myIndex; set => myIndex = value; }

        public async Task StartAcceptingPeerConnections()
        {
            var connectionListener = new TcpListener(IPEndPoint.Parse(this.localIpAddress));
            connectionListener.Start();

            while (!ct.IsCancellationRequested)
            {
                var newConnection = await connectionListener.AcceptTcpClientAsync(ct);
                if (newConnection != null)
                {
                    OnNewChatMessageReceived($"Someone connected from {newConnection.Client.RemoteEndPoint}");
                    StartListeningToPeerConnection(newConnection);
                }
            }

            connectionListener?.Stop();
        }

        private Peer StartListeningToPeerConnection(TcpClient newConnection)
        {
            Peer newPeer = new Peer(newConnection, ct, this);
            //this.peerConnections.Add(newPeer);
            var t = Task.Run(newPeer.ServeAsync, ct);
            return newPeer;
        }

        public async Task<Peer> ConnectToPeerByAddressAsync(string ipAddress)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPEndPoint.Parse(ipAddress), ct);

            var peer = StartListeningToPeerConnection(client);
            peer.IpAddress = ipAddress;

            if (this.MyIndex == null)
            {
                await peer.SendAsync(new ListPeers());
            }
            return peer;
        }

        // TODO: clear connection list
        public event EventHandler<NewPeerConnectedEventArgs> OnNewPeerConnected;
        public event EventHandler<NewChatMessageEventArgs> OnNewChatMessage;

        public void OnNewChatMessageReceived(string text)
        {
            OnNewChatMessage?.Invoke(this, new NewChatMessageEventArgs
            {
                Message = text
            });
        }

        public void AddPeerToCollection(Peer peer)
        {
            this.peerConnections.Add(peer);
            OnNewChatMessageReceived("New peer member: " + peer.IpAddress);
            OnNewChatMessageReceived("Peers are: " + String.Join(
                Environment.NewLine,
                this.peerConnections.Select(p => $"#{p.Index} {p.IpAddress}")));
        }
    }

    public class Peer
    {
        private Chat chat;
        private TcpClient tcpClient;
        private CancellationToken ct;
        private int index;
        private string ipAddress;

        public Peer(TcpClient tcpClient, CancellationToken ct, Chat chat)
        {
            this.tcpClient = tcpClient;
            this.ct = ct;
            this.chat = chat;
        }

        public string IpAddress { get => ipAddress; set => ipAddress = value; }
        public int Index { get => index; set => index = value; }

        public async Task ServeAsync()
        {
            try
            {
                var stream = tcpClient.GetStream();
                var reader = new StreamReader(stream);
                //var writer = new StreamWriter(stream);

                string? nextLine = null;
                do
                {
                    nextLine = await reader.ReadLineAsync();
                    if (nextLine != null)
                    {
                        await HandleCommand(nextLine);
                    }
                }
                while (!ct.IsCancellationRequested && nextLine != null);
            }
            catch (OperationCanceledException)
            {
                this.tcpClient.Close();
            }
        }

        private async Task HandleCommand(string json)
        {
            JObject jObject = JObject.Parse(json);
            switch (jObject["Type"]?.Value<string>())
            {
                case ListPeers.COMMAND_NAME:
                    this.chat.OnNewChatMessageReceived("Sending list of peers");
                    // Possibly can refuse or delay connection when there 
                    // is at least one peer with 'null' index
                    this.chat.MyIndex ??= 0;
                    var myself = new PeerInfo
                    {
                        Index = this.chat.MyIndex.Value,
                        IpAddress = this.chat.MyIpAddress
                    };
                    await SendAsync(new HereAreThePeers
                    {
                        Peers = this.chat.PeerConnections
                            .Select(p => new PeerInfo
                            {
                                Index = p.index,
                                IpAddress = p.ipAddress
                            })
                            .Append(myself)
                            .OrderBy(p => p.Index),
                        YourIndex = this.chat.PeerConnections.Count() + 1,
                        MyIndex = this.chat.MyIndex.Value
                    });
                    break;
                case HereAreThePeers.COMMAND_NAME:
                    this.chat.OnNewChatMessageReceived("Got list of peers");
                    HereAreThePeers response = jObject.ToObject<HereAreThePeers>()!;
                    int myIndex = response.Peers.Count();
                    this.chat.MyIndex = myIndex;
                    var helloAddMeToYourPeersList = new HelloAddMeToYourPeersList
                    {
                        MyIndex = myIndex,
                        MyIpAddress = this.chat.MyIpAddress
                    };
                    foreach (var peer in response.Peers)
                    {
                        var peerConnection = peer.Index == response.MyIndex
                            ? this
                            : await chat.ConnectToPeerByAddressAsync(peer.IpAddress);
                        peerConnection.index = peer.Index;
                        await peerConnection.SendAsync(helloAddMeToYourPeersList);
                    }
                    break;
                case HelloAddMeToYourPeersList.COMMAND_NAME:
                    var addingRequest = jObject.ToObject<HelloAddMeToYourPeersList>();
                    this.chat.OnNewChatMessageReceived("Adding stranger to the list");
                    this.index = addingRequest.MyIndex;
                    this.ipAddress = addingRequest.MyIpAddress;
                    this.chat.AddPeerToCollection(this);
                    var youAreIn = new OkeyYouAreIn
                    {
                        MyIndex = this.chat.MyIndex.Value,
                    };
                    await SendAsync(youAreIn);
                    break;
                case OkeyYouAreIn.COMMAND_NAME:
                    OkeyYouAreIn okeyYoureIn = jObject.ToObject<OkeyYouAreIn>()!;
                    this.index = okeyYoureIn.MyIndex;
                    this.chat.OnNewChatMessageReceived("Got approval from " + okeyYoureIn.MyIndex);
                    this.chat.AddPeerToCollection(this);
                    break;
            }
        }

        public async Task SendAsync<T>(T data) where T : PeerCommand
        {
            string json = JsonConvert.SerializeObject(data);
            var writer = new StreamWriter(tcpClient.GetStream());
            await writer.WriteLineAsync(json);
            await writer.FlushAsync();
        }
    }

    public abstract class PeerCommand
    {
        public abstract string Type { get; }
    }

    public class ListPeers : PeerCommand
    {
        public const string COMMAND_NAME = "list peers";

        public override string Type { get; } = COMMAND_NAME;
    }

    public class HereAreThePeers : PeerCommand
    {
        public const string COMMAND_NAME = "here are the peers";
        public override string Type { get; } = COMMAND_NAME;
        public IEnumerable<PeerInfo> Peers { get; set; }
        public int YourIndex { get; set; }
        public int MyIndex { get; set; }
    }
    public class PeerInfo
    {
        public int Index { get; set; }
        public string IpAddress { get; set; } = null!;
    }

    public class HelloAddMeToYourPeersList : PeerCommand
    {
        public const string COMMAND_NAME = "add to me to peers list";
        public override string Type { get; } = COMMAND_NAME;
        public int MyIndex { get; set; }
        public string MyIpAddress { get; set; }
    }

    public class OkeyYouAreIn : PeerCommand
    {
        public const string COMMAND_NAME = "ok you're in";
        public override string Type { get; } = COMMAND_NAME;
        public int MyIndex { get; set; }
    }
}

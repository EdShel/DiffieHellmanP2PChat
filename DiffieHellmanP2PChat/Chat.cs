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
using System.Numerics;
using DiffieHellman;
using System.Text;
using System.Text.RegularExpressions;

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
        private DiffieHellmanAlgorithm? secretDh;
        private DiffieHellmanAlgorithm? sharedSecretDh;

        public Chat(string localIpAddress, CancellationToken ct)
        {
            this.localIpAddress = localIpAddress;
            this.ct = ct;
        }

        public IEnumerable<Peer> PeerConnections => peerConnections;
        public string MyIpAddress => localIpAddress;
        public int? MyIndex { get => myIndex; set => myIndex = value; }
        public bool CanSendMessage => sharedSecretDh != null;
        public DiffieHellmanAlgorithm? SecretDh { get => this.secretDh; set => this.secretDh = value; }
        public DiffieHellmanAlgorithm? SharedSecretDh { get => this.sharedSecretDh; set => this.sharedSecretDh = value; }

        public async Task StartAcceptingPeerConnections()
        {
            var endpoint = IPEndPoint.Parse(this.localIpAddress);
            var connectionListener = new TcpListener(endpoint);
            connectionListener.Start();

            OnNewChatMessageReceived($"You are {NicknameStore.GetNickname(endpoint.Port)}");
            OnNewChatMessageReceived($"Please enter address of a peer...");

            while (!ct.IsCancellationRequested)
            {
                var newConnection = await connectionListener.AcceptTcpClientAsync(ct);
                if (newConnection != null)
                {
                    StartListeningToPeerConnection(newConnection);
                }
            }

            connectionListener?.Stop();
        }

        private Peer StartListeningToPeerConnection(TcpClient newConnection)
        {
            Peer newPeer = new Peer(newConnection, ct, this);
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
            lock (this.peerConnections)
            {
                this.peerConnections.Add(peer);
                this.SecretDh = null;
                this.SharedSecretDh = null;
            }
        }

        public async Task SendMessage(string messageText)
        {
            if (this.SharedSecretDh == null || !this.myIndex.HasValue)
            {
                return;
            }
            var plaintextBytes = Encoding.UTF8.GetBytes(messageText);
            var ciphertextBytes = this.SharedSecretDh.Encrypt(plaintextBytes);
            var ciphertextBase64 = Convert.ToBase64String(ciphertextBytes);
            var chatMessage = new ChatMessageCommand
            {
                Base64Message = ciphertextBase64
            };

            foreach (var peer in this.peerConnections)
            {
                await peer.SendAsync(chatMessage);
            }

            var message = $"You: {messageText}";
            this.OnNewChatMessageReceived(message);
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
        public int Port => PortParser.Parse(IpAddress);

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

                    this.chat.OnNewChatMessageReceived("You connected to "
                        + string.Join(", ", response.Peers.Select(p => NicknameStore.GetNickname(PortParser.Parse(p.IpAddress)))));
                    break;
                case HelloAddMeToYourPeersList.COMMAND_NAME:
                    var addingRequest = jObject.ToObject<HelloAddMeToYourPeersList>();
                    this.index = addingRequest.MyIndex;
                    this.ipAddress = addingRequest.MyIpAddress;
                    this.chat.AddPeerToCollection(this);
                    var youAreIn = new OkeyYouAreIn
                    {
                        MyIndex = this.chat.MyIndex.Value,
                    };
                    await SendAsync(youAreIn);

                    this.chat.OnNewChatMessageReceived(
                        $"{NicknameStore.GetNickname(this.Port)} has joined the chat");
                    break;
                case OkeyYouAreIn.COMMAND_NAME:
                    try
                    {
                        Monitor.Enter(this.chat);
                        OkeyYouAreIn okeyYoureIn = jObject.ToObject<OkeyYouAreIn>()!;
                        this.index = okeyYoureIn.MyIndex;
                        this.chat.AddPeerToCollection(this);

                        await GenerateDhNumbersAndPassThemToTheNext();
                    }
                    finally
                    {
                        Monitor.Exit(this.chat);
                    }
                    break;
                case CalculateDiffieHellmanKeyAndPassItToTheNext.COMMAND_NAME:
                    var dh = jObject.ToObject<CalculateDiffieHellmanKeyAndPassItToTheNext>();
                    if (this.chat.SecretDh == null)
                    {
                        this.chat.SecretDh = DiffieHellmanAlgorithm.CreateFromKnownNumbers(dh.P, dh.G);
                    }
                    var poweredByMe = this.chat.SecretDh.CreateFromPublicKey(dh.Base);

                    if (dh.SharedFor == this.chat.MyIndex)
                    {
                        this.chat.SharedSecretDh = poweredByMe;

                        int peersCount = this.chat.PeerConnections.Count() + 1;
                        if (dh.PeersFinishedCount < peersCount)
                        {
                            var command = new CalculateDiffieHellmanKeyAndPassItToTheNext
                            {
                                G = dh.G,
                                P = dh.P,
                                Base = this.chat.SecretDh!.Key,
                                PeersFinishedCount = dh.PeersFinishedCount + 1,
                                SharedFor = GetPreviousPeerIndex(),
                            };

                            var nextPeer = this.chat.PeerConnections.First(
                                p => p.index == GetNextPeerIndex());
                            await nextPeer.SendAsync(command);
                        }
                        return;
                    }

                    var justPowered = new CalculateDiffieHellmanKeyAndPassItToTheNext
                    {
                        G = dh.G,
                        P = dh.P,
                        Base = poweredByMe.Key,
                        PeersFinishedCount = dh.PeersFinishedCount,
                        SharedFor = dh.SharedFor,
                    };
                    var nextPeerToPass = this.chat.PeerConnections.First(
                        p => p.index == GetNextPeerIndex());
                    await nextPeerToPass.SendAsync(justPowered);

                    break;
                case ChatMessageCommand.COMMAND_NAME:
                    if (this.chat.SharedSecretDh == null)
                    {
                        return;
                    }
                    var chatMessage = jObject.ToObject<ChatMessageCommand>()!;
                    var ciphertextBytes = Convert.FromBase64String(chatMessage.Base64Message);
                    var plaintextBytes = this.chat.SharedSecretDh.Decrypt(ciphertextBytes);
                    var plaintext = Encoding.UTF8.GetString(plaintextBytes);
                    var message = NicknameStore.GetNickname(this.Port) + ": " + plaintext;
                    this.chat.OnNewChatMessageReceived(message);
                    break;
            }
        }

        private async Task GenerateDhNumbersAndPassThemToTheNext()
        {
            bool hasEveryoneAccepted = this.chat.PeerConnections.Count() == this.chat.MyIndex;
            if (hasEveryoneAccepted)
            {
                this.chat.SecretDh = DiffieHellmanAlgorithm.Create();
                var nextPeer = this.chat.PeerConnections.First(
                    p => p.index == GetNextPeerIndex());
                var command = new CalculateDiffieHellmanKeyAndPassItToTheNext
                {
                    G = this.chat.SecretDh!.G,
                    P = this.chat.SecretDh!.P,
                    Base = this.chat.SecretDh!.Key,
                    SharedFor = GetPreviousPeerIndex()
                };
                await nextPeer.SendAsync(command);
            }
        }

        private int GetNextPeerIndex()
        {
            int peersCount = this.chat.PeerConnections.Count() + 1;
            return (this.chat.MyIndex.Value + 1) % peersCount;
        }
        private int GetPreviousPeerIndex()
        {
            int peersCount = this.chat.PeerConnections.Count() + 1;
            return ((this.chat.MyIndex.Value - 1) + peersCount) % peersCount;
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

    public class CalculateDiffieHellmanKeyAndPassItToTheNext : PeerCommand
    {
        public const string COMMAND_NAME = "calculate dh";
        public override string Type { get; } = COMMAND_NAME;
        public BigInteger G { get; set; }
        public BigInteger P { get; set; }
        public BigInteger Base { get; set; }
        public int SharedFor { get; set; }
        public int PeersFinishedCount { get; set; }
    }

    public class ChatMessageCommand : PeerCommand
    {
        public const string COMMAND_NAME = "chat message";
        public override string Type { get; } = COMMAND_NAME;
        public string Base64Message { get; set; } = null!;
    }

    public static class PortParser
    {
        public static int Parse(string ip)
        {
            return int.Parse(new Regex(@":(\d+)$").Match(ip).Groups[1].Value);
        }
    }
}

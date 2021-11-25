using DiffieHellman;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiffieHellmanP2PChat
{
    public class NewChatMessageEventArgs : EventArgs
    {
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

        public void OnPeerConnectionLost(Peer peer)
        {
            int peerIndex = peer.Index;
            if (peerIndex < this.MyIndex)
            {
                this.MyIndex--;
            }
            foreach (var nextPeer in this.peerConnections.Where(p => peerIndex < p.Index))
            {
                nextPeer.Index--;
            }
            lock (this)
            {
                this.peerConnections.Remove(peer);
            }
            var quitUserName = NicknameStore.GetNickname(PortParser.Parse(peer.IpAddress));
            OnNewChatMessageReceived($"{quitUserName} left the conversation");
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
}

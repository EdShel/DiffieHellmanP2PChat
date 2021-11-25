using DiffieHellman;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiffieHellmanP2PChat
{
    public class Peer
    {
        private readonly Chat chat;
        private readonly TcpClient tcpClient;
        private readonly CancellationToken ct;
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
            catch (Exception)
            {
                this.chat.OnPeerConnectionLost(this);
            }
            finally
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
                    await HandlePeersListRequest();
                    break;
                case HereAreThePeers.COMMAND_NAME:
                    await HandleReceivedPeersList(jObject);
                    break;
                case HelloAddMeToYourPeersList.COMMAND_NAME:
                    await HandleConnectionRequest(jObject);
                    break;
                case OkeyYouAreIn.COMMAND_NAME:
                    await HandlePeerAcceptedMe(jObject);
                    break;
                case CalculateDiffieHellmanKeyAndPassItToTheNext.COMMAND_NAME:
                    await HandleCalculatingDiffieHellmanKey(jObject);
                    break;
                case ChatMessageCommand.COMMAND_NAME:
                    HendleChatMessageReceived(jObject);
                    break;
            }
        }

        private async Task HandlePeersListRequest()
        {
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
                MyIndex = this.chat.MyIndex.Value
            });
        }

        private async Task HandleReceivedPeersList(JObject jObject)
        {
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

            var peersNames = response.Peers.Select(
                p => NicknameStore.GetNickname(PortParser.Parse(p.IpAddress)));
            this.chat.OnNewChatMessageReceived("You connected to " + string.Join(", ", peersNames));
        }

        private async Task HandleConnectionRequest(JObject jObject)
        {
            var requestToConnect = jObject.ToObject<HelloAddMeToYourPeersList>();
            this.index = requestToConnect.MyIndex;
            this.ipAddress = requestToConnect.MyIpAddress;
            this.chat.AddPeerToCollection(this);
            var youAreIn = new OkeyYouAreIn
            {
                MyIndex = this.chat.MyIndex.Value,
            };
            await SendAsync(youAreIn);

            this.chat.OnNewChatMessageReceived(
                $"{NicknameStore.GetNickname(this.Port)} has joined the conversation");
        }

        private async Task HandlePeerAcceptedMe(JObject jObject)
        {
            try
            {
                Monitor.Enter(this.chat);
                OkeyYouAreIn connectionResponse = jObject.ToObject<OkeyYouAreIn>()!;
                this.index = connectionResponse.MyIndex;
                this.chat.AddPeerToCollection(this);

                await GenerateDhNumbersAndPassThemToTheNext();
            }
            finally
            {
                Monitor.Exit(this.chat);
            }
        }

        private async Task HandleCalculatingDiffieHellmanKey(JObject jObject)
        {
            var dh = jObject.ToObject<CalculateDiffieHellmanKeyAndPassItToTheNext>()!;
            if (this.chat.MySecretDh == null)
            {
                this.chat.MySecretDh = DiffieHellmanAlgorithm.CreateFromKnownNumbers(dh.P, dh.G);
            }

            var poweredByMe = this.chat.MySecretDh.CreateFromPublicKey(dh.Base);

            if (dh.SharedFor == this.chat.MyIndex)
            {
                this.chat.SharedSecretDh = poweredByMe;

                int peersCount = this.chat.PeerConnections.Count() + 1;
                bool notAllPeersHaveSharedKeyYet = dh.PeersFinishedCount < peersCount;
                if (notAllPeersHaveSharedKeyYet)
                {
                    var command = new CalculateDiffieHellmanKeyAndPassItToTheNext
                    {
                        G = dh.G,
                        P = dh.P,
                        Base = this.chat.MySecretDh!.Key,

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
        }

        private void HendleChatMessageReceived(JObject jObject)
        {
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
        }

        private async Task GenerateDhNumbersAndPassThemToTheNext()
        {
            bool hasEveryoneAccepted = this.chat.PeerConnections.Count() == this.chat.MyIndex;
            if (hasEveryoneAccepted)
            {
                this.chat.MySecretDh = DiffieHellmanAlgorithm.Create();
                var nextPeer = this.chat.PeerConnections.First(
                    p => p.index == GetNextPeerIndex());
                var command = new CalculateDiffieHellmanKeyAndPassItToTheNext
                {
                    G = this.chat.MySecretDh!.G,
                    P = this.chat.MySecretDh!.P,
                    Base = this.chat.MySecretDh!.Key,
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
}

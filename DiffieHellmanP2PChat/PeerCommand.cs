using System.Collections.Generic;
using System.Numerics;

namespace DiffieHellmanP2PChat
{
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
}

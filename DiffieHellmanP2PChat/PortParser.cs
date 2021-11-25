using System.Text.RegularExpressions;

namespace DiffieHellmanP2PChat
{
    public static class PortParser
    {
        public static int Parse(string ip)
        {
            return int.Parse(new Regex(@":(\d+)$").Match(ip).Groups[1].Value);
        }
    }
}

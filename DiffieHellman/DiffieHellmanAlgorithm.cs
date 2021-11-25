using System.Numerics;
using System.Security.Cryptography;

namespace DiffieHellman
{
    public class DiffieHellmanAlgorithm
    {
        private const int KEY_LENGTH_BITS = 128;

        private readonly BigInteger p;
        private readonly BigInteger g;
        private readonly BigInteger a;

        private readonly BigInteger key;

        private DiffieHellmanAlgorithm(BigInteger p, BigInteger g, BigInteger a)
        {
            this.p = p;
            this.g = g;
            this.a = a;
            this.key = BigInteger.ModPow(this.g, a, this.p);
        }

        public BigInteger P => p;
        public BigInteger G => g;
        public BigInteger Key => key;

        public static DiffieHellmanAlgorithm Create()
        {
            var rng = new RandomBigIntegerGenerator();
            BigInteger p = rng.NextPrimeNumber(KEY_LENGTH_BITS);
            BigInteger g = rng.GetPrimitiveRoot(p);
            BigInteger a = rng.NextBigInteger(KEY_LENGTH_BITS);
            return new DiffieHellmanAlgorithm(p, g, a);
        }

        public static DiffieHellmanAlgorithm CreateFromKnownNumbers(BigInteger p, BigInteger g)
        {
            var secretKey = new RandomBigIntegerGenerator().NextBigInteger(8 * p.GetByteCount());
            return new DiffieHellmanAlgorithm(p, g, secretKey);
        }

        public DiffieHellmanAlgorithm CreateFromPublicKey(BigInteger publicKey)
        {
            return new DiffieHellmanAlgorithm(this.p, publicKey, this.a);
        }

        public byte[] Encrypt(byte[] data)
        {
            var sharedSecretKey = this.key.ToByteArray();
            var aes = Aes.Create();
            var encryptor = aes.CreateEncryptor(sharedSecretKey, new byte[16]);
            using var ms = new MemoryStream();
            using var crypto = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
            crypto.Write(data);
            crypto.FlushFinalBlock();
            return ms.ToArray();
        }
        public byte[] Decrypt(byte[] data)
        {
            var sharedSecretKey = this.key.ToByteArray();
            var aes = Aes.Create();
            var decryptor = aes.CreateDecryptor(sharedSecretKey, new byte[16]);
            using var ms = new MemoryStream(data);
            using var crypto = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var rs = new MemoryStream();
            crypto.CopyTo(rs);
            return rs.ToArray();
        }
    }
}

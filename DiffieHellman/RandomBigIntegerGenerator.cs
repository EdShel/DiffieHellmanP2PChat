// https://github.com/arupmondal-cs/BigInteger-Random-Number-Generator-and-Prime-Test/blob/master/BigIntRandomGen.cs
using System.Numerics;
using System.Security.Cryptography;

namespace DiffieHellman
{
    class RandomBigIntegerGenerator
    {
        private Random rnd = new Random(6);

        public BigInteger NextPrimeNumber(int bitLength)
        {
            while (true)
            {
                BigInteger randomNumber = NextBigInteger(bitLength);
                if (randomNumber.IsProbablePrime(256))
                {
                    return randomNumber;
                }
            }
        }

        public BigInteger GetPrimitiveRoot(BigInteger p)
        {
            BigInteger phi = p - 1, n = phi;
            List<BigInteger> fact = FindPrimeFactorsFast(ref n);

            while (true)
            {
                for (BigInteger res = NextBigInteger(128); res <= p; ++res)
                {
                    bool ok = true;
                    for (int i = 0; i < fact.Count && ok; ++i)
                        ok &= BigInteger.ModPow(res, phi / fact[i], p) != 1;
                    if (ok) return res;
                }
            }
        }

        private static List<BigInteger> FindPrimeFactors(ref BigInteger n)
        {
            List<BigInteger> fact = new List<BigInteger>();
            for (BigInteger i = 2; i * i <= n; ++i)
                if (n % i == 0)
                {
                    fact.Add(i);
                    while (n % i == 0)
                        n /= i;
                }
            if (n > 1)
                fact.Add(n);
            return fact;
        }

        private static List<BigInteger> FindPrimeFactorsFast(ref BigInteger n)
        {
            List<BigInteger> factors = new List<BigInteger>();
            for (BigInteger i = 2; i < n; i++)
            {
                while (n % i == 0)
                {

                    factors.Add(i);
                    n /= i;
                    if (n.IsProbablePrime(20))
                        return factors;
                }
            }
            return factors;
        }

        public BigInteger NextBigInteger(int bitLength)
        {
            if (bitLength < 1) return BigInteger.Zero;

            int bytes = bitLength / 8;
            int bits = bitLength % 8;

            // Generates enough random bytes to cover our bits.
            byte[] bs = new byte[bytes + 1];
            rnd.NextBytes(bs);

            // Mask out the unnecessary bits.
            byte mask = (byte)(0xFF >> (8 - bits));
            bs[bs.Length - 1] &= mask;

            return new BigInteger(bs);
        }

        // Random Integer Generator within the given range
        public BigInteger RandomBigInteger(BigInteger start, BigInteger end)
        {
            if (start == end) return start;

            BigInteger res = end;

            // Swap start and end if given in reverse order.
            if (start > end)
            {
                end = start;
                start = res;
                res = end - start;
            }
            else
                // The distance between start and end to generate a random BigIntger between 0 and (end-start) (non-inclusive).
                res -= start;

            byte[] bs = res.ToByteArray();

            // Count the number of bits necessary for res.
            int bits = 8;
            byte mask = 0x7F;
            while ((bs[bs.Length - 1] & mask) == bs[bs.Length - 1])
            {
                bits--;
                mask >>= 1;
            }
            bits += 8 * bs.Length;

            // Generate a random BigInteger that is the first power of 2 larger than res, 
            // then scale the range down to the size of res,
            // finally add start back on to shift back to the desired range and return.
            return ((NextBigInteger(bits + 1) * res) / BigInteger.Pow(2, bits + 1)) + start;
        }
    }


    // Miller-Rabin primality test as an extension method on the BigInteger type.
    // Based on the Ruby implementation on this page.
    public static class BigIntegerPrimeTest
    {
        public static bool IsProbablePrime(this BigInteger source, int certainty)
        {
            if (source == 2 || source == 3)
                return true;
            if (source < 2 || source % 2 == 0)
                return false;

            BigInteger d = source - 1;
            int s = 0;

            while (d % 2 == 0)
            {
                d /= 2;
                s += 1;
            }

            // There is no built-in method for generating random BigInteger values.
            // Instead, random BigIntegers are constructed from randomly generated
            // byte arrays of the same length as the source.
            RandomNumberGenerator rng = RandomNumberGenerator.Create();
            byte[] bytes = new byte[source.ToByteArray().LongLength];
            BigInteger a;

            for (int i = 0; i < certainty; i++)
            {
                do
                {
                    // This may raise an exception in Mono 2.10.8 and earlier.
                    // http://bugzilla.xamarin.com/show_bug.cgi?id=2761
                    rng.GetBytes(bytes);
                    a = new BigInteger(bytes);
                }
                while (a < 2 || a >= source - 2);

                BigInteger x = BigInteger.ModPow(a, d, source);
                if (x == 1 || x == source - 1)
                    continue;

                for (int r = 1; r < s; r++)
                {
                    x = BigInteger.ModPow(x, 2, source);
                    if (x == 1)
                        return false;
                    if (x == source - 1)
                        break;
                }

                if (x != source - 1)
                    return false;
            }

            return true;
        }
    }
}

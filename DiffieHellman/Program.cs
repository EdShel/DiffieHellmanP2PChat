using DiffieHellman;
using System.Text;

var dhA = DiffieHellmanAlgorithm.Create();
var A = dhA.Key;

var g = dhA.G;
var p = dhA.P;

var dhB = DiffieHellmanAlgorithm.CreateFromKnownNumbers(p, g);
var B = dhB.Key;

var sharedKeyOfA = dhA.CreateFromPublicKey(B).Key;
var sharedKeyOfB = dhB.CreateFromPublicKey(A).Key;

Console.WriteLine("P: " + p.ToString());
Console.WriteLine("G: " + g.ToString());

Console.WriteLine($"The shared secret keys are equal: {sharedKeyOfA == sharedKeyOfB}");

string message = "Hello, world!";
var ciphertext = dhA.Encrypt(Encoding.UTF8.GetBytes(message));
var plaintext = Encoding.UTF8.GetString(dhA.Decrypt(ciphertext));

Console.WriteLine($"Original message is: '{message}'");
Console.WriteLine($"Decoded message is : '{plaintext}'");
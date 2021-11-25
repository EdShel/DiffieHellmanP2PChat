namespace DiffieHellmanP2PChat
{
    public class NicknameStore
    {
        private static readonly string[] adjectives = new[]
        {
            "Brave",
            "Coward",
            "Strong",
            "High",
            "Golden",
            "Heavy",
            "Mighty",
            "Handsome",
            "Fast",
            "Slow",
            "Red",
            "Green",
            "Black",
            "Orange",
            "Attractive",
            "Lazy",
            "Nasty",
        };

        private static readonly string[] animals = new[]
        {
            "Monkey 🙉",
            "Monke 🐒",
            "Gorilla 🦍",
            "Dog 🐕",
            "Orangutan 🦧",
            "Poodle 🐩",
            "Wolf 🐺",
            "Fox 🦊",
            "Raccoon 🦝",
            "Cat 🐱",
            "Lion 🦁",
            "Horse 🐴",
            "Unicorn 🦄",
            "Pig 🐷",
            "Goat 🐐",
            "Elephant 🐘",
            "Mouse 🐭",
            "Hamster 🐹",
            "Rabbit 🐰",
            "Bat 🦇",
            "Koala 🐨",
            "Duck 🦆",
            "Frog 🐸",
            "Parrot 🦜",
            "T-Rex 🦖",
        };

        public static string GetNickname(int index)
        {
            int adjectiveIndex = index & 0xf;
            int animalIndex = (index >> 8) % animals.Length;
            return $"{adjectives[adjectiveIndex]} {animals[animalIndex]}";
        }
    }
}

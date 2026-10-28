using System.Security.Cryptography;

namespace Pointage.Members;

public static class MemberCode
{
    // Sans O/0, I/1/L, B/8, S/5, Z/2 : un code lu ou recopié à la main ne se confond pas avec un autre caractère.
    public const string Alphabet = "ACDEFGHJKMNPQRTUVWXY34679";
    public const int Length = 6;

    public static string New() => new(RandomNumberGenerator.GetItems<char>(Alphabet, Length));

    public static string Normalize(string input) =>
        string.Concat(input.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}

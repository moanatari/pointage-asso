using System.Globalization;
using System.Text;

namespace Pointage.Members;

public static class SearchText
{
    // « Inès », « Jean-Pierre » et « N'Diaye » doivent se retrouver en tapant « ines », « jean pierre » ou « ndiaye » :
    // on retire accents, casse et ponctuation des deux côtés (clé stockée et requête).
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is '-' or '\'' or '’') continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

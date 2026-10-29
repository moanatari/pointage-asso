using System.Text;

namespace Pointage.Stats;

public static class StatsCsv
{
    // BOM + « ; » : c'est ce qu'Excel en français attend pour lire les accents et séparer les colonnes.
    public static string ForPeriod(PeriodStats s)
    {
        var sb = new StringBuilder("﻿Nom;Code;Jours de présence\r\n");
        foreach (var m in s.PerMember)
            sb.Append(Escape(m.FullName)).Append(';').Append(Escape(m.Code)).Append(';').Append(m.Days).Append("\r\n");
        return sb.ToString();
    }

    private static string Escape(string v)
    {
        // Un nom commençant par =, +, -, @ serait exécuté comme formule par Excel : l'apostrophe le force en texte.
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
        return v.AsSpan().IndexOfAny(";\"\r\n") >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}

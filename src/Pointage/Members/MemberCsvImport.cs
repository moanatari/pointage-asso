using System.Globalization;
using System.Text;

namespace Pointage.Members;

// Ce fichier est volontairement isolé : le format d'import (colonnes, encodage, dates) est appelé à évoluer
// (spec §7) et doit pouvoir changer sans toucher au reste de la gestion des membres.

public sealed record ImportRow(int Line, MemberInput? Input, IReadOnlyList<string> Errors, bool Duplicate);

public static class MemberCsvImport
{
    public const string Template = "nom;prenom;date_naissance;telephone;email;commentaire;contact_nom;contact_prenom;contact_date_naissance;contact_telephone;contact_email\r\n";

    private static readonly string[] DateFormats = ["dd/MM/yyyy", "yyyy-MM-dd"];

    // Excel en français enregistre en Windows-1252 : si le texte n'est pas de l'UTF-8 valide, on bascule dessus
    // plutôt que d'afficher des « � » à la place des accents.
    public static string Decode(byte[] bytes)
    {
        string text;
        try { text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            text = Encoding.GetEncoding(1252).GetString(bytes);
        }
        return text.TrimStart('﻿');
    }

    public static IReadOnlyList<ImportRow> Parse(string text, Func<string, string, DateOnly, bool> exists, DateOnly today)
    {
        var records = Split(text);
        if (records.Count == 0) return [];

        // « Prénom », « Date de naissance » et « date_naissance » désignent la même colonne.
        var columns = new Dictionary<string, int>();
        for (var i = 0; i < records[0].Fields.Count; i++)
            columns.TryAdd(HeaderKey(records[0].Fields[i]), i);

        var rows = new List<ImportRow>();
        var seen = new HashSet<(string, string, DateOnly)>();
        foreach (var (line, fields) in records.Skip(1))
        {
            string Get(string name) => columns.TryGetValue(name, out var i) && i < fields.Count ? fields[i].Trim() : "";
            string? Opt(string name) => Get(name) is { Length: > 0 } v ? v : null;

            var errors = new List<string>();
            var contactBirth = ParseDate(Get("contactdatenaissance"));
            if (contactBirth is null && Get("contactdatenaissance") != "") errors.Add("Date de naissance du contact invalide.");
            MemberInput? input = null;
            var duplicate = false;
            if (ParseDate(Get("datenaissance")) is { } birth)
            {
                input = new MemberInput(Get("nom"), Get("prenom"), birth, Opt("telephone"), Opt("email"), Opt("commentaire"),
                    new ContactInput(Opt("contactnom"), Opt("contactprenom"), contactBirth, Opt("contacttelephone"), Opt("contactemail")));
                errors.AddRange(input.Validate(today));
                // Le même membre deux fois dans le fichier compte comme un doublon, comme un membre déjà en base.
                duplicate = errors.Count == 0
                    && (!seen.Add((SearchText.Normalize(input.LastName), SearchText.Normalize(input.FirstName), birth))
                        || exists(input.LastName, input.FirstName, birth));
            }
            else errors.Insert(0, "Date de naissance invalide.");
            // Validate ne connaît pas la date en erreur : on garde quand même les erreurs de nom/prénom.
            if (input is null)
            {
                if (Get("nom") == "") errors.Add("Le nom est obligatoire.");
                if (Get("prenom") == "") errors.Add("Le prénom est obligatoire.");
            }
            rows.Add(new ImportRow(line, input, errors, duplicate));
        }
        return rows;
    }

    // « Date de naissance » et « date_naissance » se ramènent à la même clé.
    private static string HeaderKey(string header) =>
        SearchText.Normalize(header).Replace(" ", "").Replace("_", "").Replace("datedenaissance", "datenaissance");

    private static DateOnly? ParseDate(string s) =>
        DateOnly.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    // Petit analyseur à états : un champ entre guillemets peut contenir « ; », des retours à la ligne et « "" ».
    // Les lignes vides ou réduites à « ;;; » (Excel) sont sautées. Chaque enregistrement garde le numéro de la ligne où il commence (1 = en-tête).
    private static List<(int Line, List<string> Fields)> Split(string text)
    {
        var records = new List<(int, List<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var startLine = 1;
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else { if (c == '\n') line++; field.Append(c); }
            }
            else if (c == '"') quoted = true;
            else if (c == ';') { fields.Add(field.ToString()); field.Clear(); }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                fields.Add(field.ToString()); field.Clear();
                if (fields.Any(f => f.Trim().Length > 0)) records.Add((startLine, fields));
                fields = [];
                startLine = ++line;
            }
            else field.Append(c);
        }
        fields.Add(field.ToString());
        if (fields.Any(f => f.Trim().Length > 0)) records.Add((startLine, fields));
        return records;
    }
}

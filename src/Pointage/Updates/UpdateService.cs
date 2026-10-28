using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pointage.Data;

namespace Pointage.Updates;

public sealed record ReleaseInfo(Version Version, string SetupUrl, string Sha256Url);

public sealed partial class UpdateService(HttpClient http, IConfiguration config, Db db, ILogger<UpdateService> logger)
{
    private const string SetupName = "Pointage-Setup.exe";
    private const string HashName = SetupName + ".sha256";

    public static Version Current { get; } = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public ReleaseInfo? Latest { get; private set; }

    // Remplacé dans les tests : on ne lance jamais de vrai processus depuis la suite de tests.
    internal Action<string> Launch { get; set; } = path =>
        Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });

    public async Task CheckAsync(CancellationToken ct)
    {
        // Dépôt vide (tests) ou mal formé : aucun appel réseau.
        if (Repo() is not { } repo) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Pointage", null));
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
            string? setup = null, hash = null;
            foreach (var asset in json.RootElement.GetProperty("assets").EnumerateArray())
            {
                var url = asset.GetProperty("browser_download_url").GetString();
                switch (asset.GetProperty("name").GetString())
                {
                    case SetupName: setup = url; break;
                    case HashName: hash = url; break;
                }
            }
            Latest = IsNewer(tag, Current) && setup is not null && hash is not null && TryParseVersion(tag, out var v)
                ? new ReleaseInfo(v, setup, hash)
                : null;
        }
        // Tout est avalé (réseau, JSON inattendu, délai dépassé) : une vérification ratée ne doit jamais arrêter le service.
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(e, "Vérification des mises à jour impossible.");
        }
    }

    public async Task ApplyAsync(ReleaseInfo r, CancellationToken ct)
    {
        var repo = Repo() ?? throw new InvalidOperationException("Dépôt de mise à jour non configuré.");
        // Contrôle avant tout téléchargement. Le service tourne en LocalSystem : seul un fichier publié
        // dans le dépôt exact, et dont le hachage publié correspond, peut être exécuté.
        var setupUri = CheckUrl(r.SetupUrl, repo, SetupName);
        var hashUri = CheckUrl(r.Sha256Url, repo, HashName);

        var dir = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(db.Path))!, "updates")).FullName;
        var setupPath = Path.Combine(dir, SetupName);

        var expected = await http.GetStringAsync(hashUri, ct);
        await File.WriteAllTextAsync(Path.Combine(dir, HashName), expected, ct);
        await using (var source = await http.GetStreamAsync(setupUri, ct))
        await using (var target = new FileStream(setupPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await source.CopyToAsync(target, ct);

        // Fichier rouvert en lecture seule et gardé ouvert sans partage en écriture jusqu'au lancement :
        // personne ne peut le remplacer entre la vérification du hachage et l'exécution.
        using var file = new FileStream(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!HashMatches(file, expected))
            throw new InvalidOperationException("Le fichier téléchargé est corrompu ou modifié.");
        Launch(setupPath);
    }

    public static bool IsNewer(string tag, Version current) =>
        TryParseVersion(tag, out var v) && v > Normalize(current);

    public static bool HashMatches(Stream file, string sha256FileContent)
    {
        // Format de sha256sum : « <hex>  <nom> », ou le hachage seul.
        var expected = sha256FileContent.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var actual = Convert.ToHexString(SHA256.HashData(file));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var ok = Version.TryParse(tag.StartsWith('v') ? tag[1..] : tag, out var v);
        version = ok ? Normalize(v!) : new Version(0, 0, 0);
        return ok;
    }

    // L'assembly porte 4 composantes (0.1.0.0), les tags 2 ou 3 : on compare sur major.minor.patch.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private string? Repo()
    {
        var repo = config["Pointage:GitHubRepo"];
        if (string.IsNullOrEmpty(repo)) return null;
        if (RepoRegex().IsMatch(repo)) return repo;
        logger.LogWarning("Dépôt de mise à jour invalide : {Repo}", repo);
        return null;
    }

    private static Uri CheckUrl(string url, string repo, string assetName)
    {
        // Comparaison sur l'URL normalisée et sans caractère échappé : « ../ » ou « %2e%2e » ne peuvent pas ressortir du dépôt.
        if (url.StartsWith($"https://github.com/{repo}/releases/download/", StringComparison.Ordinal)
            && url.EndsWith("/" + assetName, StringComparison.Ordinal)
            && !url.Contains('%') && !url.Contains('?') && !url.Contains('#') && !url.Contains('\\')
            && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.AbsoluteUri == url)
            return uri;
        throw new InvalidOperationException($"Adresse de téléchargement refusée : {url}");
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$")]
    private static partial Regex RepoRegex();
}

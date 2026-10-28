using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pointage.Accounts;
using Pointage.Data;
using Pointage.Updates;

namespace Pointage.Tests;

public class UpdateTests
{
    private const string Repo = "moanatari/pointage-asso";
    private const string Download = $"https://github.com/{Repo}/releases/download/v9.9.9/";

    [Theory]
    [InlineData("v1.2.0", "1.1.9", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("v0.9", "1.0.0", false)]
    [InlineData("pas-une-version", "1.0.0", false)]
    public void IsNewer_Works(string tag, string current, bool expected) =>
        Assert.Equal(expected, UpdateService.IsNewer(tag, Version.Parse(current)));

    [Fact]
    public void IsNewer_IgnoresAssemblyRevision() => Assert.False(UpdateService.IsNewer("v0.1.0", new Version(0, 1, 0, 0)));

    [Fact]
    public void HashMatches_WithFileNameSuffix_True()
    {
        var content = "contenu de l'installateur"u8.ToArray();
        var hex = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        Assert.True(UpdateService.HashMatches(new MemoryStream(content), $"{hex}  Pointage-Setup.exe\n"));
        Assert.True(UpdateService.HashMatches(new MemoryStream(content), hex.ToUpperInvariant()));
    }

    [Fact]
    public void HashMatches_Different_False()
    {
        var other = Convert.ToHexString(SHA256.HashData("autre"u8.ToArray()));
        Assert.False(UpdateService.HashMatches(new MemoryStream("contenu"u8.ToArray()), other));
        Assert.False(UpdateService.HashMatches(new MemoryStream("contenu"u8.ToArray()), ""));
    }

    [Fact]
    public async Task Check_ParsesGitHubJson()
    {
        HttpRequestMessage? sent = null;
        var service = Service(req =>
        {
            sent = req;
            return Json($$"""
                {"tag_name":"v9.9.9","assets":[
                  {"name":"Notes.txt","browser_download_url":"{{Download}}Notes.txt"},
                  {"name":"Pointage-Setup.exe","browser_download_url":"{{Download}}Pointage-Setup.exe"},
                  {"name":"Pointage-Setup.exe.sha256","browser_download_url":"{{Download}}Pointage-Setup.exe.sha256"}]}
                """);
        });

        await service.CheckAsync(CancellationToken.None);

        Assert.Equal($"https://api.github.com/repos/{Repo}/releases/latest", sent!.RequestUri!.ToString());
        Assert.Equal("Pointage", sent.Headers.UserAgent.ToString());
        Assert.Equal(new ReleaseInfo(new Version(9, 9, 9), Download + "Pointage-Setup.exe", Download + "Pointage-Setup.exe.sha256"), service.Latest);
    }

    [Fact]
    public async Task Check_MissingHashAsset_LeavesLatestNull()
    {
        var service = Service(_ => Json($$"""{"tag_name":"v9.9.9","assets":[{"name":"Pointage-Setup.exe","browser_download_url":"{{Download}}Pointage-Setup.exe"}]}"""));
        await service.CheckAsync(CancellationToken.None);
        Assert.Null(service.Latest);
    }

    [Fact]
    public async Task Check_NetworkError_LeavesLatestNull()
    {
        var service = Service(_ => throw new HttpRequestException("pas de réseau"));
        await service.CheckAsync(CancellationToken.None);
        Assert.Null(service.Latest);
    }

    [Theory]
    [InlineData("https://evil.example/x.exe")]
    [InlineData("https://github.com/autre/depot/releases/download/v9.9.9/Pointage-Setup.exe")]
    [InlineData($"{Download}../../../../../autre/depot/releases/download/v9.9.9/Pointage-Setup.exe")]
    [InlineData($"{Download}%2e%2e/Pointage-Setup.exe")]
    public async Task Apply_ForeignUrl_Throws(string setupUrl)
    {
        var requested = false;
        var service = Service(_ => { requested = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        var launched = false;
        service.Launch = _ => launched = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAsync(new ReleaseInfo(new Version(9, 9, 9), setupUrl, Download + "Pointage-Setup.exe.sha256"), CancellationToken.None));
        // L'URL du hachage est contrôlée de la même façon.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAsync(new ReleaseInfo(new Version(9, 9, 9), Download + "Pointage-Setup.exe", setupUrl), CancellationToken.None));

        Assert.False(requested);
        Assert.False(launched);
    }

    [Fact]
    public async Task Apply_HashMismatch_ThrowsAndDoesNotLaunch()
    {
        var service = Service(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(req.RequestUri!.AbsolutePath.EndsWith(".sha256")
                ? Convert.ToHexString(SHA256.HashData("officiel"u8.ToArray())) + "  Pointage-Setup.exe"
                : "modifié"),
        });
        var launched = false;
        service.Launch = _ => launched = true;

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAsync(new ReleaseInfo(new Version(9, 9, 9), Download + "Pointage-Setup.exe", Download + "Pointage-Setup.exe.sha256"), CancellationToken.None));

        Assert.Equal("Le fichier téléchargé est corrompu ou modifié.", e.Message);
        Assert.False(launched);
    }

    [Fact]
    public async Task Apply_HashMatches_LaunchesFromNewPrivateFolderEachTime()
    {
        var db = TestDb.Create(out _);
        var service = Service(GoodRelease, db);
        var launched = new List<string>();
        service.Launch = launched.Add;

        await service.ApplyAsync(Release, CancellationToken.None);
        Assert.Equal(Setup, File.ReadAllBytes(launched[0]));
        await service.ApplyAsync(Release, CancellationToken.None);

        var updates = Path.Combine(Path.GetDirectoryName(db.Path)!, "updates");
        Assert.Equal(2, launched.Count);
        Assert.NotEqual(Path.GetDirectoryName(launched[0]), Path.GetDirectoryName(launched[1]));
        Assert.All(launched, l => Assert.Equal(updates, Path.GetDirectoryName(Path.GetDirectoryName(l))));
        // Le dossier du premier téléchargement a été nettoyé.
        Assert.Equal(Path.GetDirectoryName(launched[1]), Assert.Single(Directory.GetDirectories(updates)));
        if (OperatingSystem.IsWindows())
            Assert.True(new DirectoryInfo(Path.GetDirectoryName(launched[1])!).GetAccessControl().AreAccessRulesProtected);
    }

    [Fact]
    public async Task Apply_UpdatesIsJunction_ThrowsWithoutLaunching()
    {
        var db = TestDb.Create(out _);
        var updates = Path.Combine(Path.GetDirectoryName(db.Path)!, "updates");
        var target = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pointage-junction-" + Guid.NewGuid().ToString("N"))).FullName;
        using (var mklink = Process.Start(new ProcessStartInfo("cmd", $"/c mklink /J \"{updates}\" \"{target}\"") { RedirectStandardOutput = true, UseShellExecute = false })!)
            await mklink.WaitForExitAsync();
        // Pas de jonction possible ici (hors Windows) : rien à vérifier. xUnit 2 n'a pas de saut dynamique.
        if (!Directory.Exists(updates) || !new DirectoryInfo(updates).Attributes.HasFlag(FileAttributes.ReparsePoint)) return;

        var service = Service(GoodRelease, db);
        var launched = false;
        service.Launch = _ => launched = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(Release, CancellationToken.None));
        Assert.False(launched);
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Fact]
    public async Task UpdatePage_ShowsInstalledVersion()
    {
        using var factory = new AppFactory();
        // Garde-fou : sans dépôt configuré, le worker démarré avec l'application ne touche pas au réseau.
        Assert.Equal("", factory.Services.GetRequiredService<IConfiguration>()["Pointage:GitHubRepo"]);
        var client = await factory.LoggedInClientAsync(Role.SuperAdmin);
        var html = await client.GetStringAsync("/Admin/Update");
        Assert.Contains(UpdateService.Current.ToString(3), html);
        Assert.Contains("Vérifier maintenant", html);
    }

    private static readonly byte[] Setup = "officiel"u8.ToArray();
    private static readonly ReleaseInfo Release = new(new Version(9, 9, 9), Download + "Pointage-Setup.exe", Download + "Pointage-Setup.exe.sha256");

    private static HttpResponseMessage GoodRelease(HttpRequestMessage req) => new(HttpStatusCode.OK)
    {
        Content = req.RequestUri!.AbsolutePath.EndsWith(".sha256")
            ? new StringContent(Convert.ToHexString(SHA256.HashData(Setup)))
            : new ByteArrayContent(Setup),
    };

    private static UpdateService Service(Func<HttpRequestMessage, HttpResponseMessage> respond, Db? db = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Pointage:GitHubRepo"] = Repo }).Build();
        return new UpdateService(new HttpClient(new FakeHandler(respond)), config, db ?? TestDb.Create(out _), NullLogger<UpdateService>.Instance);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
}

namespace Pointage.Updates;

// Vérifie au démarrage puis une fois par jour. CheckAsync avale ses erreurs : le service ne s'arrête jamais à cause d'elle.
public sealed class UpdateCheckWorker(UpdateService updates) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do await updates.CheckAsync(ct);
        while (await timer.WaitForNextTickAsync(ct));
    }
}

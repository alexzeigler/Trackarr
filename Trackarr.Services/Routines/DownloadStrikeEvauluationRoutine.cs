using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trackarr.Core.Queue;
using Trackarr.Core.Routine;
using Trackarr.Data;
using Trackarr.Services.Strikes;

namespace Trackarr.Services.Routines;

public sealed class DownloadStrikeEvaluationRoutine(IServiceScopeFactory scopeFactory) : Routine {
    public override string Key => "download-strike-evaluation";
    public override string Name => "Download Strike Evaluation";
    public override string Description => "Evaluates active downloads and applies strike state.";
    public override bool Enabled => true;
    public override int IntervalSeconds => 60;

    public override async Task<string> ExecuteAsync(CancellationToken cancellationToken) {
        using IServiceScope scope = scopeFactory.CreateScope();

        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        DownloadStrikeEvaluator evaluator = scope.ServiceProvider.GetRequiredService<DownloadStrikeEvaluator>();

        List<int> downloadItemIds = await db.DownloadQueue.AsNoTracking().Where(x => x.TrackingState == DownloadItemTrackingState.Active || x.TrackingState == DownloadItemTrackingState.Watching || x.TrackingState == DownloadItemTrackingState.Struck).OrderBy(x => x.LastSeenAtUtc).Select(x => x.Id).ToListAsync(cancellationToken);

        foreach (int downloadItemId in downloadItemIds) {
            await evaluator.EvaluateAsync(downloadItemId, cancellationToken);
        }
        return "Completed";
    }
}
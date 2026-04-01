using Cip.Application.Features.CipMvp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cip.Worker.Services;

public sealed class BootstrapWorker(ILogger<BootstrapWorker> logger, IProcessingStatusService processingStatusService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupStatus = await processingStatusService.GetStatusAsync(stoppingToken);
        logger.LogInformation(
            "CIP worker started with {RuntimeStore} runtime store. Events={EventCount}, Profiles={ProfileCount}, PendingChangeSets={PendingChangeSetCount}, Triggers={TriggerCount}.",
            startupStatus.RuntimeStore,
            startupStatus.EventCount,
            startupStatus.ProfileCount,
            startupStatus.PendingChangeSetCount,
            startupStatus.TriggerCount);

        while (!stoppingToken.IsCancellationRequested)
        {
            var heartbeatStatus = await processingStatusService.GetStatusAsync(stoppingToken);
            logger.LogInformation(
                "CIP worker heartbeat at {UtcNow}. Events={EventCount}, Profiles={ProfileCount}, PendingChangeSets={PendingChangeSetCount}, Triggers={TriggerCount}.",
                heartbeatStatus.UtcTime,
                heartbeatStatus.EventCount,
                heartbeatStatus.ProfileCount,
                heartbeatStatus.PendingChangeSetCount,
                heartbeatStatus.TriggerCount);

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}

using Microsoft.AspNetCore.SignalR;

namespace RobotMonitor.Server.Monitoring;

/// <summary>
/// 1 秒ごとに、通信断の判定をしてから、全ロボットの状態を画面へ送る。
/// 異常の一覧は、変わったときだけ送る。
/// </summary>
public class RobotBroadcaster(RobotStore store, IHubContext<MonitorHub> hub, ILogger<RobotBroadcaster> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sentAlertVersion = -1;
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                store.CheckDisconnected(DateTimeOffset.Now);
                await hub.Clients.All.SendAsync(MonitorHub.RobotsUpdated, store.Snapshot(), stoppingToken);

                if (store.AlertVersion != sentAlertVersion)
                {
                    sentAlertVersion = store.AlertVersion;
                    await hub.Clients.All.SendAsync(MonitorHub.AlertsChanged, store.Alerts(), stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "状態の送信に失敗しました");
            }
        }
    }
}

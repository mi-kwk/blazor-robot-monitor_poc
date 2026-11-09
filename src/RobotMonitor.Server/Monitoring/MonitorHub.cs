using Microsoft.AspNetCore.SignalR;

namespace RobotMonitor.Server.Monitoring;

/// <summary>
/// 監視画面がつなぐ SignalR の窓口（/hubs/monitor）。
/// サーバーから画面へ送るだけなので、画面から呼ぶメソッドは持たない。
/// 送るメッセージ：
///   "RobotsUpdated" … 全ロボットの最新の状態（1 秒ごと）
///   "AlertsChanged" … 異常の一覧（変わったときだけ）
/// </summary>
public class MonitorHub : Hub
{
    public const string Path = "/hubs/monitor";
    public const string RobotsUpdated = "RobotsUpdated";
    public const string AlertsChanged = "AlertsChanged";
}

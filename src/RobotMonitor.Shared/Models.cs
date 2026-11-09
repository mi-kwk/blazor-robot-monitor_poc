namespace RobotMonitor.Shared;

/// <summary>ロボットが自分で報告する状態。</summary>
public enum RobotState
{
    Idle,     // 待機
    Moving,   // 移動中
    Working,  // 作業中
    Fault,    // 故障
}

/// <summary>模擬ロボットが 1 秒ごとにサーバーへ送る報告（1 台分）。</summary>
public record RobotReport(
    string RobotId,
    RobotState State,
    int X,
    int Y,
    double Battery,      // 電池残量（%）
    double Temperature); // 温度（℃）

/// <summary>画面に表示する 1 台分の情報。サーバーが報告に「最後に受け取った時刻」などを足したもの。</summary>
public record RobotView(
    string RobotId,
    RobotState State,
    int X,
    int Y,
    double Battery,
    double Temperature,
    DateTimeOffset LastSeen,
    bool Disconnected,
    IReadOnlyList<AlertKind> ActiveAlerts);

public enum AlertKind
{
    Disconnected, // 通信断
    LowBattery,   // 電池残量の低下
    HighTemp,     // 温度の上昇
    Stuck,        // 動かない
    Fault,        // 故障（ロボット自身が故障を報告した）
}

/// <summary>異常 1 件。起きた時刻・戻った時刻・確認済みかを持つ。</summary>
public record Alert(
    int Id,
    string RobotId,
    AlertKind Kind,
    string Message,
    DateTimeOffset RaisedAt,
    DateTimeOffset? ResolvedAt,
    bool Acknowledged)
{
    public bool IsActive => ResolvedAt is null;
}

/// <summary>画面の表示用の日本語名。</summary>
public static class Labels
{
    public static string Of(RobotState s) => s switch
    {
        RobotState.Idle => "待機",
        RobotState.Moving => "移動中",
        RobotState.Working => "作業中",
        RobotState.Fault => "故障",
        _ => s.ToString(),
    };

    public static string Of(AlertKind k) => k switch
    {
        AlertKind.Disconnected => "通信断",
        AlertKind.LowBattery => "電池残量の低下",
        AlertKind.HighTemp => "温度の上昇",
        AlertKind.Stuck => "動かない",
        AlertKind.Fault => "故障",
        _ => k.ToString(),
    };
}

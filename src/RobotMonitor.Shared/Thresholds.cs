namespace RobotMonitor.Shared;

/// <summary>
/// 異常の判定に使う値。仮の値なので、試しながら見直す。
/// 「起きる値」と「戻る値」をずらしているのは、境目で異常が何度も出たり消えたりしないようにするため。
/// </summary>
public static class Thresholds
{
    /// <summary>最後の報告からこの秒数を過ぎたら通信断。</summary>
    public const double DisconnectSeconds = 5;

    /// <summary>電池残量がこの値（%）を下回ったら異常。</summary>
    public const double LowBattery = 20;
    /// <summary>電池残量がこの値（%）以上に戻ったら正常。</summary>
    public const double LowBatteryRecover = 25;

    /// <summary>温度がこの値（℃）を超えた報告が HighTempCount 回続いたら異常。</summary>
    public const double HighTemp = 60;
    public const int HighTempCount = 3;
    /// <summary>温度がこの値（℃）以下に戻ったら正常。</summary>
    public const double HighTempRecover = 55;

    /// <summary>「移動中」なのに位置がこの秒数変わらなかったら異常。</summary>
    public const double StuckSeconds = 10;

    /// <summary>倉庫のマス目の大きさ（模擬ロボットと地図表示で使う）。</summary>
    public const int GridWidth = 20;
    public const int GridHeight = 10;
}

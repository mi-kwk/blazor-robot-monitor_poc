using RobotMonitor.Shared;

namespace RobotMonitor.Server.Monitoring;

/// <summary>
/// ロボットごとの最新の状態と、異常の履歴を持つ。異常の判定もここで行う。
/// 報告の受け取り（API）と 1 秒ごとの見回り（RobotBroadcaster）が別のスレッドから呼ぶので、lock で守る。
/// 履歴はメモリにだけ持つ（サーバーを止めると消える）。
/// </summary>
public class RobotStore
{
    private sealed class Entry
    {
        public required RobotReport Last;
        public DateTimeOffset LastSeen;
        public bool Disconnected;
        public int HotCount;                    // 温度が高い報告が何回続いたか
        public DateTimeOffset LastMovedAt;       // 最後に位置が変わった（または移動中でなくなった）時刻
        public readonly Dictionary<AlertKind, int> ActiveAlertIds = new();
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _robots = new();
    private readonly List<Alert> _alerts = new();
    private int _nextAlertId = 1;

    /// <summary>異常の一覧が変わるたびに増える。画面へ送り直すかの判断に使う。</summary>
    public int AlertVersion { get; private set; }

    /// <summary>模擬ロボットからの報告を受け取り、電池・温度・動かないの異常を判定する。</summary>
    public void Report(IEnumerable<RobotReport> reports, DateTimeOffset now)
    {
        lock (_lock)
        {
            foreach (var r in reports)
            {
                if (!_robots.TryGetValue(r.RobotId, out var e))
                {
                    e = new Entry { Last = r, LastSeen = now, LastMovedAt = now };
                    _robots[r.RobotId] = e;
                }

                // 位置が変わった、または移動中でなければ「動かない」の時計を戻す
                if (r.State != RobotState.Moving || r.X != e.Last.X || r.Y != e.Last.Y)
                    e.LastMovedAt = now;

                e.Last = r;
                e.LastSeen = now;

                // 通信断から戻った
                if (e.Disconnected)
                {
                    e.Disconnected = false;
                    Resolve(e, AlertKind.Disconnected, now);
                }

                // 故障（ロボットが自分で報告してくる）
                if (r.State == RobotState.Fault)
                    Raise(e, AlertKind.Fault, "故障を報告しています", now);
                else
                    Resolve(e, AlertKind.Fault, now);

                // 電池残量
                if (r.Battery < Thresholds.LowBattery)
                    Raise(e, AlertKind.LowBattery, $"電池残量が {r.Battery:F0}% です", now);
                else if (r.Battery >= Thresholds.LowBatteryRecover)
                    Resolve(e, AlertKind.LowBattery, now);

                // 温度（一瞬の高い値では出さず、続いたら出す）
                e.HotCount = r.Temperature > Thresholds.HighTemp ? e.HotCount + 1 : 0;
                if (e.HotCount >= Thresholds.HighTempCount)
                    Raise(e, AlertKind.HighTemp, $"温度が {r.Temperature:F0}℃ です", now);
                else if (r.Temperature <= Thresholds.HighTempRecover)
                    Resolve(e, AlertKind.HighTemp, now);

                // 動かない
                if ((now - e.LastMovedAt).TotalSeconds >= Thresholds.StuckSeconds)
                    Raise(e, AlertKind.Stuck, $"移動中のまま {Thresholds.StuckSeconds:F0} 秒以上止まっています", now);
                else
                    Resolve(e, AlertKind.Stuck, now);
            }
        }
    }

    /// <summary>1 秒ごとに呼ぶ。報告が途絶えたロボットを通信断にする。</summary>
    public void CheckDisconnected(DateTimeOffset now)
    {
        lock (_lock)
        {
            foreach (var e in _robots.Values)
            {
                if (!e.Disconnected && (now - e.LastSeen).TotalSeconds >= Thresholds.DisconnectSeconds)
                {
                    e.Disconnected = true;
                    Raise(e, AlertKind.Disconnected, $"{Thresholds.DisconnectSeconds:F0} 秒以上報告がありません", now);
                }
            }
        }
    }

    public List<RobotView> Snapshot()
    {
        lock (_lock)
        {
            return _robots.Values
                .OrderBy(e => e.Last.RobotId)
                .Select(e => new RobotView(
                    e.Last.RobotId, e.Last.State, e.Last.X, e.Last.Y, e.Last.Battery, e.Last.Temperature,
                    e.LastSeen, e.Disconnected, e.ActiveAlertIds.Keys.OrderBy(k => k).ToList()))
                .ToList();
        }
    }

    /// <summary>異常の一覧（新しい順）。</summary>
    public List<Alert> Alerts()
    {
        lock (_lock)
        {
            return _alerts.OrderByDescending(a => a.Id).ToList();
        }
    }

    public bool Acknowledge(int id)
    {
        lock (_lock)
        {
            var i = _alerts.FindIndex(a => a.Id == id);
            if (i < 0) return false;
            if (!_alerts[i].Acknowledged)
            {
                _alerts[i] = _alerts[i] with { Acknowledged = true };
                AlertVersion++;
            }
            return true;
        }
    }

    public void AcknowledgeAll()
    {
        lock (_lock)
        {
            for (var i = 0; i < _alerts.Count; i++)
            {
                if (!_alerts[i].Acknowledged)
                {
                    _alerts[i] = _alerts[i] with { Acknowledged = true };
                    AlertVersion++;
                }
            }
        }
    }

    // ---- 以下は lock の中から呼ぶ ----

    private void Raise(Entry e, AlertKind kind, string message, DateTimeOffset now)
    {
        if (e.ActiveAlertIds.ContainsKey(kind)) return; // すでに出ている異常は重ねて出さない
        var alert = new Alert(_nextAlertId++, e.Last.RobotId, kind, message, now, null, false);
        _alerts.Add(alert);
        e.ActiveAlertIds[kind] = alert.Id;
        AlertVersion++;
    }

    private void Resolve(Entry e, AlertKind kind, DateTimeOffset now)
    {
        if (!e.ActiveAlertIds.Remove(kind, out var id)) return;
        var i = _alerts.FindIndex(a => a.Id == id);
        if (i >= 0) _alerts[i] = _alerts[i] with { ResolvedAt = now };
        AlertVersion++;
    }
}

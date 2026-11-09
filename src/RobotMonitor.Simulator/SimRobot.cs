using RobotMonitor.Shared;

namespace RobotMonitor.Simulator;

/// <summary>
/// 模擬ロボット 1 台。1 秒ごとに Step() で動き、ToReport() で報告を作る。
/// 動き方は単純：待機 → 行き先を決めて移動 → 着いたら作業 → 待機、を繰り返す。
/// 待機中は充電されることにしている（充電場への移動は扱わない）。
/// </summary>
public class SimRobot
{
    private readonly Random _rand;

    public string Id { get; }
    public RobotState State { get; private set; } = RobotState.Idle;
    public int X { get; private set; }
    public int Y { get; private set; }
    public double Battery { get; private set; }
    public double Temperature { get; private set; } = 35;

    private (int X, int Y) _target;
    private int _waitLeft;   // 待機・作業の残り秒数

    // ---- コマンドで起こす異常 ----
    public bool Disconnected { get; set; } // 報告を送らない
    public bool DrainBattery { get; set; } // 電池が急に減る
    public bool Overheat { get; set; }     // 温度が上がり続ける
    public bool Stuck { get; set; }        // 移動中のまま止まる
    public bool Faulted { get; set; }      // 故障

    public SimRobot(string id, int seed)
    {
        Id = id;
        _rand = new Random(seed);
        X = _rand.Next(Thresholds.GridWidth);
        Y = _rand.Next(Thresholds.GridHeight);
        Battery = 50 + _rand.Next(50);
        _waitLeft = _rand.Next(1, 4);
    }

    public void Step()
    {
        if (Faulted)
        {
            State = RobotState.Fault;
        }
        else if (Stuck)
        {
            State = RobotState.Moving; // 移動中のつもりだが、位置は変わらない
            Battery -= 0.2;
        }
        else
        {
            StepNormal();
        }

        if (DrainBattery) Battery -= 5;
        Battery = Math.Clamp(Battery, 0, 100);

        // 温度：状態ごとの目安の温度に少しずつ近づく。Overheat なら上がり続ける
        if (Overheat)
        {
            Temperature = Math.Min(Temperature + 3, 75);
        }
        else
        {
            var baseTemp = State switch
            {
                RobotState.Moving => 40,
                RobotState.Working => 45,
                _ => 35,
            };
            Temperature += (baseTemp - Temperature) * 0.2 + (_rand.NextDouble() - 0.5);
        }
    }

    private void StepNormal()
    {
        if (State == RobotState.Fault) State = RobotState.Idle; // 故障から復旧した

        switch (State)
        {
            case RobotState.Idle:
                Battery += 1.0; // 待機中は充電される
                if (--_waitLeft <= 0)
                {
                    _target = (_rand.Next(Thresholds.GridWidth), _rand.Next(Thresholds.GridHeight));
                    State = RobotState.Moving;
                }
                break;

            case RobotState.Moving:
                Battery -= 0.5;
                // 1 秒に 1 マス、まず横、次に縦へ進む
                if (X != _target.X) X += Math.Sign(_target.X - X);
                else if (Y != _target.Y) Y += Math.Sign(_target.Y - Y);

                if (X == _target.X && Y == _target.Y)
                {
                    State = RobotState.Working;
                    _waitLeft = _rand.Next(3, 7);
                }
                break;

            case RobotState.Working:
                Battery -= 0.3;
                if (--_waitLeft <= 0)
                {
                    State = RobotState.Idle;
                    _waitLeft = _rand.Next(2, 6);
                }
                break;
        }
    }

    /// <summary>起こした異常をすべてやめて、すぐ正常の範囲に戻す（動作確認を早く進めるため）。</summary>
    public void Reset()
    {
        Disconnected = DrainBattery = Overheat = Stuck = Faulted = false;
        if (Battery < Thresholds.LowBatteryRecover) Battery = 80;
        if (Temperature > Thresholds.HighTempRecover) Temperature = 40;
    }

    public RobotReport ToReport() =>
        new(Id, State, X, Y, Math.Round(Battery, 1), Math.Round(Temperature, 1));

    public string Flags()
    {
        var f = new List<string>();
        if (Disconnected) f.Add("通信断");
        if (DrainBattery) f.Add("電池減");
        if (Overheat) f.Add("高温");
        if (Stuck) f.Add("停止");
        if (Faulted) f.Add("故障");
        return f.Count == 0 ? "" : $"[{string.Join(",", f)}]";
    }
}

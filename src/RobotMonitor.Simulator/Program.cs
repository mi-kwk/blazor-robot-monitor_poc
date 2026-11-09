// 模擬ロボット：N 台分の状態を 1 秒ごとにサーバーへ送る。
// コマンドを入力して Enter で、異常を起こせる（help で一覧）。
//
//   dotnet run --project src/RobotMonitor.Simulator -- [--url http://localhost:5080] [--count 10] [--interval 1000]

using System.Net.Http.Json;
using System.Text;
using RobotMonitor.Shared;
using RobotMonitor.Simulator;

Console.OutputEncoding = Encoding.UTF8;

var url = Arg("--url") ?? "http://localhost:5080";
var count = int.Parse(Arg("--count") ?? "10");
var interval = int.Parse(Arg("--interval") ?? "1000");

var robots = Enumerable.Range(1, count)
    .Select(i => new SimRobot($"R{i:00}", seed: i))
    .ToList();
var gate = new object();

Console.WriteLine($"模擬ロボット {count} 台 → {url}（{interval} ミリ秒ごと）");
PrintHelp();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// コマンドの読み取りは別の流れで行う（送信の 1 秒ごとの繰り返しを止めないため）
_ = Task.Run(() =>
{
    while (!cts.IsCancellationRequested)
    {
        var line = Console.ReadLine();
        if (line is null) return; // 入力が閉じられた（パイプで起動した場合など）
        lock (gate)
        {
            if (!HandleCommand(line.Trim())) cts.Cancel();
        }
    }
});

using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(3) };
var failing = false;
var tick = 0;

using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
try
{
    while (await timer.WaitForNextTickAsync(cts.Token))
    {
        List<RobotReport> reports;
        lock (gate)
        {
            foreach (var r in robots) r.Step();
            reports = robots.Where(r => !r.Disconnected).Select(r => r.ToReport()).ToList();
        }

        try
        {
            var res = await http.PostAsJsonAsync("api/reports", reports, cts.Token);
            res.EnsureSuccessStatusCode();
            if (failing) Console.WriteLine("サーバーへの送信が戻りました。");
            failing = false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cts.IsCancellationRequested)
        {
            if (!failing) Console.WriteLine($"サーバーへ送信できません（{ex.Message}）。送り続けます…");
            failing = true;
        }

        if (++tick % 10 == 0)
        {
            lock (gate) PrintStatus();
        }
    }
}
catch (OperationCanceledException)
{
}
Console.WriteLine("終了しました。");

// ---------------------------------------------------------------

bool HandleCommand(string line)
{
    if (line.Length == 0) return true;
    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var cmd = parts[0].ToLowerInvariant();

    if (cmd is "q" or "quit") return false;
    if (cmd is "help" or "?") { PrintHelp(); return true; }
    if (cmd == "list") { PrintStatus(); return true; }

    if (parts.Length < 2)
    {
        Console.WriteLine("ロボットの番号を指定してください（例：d 3、r all）");
        return true;
    }

    var targets = parts[1].ToLowerInvariant() == "all"
        ? robots
        : robots.Where(r => int.TryParse(parts[1], out var n) && r.Id == $"R{n:00}").ToList();
    if (targets.Count == 0)
    {
        Console.WriteLine($"ロボット {parts[1]} はいません（1〜{count}、または all）");
        return true;
    }

    foreach (var r in targets)
    {
        switch (cmd)
        {
            case "d": r.Disconnected = !r.Disconnected; break;
            case "b": r.DrainBattery = !r.DrainBattery; break;
            case "h": r.Overheat = !r.Overheat; break;
            case "s": r.Stuck = !r.Stuck; break;
            case "f": r.Faulted = !r.Faulted; break;
            case "r": r.Reset(); break;
            default:
                Console.WriteLine($"知らないコマンドです：{cmd}（help で一覧）");
                return true;
        }
        Console.WriteLine($"{r.Id} {r.Flags()}");
    }
    return true;
}

void PrintHelp()
{
    Console.WriteLine("""
        コマンド（入力して Enter。N はロボットの番号 1〜、または all）
          d N   通信断（報告を送らない）のオン／オフ
          b N   電池が急に減る、のオン／オフ
          h N   温度が上がり続ける、のオン／オフ
          s N   移動中のまま止まる、のオン／オフ
          f N   故障のオン／オフ
          r N   起こした異常をすべてやめて、正常の範囲に戻す
          list  全台の状態を表示
          help  この説明
          q     終了
        """);
}

void PrintStatus()
{
    foreach (var r in robots)
    {
        Console.WriteLine($"  {r.Id} {Labels.Of(r.State),-4} ({r.X,2},{r.Y,2}) 電池 {r.Battery,5:F1}% 温度 {r.Temperature,4:F1}℃ {r.Flags()}");
    }
}

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using RobotMonitor.Shared;

namespace RobotMonitor.Client.Services;

/// <summary>サーバーとのつながりの状態（画面の上部に表示する）。</summary>
public enum LinkState
{
    Connecting,   // 接続中
    Connected,    // 接続済み
    Reconnecting, // 切れたので、つなぎ直している
    Disconnected, // 切れている（再試行の合間）
}

/// <summary>
/// サーバーと SignalR でつなぎ、ロボットの状態と異常の一覧を受け取って持っておく。
/// 画面の部品はこのクラスの Changed を購読して、届いたら描き直す。
/// ブラウザの中で 1 つだけ作る（Program.cs で AddSingleton）。
/// </summary>
public class MonitorClient(HttpClient http, Uri baseUri) : IAsyncDisposable
{
    // サーバー側の MonitorHub と同じ名前（Shared に置いていないのは、サーバーの実装の名前だから）
    private const string HubPath = "hubs/monitor";
    private const string RobotsUpdated = "RobotsUpdated";
    private const string AlertsChanged = "AlertsChanged";

    private HubConnection? _hub;
    private bool _started;

    public IReadOnlyList<RobotView> Robots { get; private set; } = [];
    public IReadOnlyList<Alert> Alerts { get; private set; } = [];
    public LinkState State { get; private set; } = LinkState.Connecting;
    /// <summary>最後に状態が届いた時刻（ブラウザの時計）。</summary>
    public DateTimeOffset? LastReceived { get; private set; }
    public int UnacknowledgedCount => Alerts.Count(a => !a.Acknowledged);

    public event Action? Changed;

    /// <summary>最初の 1 回だけ接続を始める（どの画面から呼んでもよい）。</summary>
    public Task StartAsync()
    {
        if (_started) return Task.CompletedTask;
        _started = true;

        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(baseUri, HubPath))
            .WithAutomaticReconnect(new ForeverRetryPolicy()) // 切れたら、つながるまで試し続ける
            .Build();

        _hub.On<List<RobotView>>(RobotsUpdated, robots =>
        {
            Robots = robots;
            LastReceived = DateTimeOffset.Now;
            Changed?.Invoke();
        });
        _hub.On<List<Alert>>(AlertsChanged, alerts =>
        {
            Alerts = alerts;
            Changed?.Invoke();
        });
        _hub.Reconnecting += _ =>
        {
            SetState(LinkState.Reconnecting);
            return Task.CompletedTask;
        };
        _hub.Reconnected += async _ =>
        {
            SetState(LinkState.Connected);
            await LoadAsync(); // 切れている間に変わった分を取り直す
        };
        _hub.Closed += _ =>
        {
            SetState(LinkState.Disconnected);
            return Task.CompletedTask;
        };

        // 待たずに裏で接続する（画面はすぐ表示し、届いたら描き直す）
        _ = ConnectLoopAsync();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 最初の接続。WithAutomaticReconnect は「一度つながった後に切れた場合」しか試し直さないので、
    /// 最初からサーバーが止まっているときのために、つながるまで自分で繰り返す。
    /// </summary>
    private async Task ConnectLoopAsync()
    {
        while (true)
        {
            try
            {
                SetState(LinkState.Connecting);
                await _hub!.StartAsync();
                SetState(LinkState.Connected);
                await LoadAsync();
                return;
            }
            catch
            {
                SetState(LinkState.Disconnected);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }

    /// <summary>API で現在の状態をまとめて取得する。</summary>
    private async Task LoadAsync()
    {
        try
        {
            Robots = await http.GetFromJsonAsync<List<RobotView>>("api/robots") ?? [];
            Alerts = await http.GetFromJsonAsync<List<Alert>>("api/alerts") ?? [];
            Changed?.Invoke();
        }
        catch (HttpRequestException)
        {
            // 取得に失敗しても、SignalR で次の状態が届けば表示は追いつく
        }
    }

    public async Task AcknowledgeAsync(int alertId)
    {
        await http.PostAsync($"api/alerts/{alertId}/ack", null);
        // 一覧は、サーバーから AlertsChanged で送り直されてくる（1 秒以内）
    }

    public async Task AcknowledgeAllAsync()
    {
        await http.PostAsync("api/alerts/ack-all", null);
    }

    private void SetState(LinkState state)
    {
        State = state;
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
    }

    /// <summary>0 秒、2 秒、5 秒、以後は 5 秒おきに、ずっと試し直す。</summary>
    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext ctx) => ctx.PreviousRetryCount switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromSeconds(2),
            _ => TimeSpan.FromSeconds(5),
        };
    }
}

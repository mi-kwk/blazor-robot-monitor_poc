using Microsoft.AspNetCore.Components;

namespace RobotMonitor.Client.Services;

/// <summary>
/// MonitorClient の内容を表示する部品の共通の土台。
/// 状態が届くたびに描き直し、部品が消えるときに購読をやめる（やめないと、消えた部品を描き直そうとして無駄が残る）。
/// </summary>
public abstract class MonitorComponentBase : ComponentBase, IDisposable
{
    [Inject] protected MonitorClient Monitor { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Monitor.Changed += OnMonitorChanged;
        await Monitor.StartAsync();
    }

    // SignalR の受信は部品の描画とは別の流れから来るので、InvokeAsync を通して描き直す
    private void OnMonitorChanged() => _ = InvokeAsync(StateHasChanged);

    public virtual void Dispose() => Monitor.Changed -= OnMonitorChanged;

    /// <summary>画面に出す状態名。通信断のときは、最後に報告された状態より通信断を優先して出す。</summary>
    protected static string StateLabel(RobotMonitor.Shared.RobotView r) =>
        r.Disconnected ? "通信断" : RobotMonitor.Shared.Labels.Of(r.State);

    /// <summary>CSS のクラス名（色分け用）。</summary>
    protected static string StateClass(RobotMonitor.Shared.RobotView r) =>
        r.Disconnected ? "st-disconnected" : $"st-{r.State.ToString().ToLowerInvariant()}";
}

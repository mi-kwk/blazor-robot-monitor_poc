using RobotMonitor.Server.Components;
using RobotMonitor.Server.Monitoring;
using RobotMonitor.Shared;

var builder = WebApplication.CreateBuilder(args);

// 画面（Blazor WebAssembly）を配信する
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// 監視の仕組み：状態の保持・異常の判定、画面への即時の送信
builder.Services.AddSingleton<RobotStore>();
builder.Services.AddSignalR();
builder.Services.AddHostedService<RobotBroadcaster>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// 手元での動作確認だけなので http のみで動かす（https への転送はしない）

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(RobotMonitor.Client._Imports).Assembly);

// ---- API ----
var api = app.MapGroup("/api");

// 模擬ロボットからの報告（複数台分をまとめて受け取る）
api.MapPost("/reports", (List<RobotReport> reports, RobotStore store) =>
{
    store.Report(reports, DateTimeOffset.Now);
    return Results.NoContent();
});

// 画面が最初に表示するときの取得用（その後は SignalR で届く）
api.MapGet("/robots", (RobotStore store) => store.Snapshot());
api.MapGet("/alerts", (RobotStore store) => store.Alerts());

// 異常を確認済みにする
api.MapPost("/alerts/{id:int}/ack", (int id, RobotStore store) =>
    store.Acknowledge(id) ? Results.NoContent() : Results.NotFound());
api.MapPost("/alerts/ack-all", (RobotStore store) =>
{
    store.AcknowledgeAll();
    return Results.NoContent();
});

// ---- SignalR ----
app.MapHub<MonitorHub>(MonitorHub.Path);

app.Run();

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RobotMonitor.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// この画面はブラウザの中で動く。データベースには直接つながず、配信元のサーバーに API と SignalR でつなぐ。
var baseUri = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = baseUri });
builder.Services.AddSingleton(sp => new MonitorClient(new HttpClient { BaseAddress = baseUri }, baseUri));

await builder.Build().RunAsync();

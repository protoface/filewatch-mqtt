using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using PoC.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "WatchPuppy2");
LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(builder.Services);
builder.Services.AddKeyedSingleton<string[]>("cmdArgs", args);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

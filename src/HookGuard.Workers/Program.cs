using HookGuard.Workers;

var builder = Host.CreateApplicationBuilder(args);

// Phase 2 registers the outbox relay, the delivery workers and the retry scheduler here.
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

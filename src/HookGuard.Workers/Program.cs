using HookGuard.Application.Outbox;
using HookGuard.Domain.Time;
using HookGuard.Infrastructure;
using HookGuard.Infrastructure.Time;
using HookGuard.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHookGuardPersistence(
    builder.Configuration.GetConnectionString("HookGuard")
    ?? throw new InvalidOperationException(
        "Connection string 'HookGuard' is not configured. Set ConnectionStrings:HookGuard."));

builder.Services.AddHookGuardMessaging(
    builder.Configuration.GetConnectionString("RabbitMq")
    ?? throw new InvalidOperationException(
        "Connection string 'RabbitMq' is not configured. Set ConnectionStrings:RabbitMq."));

// Application services are registered here rather than in a self-contained extension so that the
// composition root stays the single place that decides what the process is made of.
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<OutboxRelay>();

// Phase 2 adds the delivery workers and the retry scheduler here.
builder.Services.AddHostedService<OutboxRelayService>();

var host = builder.Build();
host.Run();

using HookGuard.Application.Deliveries;
using HookGuard.Application.Outbox;
using HookGuard.Domain.Retry;
using HookGuard.Domain.Time;
using HookGuard.Infrastructure;
using HookGuard.Infrastructure.Randomness;
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

builder.Services.AddHookGuardDelivery();

// Application services are registered here rather than in a self-contained extension so that the
// composition root stays the single place that decides what the process is made of.
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IRandom, SystemRandom>();
builder.Services.AddSingleton<RetryPolicy>();
builder.Services.AddScoped<OutboxRelay>();
builder.Services.AddScoped<DeliveryWorker>();

builder.Services.AddHostedService<OutboxRelayService>();
builder.Services.AddHostedService<DeliveryWorkerService>();

var host = builder.Build();
host.Run();

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Phase 6 turns this into the chaos simulator: configurable failure rate, latency and timeouts,
// so the retry, backoff and dead-letter paths can be demonstrated without a real merchant.
app.MapGet("/", () => Results.Text(
    "HookGuard fake merchant. The chaos simulator (failure rate, latency, timeouts) arrives in phase 6.",
    "text/plain"));

app.MapPost("/webhooks/{**path}", () => Results.Ok(new { accepted = true }));

app.Run();

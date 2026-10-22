namespace HookGuard.Application.Deliveries;

/// <summary>
/// What one sweep of the delivery rows did, for the log line that says whether HookGuard is still
/// keeping up with them.
/// </summary>
/// <param name="Recovered">Deliveries handed back because the worker holding them was gone.</param>
/// <param name="Announced">Deliveries re-announced because their attempt had come due.</param>
public readonly record struct SweepResult(int Recovered, int Announced);

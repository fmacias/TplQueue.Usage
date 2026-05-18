using Microsoft.AspNetCore.SignalR;
using TplQueue.Usage.QueueObserverSignalRDashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddDashboardSample(builder.Configuration);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<QueueEventsHub>("/hubs/queue-events");

app.MapGet("/api/sample/runs", (DashboardRunStore store) =>
{
    return Results.Ok(store.SnapshotRuns());
});

app.MapGet("/api/sample/runs/{runId:guid}", (Guid runId, DashboardRunStore store) =>
{
    var run = store.TryGetRun(runId);
    return run == null ? Results.NotFound() : Results.Ok(run);
});

app.MapGet("/api/sample/runs/{runId:guid}/events", (Guid runId, DashboardRunStore store) =>
{
    return Results.Ok(store.SnapshotEvents(runId));
});

app.MapPost("/api/sample/runs/{mode}", async (string mode, DashboardRunCoordinator coordinator, CancellationToken ct) =>
{
    if (!DashboardRunModeParser.TryParse(mode, out var runMode))
    {
        return Results.BadRequest(new
        {
            message = "Unsupported run mode. Use 'wait' or 'cancel'."
        });
    }

    var startResult = await coordinator
        .TryStartRunAsync(DashboardRunScenario.Metadata, runMode, ct)
        .ConfigureAwait(false);

    return startResult.Started
        ? Results.Accepted($"/api/sample/runs/{startResult.Run!.RunId}", startResult.Run)
        : Results.Conflict(new { message = startResult.Message });
});

app.MapPost("/api/sample/payload-runs/{mode}", async (string mode, DashboardRunCoordinator coordinator, CancellationToken ct) =>
{
    if (!DashboardRunModeParser.TryParse(mode, out var runMode))
    {
        return Results.BadRequest(new
        {
            message = "Unsupported run mode. Use 'wait' or 'cancel'."
        });
    }

    var startResult = await coordinator
        .TryStartRunAsync(DashboardRunScenario.Payload, runMode, ct)
        .ConfigureAwait(false);

    return startResult.Started
        ? Results.Accepted($"/api/sample/runs/{startResult.Run!.RunId}", startResult.Run)
        : Results.Conflict(new { message = startResult.Message });
});

app.Run();

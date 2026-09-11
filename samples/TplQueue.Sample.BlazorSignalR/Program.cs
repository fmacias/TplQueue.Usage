using Microsoft.AspNetCore.DataProtection;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Microsoft.DependencyInjection;
using TplQueue.Sample.BlazorSignalR.Components;
using TplQueue.Sample.BlazorSignalR.Composition;
using TplQueue.Sample.Etl.Composition;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
    
// Add TplQueue services to the container
builder.Services.AddTplQueue(builder.Configuration, CoreApi.Create());
builder.Services.AddSampleEtlWorkflow();
builder.Services.AddMeasurementEtlConsumer();

// Register this last so the process-local dashboard does not depend on a
// writable, DPAPI-compatible user key store.
builder.Services.AddSingleton<IDataProtectionProvider>(
    new EphemeralDataProtectionProvider());

var app = builder.Build();

app.Services.RegisterSampleEtlPayloadHandlers();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

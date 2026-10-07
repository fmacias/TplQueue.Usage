using Microsoft.AspNetCore.DataProtection;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Microsoft.DependencyInjection;
using TplQueue.Sample.BlazorSignalR.Components;
using TplQueue.Sample.BlazorSignalR.Composition;
using TplQueue.Sample.Simulation.Composition;
using TplQueue.Sample.Domain.Composition;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddTplQueue(builder.Configuration, CoreApi.Create());
builder.Services.AddSampleDomain();
builder.Services.AddSampleEtlWorkflow();
builder.Services.AddSampleSingleJobSimulation();
builder.Services.AddMeasurementEtlConsumer();
builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());

var app = builder.Build();
app.Services.RegisterSampleEtlPayloadHandlers();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

using Microsoft.Extensions.DependencyInjection;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Composition;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Microsoft.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TplQueue.Sample.BlazorSignalR.Application;
using TplQueue.Sample.BlazorSignalR.Composition;
using TplQueue.Sample.Domain.Composition;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class SimulationRegistrationTests
{
    [Test]
    public void BlazorComposition_ValidatesAndResolvesFactoryWorkflowsAndHostedService()
    {
        // Arrange: use the same Domain, Simulation and monitoring registrations as Program.cs.
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTplQueue(configuration, CoreApi.Create());
        services.AddSampleDomain();
        services.AddSampleEtlWorkflow();
        services.AddSampleSingleJobSimulation();
        services.AddSampleMultipleFifoRootsSimulation();
        services.AddMeasurementEtlConsumer(configuration);

        // Act: validate constructor dependencies and lifetimes before resolving services.
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        // Assert: both job construction and cached-job rehydration can resolve their handlers.
        Assert.DoesNotThrow(() => provider.RegisterSampleEtlPayloadHandlers());
        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredService<ISampleJobFactory>(), Is.Not.Null);
            Assert.That(provider.GetServices<ISimulationWorkflow>().Count(), Is.EqualTo(3));
            Assert.That(provider.GetRequiredService<ISimulationService>(), Is.Not.Null);
            Assert.That(provider.GetServices<IHostedService>().OfType<SampleEtlDemoHostedService>().Count(),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void WorkflowRegistration_IncludesGraphMembershipBeforeAnyOutcome()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSampleEtlWorkflow();
        var registration = services.SingleOrDefault(item =>
            item.ServiceType.Name == "ISimulationGraphCatalog");

        // Assert
        Assert.That(registration, Is.Not.Null,
            "Root membership must be available before successful completion.");
        Assert.That(registration!.ServiceType.Assembly, Is.EqualTo(typeof(ISimulationWorkflow).Assembly));
        Assert.That(registration.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
    }

    [Test]
    public void WorkflowRegistration_IncludesOneSingletonWorkflow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSampleEtlWorkflow();
        var registration = services.SingleOrDefault(item =>
            item.ServiceType == typeof(ISimulationWorkflow));

        // Assert
        Assert.That(registration, Is.Not.Null,
            "The simulation module must own delivery independently of the host.");
        Assert.That(registration!.ServiceType.Assembly, Is.EqualTo(typeof(ISimulationWorkflow).Assembly));
        Assert.That(registration.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
    }

    [Test]
    public void WorkflowRegistration_RejectsMissingServices()
    {
        // Act / Assert
        Assert.Multiple(() =>
        {
            Assert.That(() => SampleEtlServiceCollectionExtensions.AddSampleEtlWorkflow(null!),
                Throws.ArgumentNullException);
            Assert.That(() => SampleEtlServiceCollectionExtensions.AddSampleSingleJobSimulation(null!),
                Throws.ArgumentNullException);
            Assert.That(() => SampleEtlServiceCollectionExtensions.AddSampleMultipleFifoRootsSimulation(null!),
                Throws.ArgumentNullException);
        });
    }

    [Test]
    public void MultipleFifoRootsRegistration_IsIdempotent()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSampleMultipleFifoRootsSimulation();
        services.AddSampleMultipleFifoRootsSimulation();

        // Assert
        var workflow = services.Single(item => item.ServiceType == typeof(ISimulationWorkflow));
        Assert.That(workflow.ImplementationType!.Name, Is.EqualTo("MultipleFifoRootsWorkflow"));
        Assert.That(workflow.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
        Assert.That(services.Count(item => item.ServiceType == typeof(IEtlQueueRuntime)), Is.EqualTo(1));
        Assert.That(services.Any(item => item.ServiceType == typeof(ISampleJobFactory)), Is.False);
    }

    [Test]
    public void CombinedRegistration_RegistersOneSingletonPerTypeWithoutDomainServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSampleEtlWorkflow();
        services.AddSampleSingleJobSimulation();
        services.AddSampleMultipleFifoRootsSimulation();
        services.AddSampleEtlWorkflow();
        services.AddSampleSingleJobSimulation();
        services.AddSampleMultipleFifoRootsSimulation();

        // Assert
        Assert.Multiple(() =>
        {
            var workflows = services.Where(item => item.ServiceType == typeof(ISimulationWorkflow)).ToArray();
            Assert.That(workflows, Has.Length.EqualTo(3));
            Assert.That(workflows.Select(item => item.ImplementationType!.Name), Is.EquivalentTo(new[]
                { "EtlSimulationWorkflow", "SingleJobWorkflow", "MultipleFifoRootsWorkflow" }));
            Assert.That(workflows.Select(item => item.Lifetime), Is.All.EqualTo(ServiceLifetime.Singleton));
            Assert.That(services.Count(item => item.ServiceType == typeof(ISimulationGraphCatalog)), Is.EqualTo(1));
            Assert.That(services.Single(item => item.ServiceType == typeof(ISimulationService)).Lifetime,
                Is.EqualTo(ServiceLifetime.Singleton));
            Assert.That(services.Any(item => item.ServiceType == typeof(ISampleJobFactory)), Is.False);
            Assert.That(services.Any(item => item.ServiceType == typeof(ISampleFifoQ)), Is.False);
            Assert.That(typeof(SampleEtlServiceCollectionExtensions).Assembly.GetReferencedAssemblies()
                .Select(assembly => assembly.Name), Does.Not.Contain("TplQueue.Sample.Domain"));
        });
    }
}

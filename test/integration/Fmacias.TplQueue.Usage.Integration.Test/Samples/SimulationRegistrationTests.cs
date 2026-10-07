using Microsoft.Extensions.DependencyInjection;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Composition;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class SimulationRegistrationTests
{
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
        });
    }

    [Test]
    public void CombinedRegistration_RegistersOneSingletonPerTypeWithoutDomainServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSampleEtlWorkflow();
        services.AddSampleSingleJobSimulation();
        services.AddSampleEtlWorkflow();
        services.AddSampleSingleJobSimulation();

        // Assert
        Assert.Multiple(() =>
        {
            var workflows = services.Where(item => item.ServiceType == typeof(ISimulationWorkflow)).ToArray();
            Assert.That(workflows, Has.Length.EqualTo(2));
            Assert.That(workflows.Select(item => item.ImplementationType).Distinct().Count(), Is.EqualTo(2));
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

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
        Assert.That(registration!.ServiceType.Assembly, Is.EqualTo(typeof(IEtlWorkflow).Assembly));
        Assert.That(registration.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
    }

    [Test]
    public void WorkflowRegistration_IncludesModuleOwnedSimulationLifecycle()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSampleEtlWorkflow();
        var registration = services.SingleOrDefault(item =>
            item.ServiceType.Name == "ISimulationService");

        // Assert
        Assert.That(registration, Is.Not.Null,
            "The simulation module must own finite delivery independently of the host.");
        Assert.That(registration!.ServiceType.Assembly, Is.EqualTo(typeof(IEtlWorkflow).Assembly));
        Assert.That(registration.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
    }

    [Test]
    public void WorkflowRegistration_RejectsMissingOrDuplicateScenariosBeforeCreatingTimers()
    {
        var services = new ServiceCollection();
        var settings = new SimulationScenarioSettings("same", AvailableQueue.FIFO,
            TimeSpan.FromSeconds(3), TimeSpan.Zero, 1, 2, 2);

        Assert.Multiple(() =>
        {
            Assert.That(() => SampleEtlServiceCollectionExtensions.AddSampleEtlWorkflow(null!),
                Throws.ArgumentNullException);
            Assert.That(() => services.AddSampleEtlWorkflow(null!), Throws.ArgumentNullException);
            Assert.That(() => services.AddSampleEtlWorkflow(Array.Empty<SimulationScenarioSettings>()),
                Throws.ArgumentException);
            Assert.That(() => services.AddSampleEtlWorkflow(new SimulationScenarioSettings[] { null! }),
                Throws.ArgumentException);
            Assert.That(() => services.AddSampleEtlWorkflow(new[] { settings, settings }),
                Throws.ArgumentException);
        });
        Assert.That(services, Is.Empty);
    }
}

using System.Reflection;
using Fmacias.TplQueue.Contracts;
using Microsoft.Extensions.DependencyInjection;
using TplQueue.Sample.BlazorSignalR.Composition;
using TplQueue.Sample.BlazorSignalR.Presentation.Etl;
using TplQueue.Sample.Etl.Contracts;
using TplQueue.Sample.Simulation.Composition;
using TplQueue.Sample.Simulation.Runtime;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class SimulationRuntimeAccessTests
{
    [Test]
    public void BlazorAndSimulation_UseTheSameRuntimeAndQueueInstances()
    {
        // Arrange: wrapper factories are transient, just as in Domain registration.
        var queues = new SampleQueues();
        var resolutions = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<ISampleFifoQ>(_ => { resolutions++; return queues; });
        services.AddTransient<ISampleParallelQ>(_ => { resolutions++; return queues; });
        services.AddTransient<ISampleCacheQ>(_ => { resolutions++; return queues; });
        services.AddSampleEtlWorkflow();
        services.AddSampleSingleJobSimulation();
        services.AddMeasurementEtlConsumer();
        using var provider = services.BuildServiceProvider();

        // Act
        var runtime = provider.GetRequiredService<IEtlQueueRuntime>();
        var catalog = provider.GetRequiredService<EtlQueueCatalog>();
        using (runtime.Subscribe(provider.GetRequiredService<EtlQueueObserver>()))
        {
            runtime.ResumePolling();

            // Assert: catalog, observer and workflow runtime all use the captured queues.
            Assert.That(provider.GetService<EtlQueueRuntime>(), Is.Null,
                "Consumers resolve the runtime through its registered contract.");
            Assert.That(runtime, Is.SameAs(provider.GetRequiredService<IEtlQueueRuntime>()));
            foreach (var queue in Enum.GetValues<AvailableQueue>())
            {
                var inner = runtime.GetQueue(queue);
                var proxy = (QueueSubscriptionProxy)inner;
                Assert.That(inner, Is.SameAs(queues.GetQueue(queue)));
                Assert.That(catalog.Resolve(inner.QueueId).MaxParallelism, Is.EqualTo(inner.MaxParallelism));
                Assert.That(proxy.SubscriptionCount, Is.EqualTo(2));
                Assert.That(proxy.ResumeCalls, Is.EqualTo(1));
            }
            Assert.That(resolutions, Is.EqualTo(3));
        }
        Assert.That(Enum.GetValues<AvailableQueue>().Select(queue =>
            ((QueueSubscriptionProxy)runtime.GetQueue(queue)).SubscriptionCount), Is.All.EqualTo(1));
        Assert.That(() => runtime.GetQueue((AvailableQueue)99), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => runtime.Subscribe(null!), Throws.ArgumentNullException);

        provider.Dispose();
        Assert.That(() => runtime.GetQueue(AvailableQueue.FIFO), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => runtime.ResumePolling(), Throws.TypeOf<ObjectDisposedException>());
    }

    private sealed class SampleQueues : ISampleFifoQ, ISampleParallelQ, ISampleCacheQ
    {
        public IFifoQ InnerFifoQ { get; } = DispatchProxy.Create<IFifoQ, QueueSubscriptionProxy>();
        public IParallelQ InnerParallelQ { get; } = DispatchProxy.Create<IParallelQ, QueueSubscriptionProxy>();
        public ICacheQ InnerCacheQ { get; } = DispatchProxy.Create<ICacheQ, QueueSubscriptionProxy>();
        public IQ GetQueue(AvailableQueue queue) => queue switch
        {
            AvailableQueue.FIFO => InnerFifoQ,
            AvailableQueue.Parallel => InnerParallelQ,
            AvailableQueue.Cache => InnerCacheQ,
            _ => throw new ArgumentOutOfRangeException(nameof(queue))
        };
        public void Dispose() { }
    }
}

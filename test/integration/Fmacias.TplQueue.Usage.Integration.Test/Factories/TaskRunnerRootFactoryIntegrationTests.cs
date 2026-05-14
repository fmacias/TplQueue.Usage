using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core.Factories;
using Fmacias.TplQueue.Defaults;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace Fmacias.TplQueue.Integration.Test.Factories
{
    [TestFixture]
    public class JobRootFactoryIntegrationTests
    {
        private IJobRootFactory _jobRootFactory = null!;
        private IJobFactory _jobFactory = null!;
        private IQFactory _queueFactoryCore = null!;
        private Dictionary<string, IQOptions> _queueOptions = null!;
        private IApi _api = null;
        [SetUp]
        public void SetUp()
        {
            _queueOptions = new Dictionary<string, IQOptions>
            {
                { "fifo", new QOptions(maxParallelism: 1, retryPolicy: "no-retry", id: Guid.NewGuid()) }
            };
            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>
            {
                { "no-retry", RetryPolicyOptions.Create(baseDelayMs: 0, maxRetries: 0) }
            };

            _api = Helper.GetApi(retryPolicyOptions, _queueOptions);
            _jobRootFactory = _api.JobFactory;
            _jobFactory = _api.JobFactory;
            _queueFactoryCore = _api.QFactory;
        }

        [TearDown]
        public void TearDown()
        {
        }

        [Test]
        public void Create_WithSameParameters_NeverSingleton()
        {
            var next = _api.JobFactory;
            Assert.That(next, Is.SameAs(_jobRootFactory));
        }

        [Test]
        public async Task CreateRoot_PropagatesRetryPolicyToChildGraph()
        {
            int calls = 0;
            Func<IRetryPolicy> rootPolicy = () => new CountingRetryPolicy(() => Interlocked.Increment(ref calls));

            var root = _jobRootFactory.JobRoot(ct => Task.CompletedTask, rootPolicy, "root");
            var child = _jobFactory.Job(ct => Task.CompletedTask, "child");
            root.After(child);

            using var queue = _queueFactoryCore.Fifo(Guid.NewGuid(), "fifo", Helper.GetLogger<IFifoQ>());

            queue.Enqueue(root, CancellationToken.None);
            queue.ResumePolling();

            await root.WaitUntilFinishedAsync();
            queue.Dispose();

            Assert.That(calls, Is.EqualTo(2));
        }
    }
}


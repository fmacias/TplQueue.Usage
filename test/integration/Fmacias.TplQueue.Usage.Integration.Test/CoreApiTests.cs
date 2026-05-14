using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.Core.Jobs;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.RetryPolicies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework.Internal;

namespace Fmacias.TplQueue.Integration.Test
{
    internal sealed class TestDispatcherOptions : IQOptions
    {
        public TestDispatcherOptions(int maxParallelism, int pulseMs, string retryPolicy, Guid id)
        {
            MaxParallelism = maxParallelism;
            PulseMs = pulseMs;
            RetryPolicy = retryPolicy;
            Id = id;
        }

        public int MaxParallelism { get; }
        public int PulseMs { get; }
        public string RetryPolicy { get; }
        public Guid Id { get; }
    }

    [TestFixture]
    public class CoreApiTests
    {
        private ILogger<IParallelQ> _logger = null!;
        private ILoggerFactory _loggerFactory = null!;
        private IParallelQ? _queue;
        private Dictionary<string, IQOptions> _queueOptions = null!;
        private Dictionary<string, IRetryPolicyOptions> _retryPolicyOptions = null!;

        [SetUp]
        public void SetUp()
        {
            _loggerFactory = NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<IParallelQ>();
            _queueOptions = new Dictionary<string, IQOptions>
            {
                { "main", new TestDispatcherOptions(2, 20, "no-retry", Guid.NewGuid()) }
            };
            _retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>
            {
                { "no-retry", RetryPolicyOptions.Create(baseDelayMs: 0, maxRetries: 0) }
            };
        }

        [Test]
        [TearDown]
        public void TearDown()
        {
            _queue?.Dispose();
            _loggerFactory?.Dispose();
        }

        [Test]
        public async Task CoreApi_Factories_CreateDispatcherAndRunGraph()
        {
            var coreApi = CoreApi.Create();
            var retryFactory = RetryPolicyAbstractFactory.Create();
            var queueFactory = coreApi.QFactory;
            var runnerFactory = coreApi.JobFactory;
            var rootFactory = coreApi.JobFactory;

            _queue = queueFactory.Parallel(Guid.NewGuid(), "main",
                4,
                _logger,
                () => retryFactory.GetPolicy<NoRetryPolicy>());

            var execution = new List<string>();
            var child = runnerFactory.Job(ct => execution.Add("child"), "child");
            var root = rootFactory.JobRoot(ct => execution.Add("root"), 
                () => retryFactory.PolicyByName("no-retry",_retryPolicyOptions), 
                "root");
            root.After(child);

            _queue.Enqueue(root, CancellationToken.None);
            _queue.ResumePolling();

            await root.WaitUntilFinishedAsync();
            
            CollectionAssert.AreEqual(new[] { "child", "root" }, execution);
        }

        [Test]
        public async Task ApiFacade_Factories_CreateDispatcherAndRunGraph()
        {
            var coreApi = CoreApi.Create();
            var api = Helper.GetApi(_retryPolicyOptions,
                _queueOptions);
            var retryFactory = api.RetryPolicyAbstractFactory;
            var queueFactory = api.QFactory;
            var runnerFactory = api.JobFactory;
            var rootFactory = api.JobFactory;

            _queue = queueFactory.Parallel(Guid.NewGuid(), "main",
                4,
                _logger,
                () => retryFactory.GetPolicy<NoRetryPolicy>());

            var execution = new List<string>();
            var child = runnerFactory.Job(ct => execution.Add("child"), "child");
            var root = rootFactory.JobRoot(
                body: ct => execution.Add("root"), 
                () => retryFactory.PolicyByName("no-retry", _retryPolicyOptions), 
                "root");
            root.After(child);

            _queue.Enqueue(root, CancellationToken.None);
            _queue.ResumePolling();

            await root.WaitUntilFinishedAsync();

            CollectionAssert.AreEqual(new[] { "child", "root" }, execution);
        }

        [Test]
        public void CoreApi_Factories_ExposeSeparatedJobAndRootContracts()
        {
            var coreApi = CoreApi.Create();
            var child = coreApi.JobFactory.Job(ct => { }, "child");
            var root = coreApi.JobFactory.JobRoot(ct => { }, name: "root");

            var chainedRoot = child.Then(root);

            Assert.That(typeof(IJob).IsAssignableFrom(typeof(IJobRoot)), Is.False);
            Assert.That(chainedRoot, Is.SameAs(root));
            Assert.That(root.GetJobsBatch(), Has.Length.EqualTo(1));
            Assert.That(root.GetJobsBatch()[0], Is.SameAs(child));
        }
    }
}


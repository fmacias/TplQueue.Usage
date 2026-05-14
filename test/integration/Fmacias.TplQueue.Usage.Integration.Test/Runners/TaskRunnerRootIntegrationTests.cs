using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core.Factories;
using Fmacias.TplQueue.Core.Jobs;
using Fmacias.TplQueue.Defaults;
using Microsoft.Extensions.Logging;
using System.Data;

namespace Fmacias.TplQueue.Integration.Test.Runners
{
    [TestFixture]
    public class JobRootIntegrationTests
    {
        private IParallelQ _q = null!;
        private ILogger<IParallelQ> _logger = null!;
        private IRetryPolicyAbstractFactory _retryPolicyFactory = null!;
        private IQFactoryAdapter _queueFactory = null!;
        private IApi _api = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _api = Helper.GetApi(new Dictionary<string, IRetryPolicyOptions>(), 
                new Dictionary<string, IQOptions>());
            _logger = Helper.GetLogger<IParallelQ>();
            _retryPolicyFactory = _api.RetryPolicyAbstractFactory;
            _queueFactory = _api.QFactory;
        }

        [SetUp]
        public void SetUp()
        {
            _q = _queueFactory.Parallel(
                Guid.NewGuid(), 
                "root-runner-integration-dispatcher",
                maxParallelism: 2,
                logger: _logger,
                retryPolicyFactory: () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>());
        }

        [TearDown]
        public void TearDown()
        {
            _q.Dispose();
        }

        [Test]
        public async Task EnqueueRoot_UsesRootRetryPolicyInsteadOfDispatcherDefault()
        {
            int dispatcherPolicyCalls = 0;
            int rootPolicyCalls = 0;

            Func<IRetryPolicy> dispatcherPolicy = () => new CountingRetryPolicy(() => dispatcherPolicyCalls++);
            Func<IRetryPolicy> rootPolicy = () => new CountingRetryPolicy(() => rootPolicyCalls++);

            _q.Dispose();
            _q = _queueFactory.Parallel(
                Guid.NewGuid(), 
                "root-policy-test",
                maxParallelism: 1,
logger: _logger, retryPolicyFactory: dispatcherPolicy);

            var jobRootFactory = _api.JobFactory;
            var jobFactory = _api.JobFactory;
            var root = jobRootFactory.JobRoot(ct => Task.CompletedTask, rootPolicy, "root");
            var child = jobFactory.Job(ct => Task.CompletedTask, "child");
            root.After(child);

            _q.Enqueue(root, CancellationToken.None);
            _q.ResumePolling();

            await root.WaitUntilFinishedAsync();

            Assert.That(rootPolicyCalls, Is.GreaterThanOrEqualTo(1), "Root-specific policy should be invoked.");
            Assert.That(dispatcherPolicyCalls, Is.EqualTo(0), "Dispatcher default policy should not be used when root provides one.");
        }

        private sealed class CountingRetryPolicy : IRetryPolicy
        {
            private readonly Action _onExecute;

            public CountingRetryPolicy(Action onExecute)
            {
                _onExecute = onExecute;
            }

            public int RetryCount { get; private set; }

            public Task<TResult> ExecuteAsync<TResult>(Func<CancellationToken, Task<TResult>> action, CancellationToken cancellationToken)
            {
                RetryCount++;
                _onExecute?.Invoke();
                return action(cancellationToken);
            }

            public IRetryPolicy SetFromDescriptor(IRetryPolicyOptions descriptor)
            {
                throw new NotImplementedException();
            }

            public IRetryPolicy SetFromOptions(RetryPolicyOptions options)
            {
                throw new NotImplementedException();
            }

            public IRetryPolicyOptions ToDescriptor()
            {
                throw new NotImplementedException();
            }
        }
    }
}


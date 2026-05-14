using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core.Jobs.Internals;
using Fmacias.TplQueue.Defaults;
using Microsoft.Extensions.Logging;

namespace Fmacias.TplQueue.Integration.Test.Factories
{
    [TestFixture]
    public class JobFactoryIntegrationTests
    {
        private IJobFactory _jobFactory = null!;
        private IJobRootFactory _jobRoot = null!;
        private IQFactory _queueFactoryCore = null!;
        private ILogger<IParallelQ> _logger = null!;

        [SetUp]
        public void SetUp()
        {
            var api = Helper.GetApi(
                new Dictionary<string, IRetryPolicyOptions>(),
                new Dictionary<string, IQOptions>());
            _jobFactory = api.JobFactory;
            _jobRoot = api.JobFactory;
            _queueFactoryCore = api.QFactory;
            _logger = Helper.GetLogger<IParallelQ>();
        }

        [Test]
        public async Task CreateGraph_WithFactories_DispatcherExecutesInDependencyOrder()
        {
            // Arrange
            var dispatcher = _queueFactoryCore.Parallel(
                Guid.NewGuid(), 
                name: "order",
                maxParallelism: 2,
                logger: _logger, retryPolicyFactory: () => NoRetryPolicy.Create());

            var execution = new List<string>();

            var child1 = _jobFactory.Job(ct => execution.Add("child-1"), name: "child-1");
            var child2 = _jobFactory.Job(ct => execution.Add("child-2"), name: "child-2");
            var root = _jobRoot.JobRoot(ct => execution.Add("root"), name: "root");

            root.After(child2);
            child2.After(child1);

            // Act
            dispatcher.Enqueue(root, CancellationToken.None);
            dispatcher.ResumePolling();

            /*
             * ALTERNATIVE: await for all the runners in this manner
             * 
             * But it is not neccessary, because the Root element will always be the last runner to be executed.
             * 
             * Example to away explicitelly all Runners
             * ----------------------------------------
             var all = Task.WhenAll(
             root.WaitUntilFinishedAsync(),
             child1.WaitUntilFinishedAsync(),
             child2.WaitUntilFinishedAsync());
             var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(5)));
            */
            //act: Await the root in a deterministic way
            //     to avoid setting a  timer for testing purpouses.
            //     But is not one obligation to await a runner.
            await root.WaitUntilFinishedAsync();
            dispatcher.Dispose();

            // Assert
            //Assert.That(completed, Is.EqualTo(all), "Graph execution should finish within timeout.");
            Assert.That(root.ExecutionEnd > child2.ExecutionEnd 
                && child2.ExecutionEnd > child1.ExecutionEnd, 
                Is.EqualTo(true));
            CollectionAssert.AreEqual(new[] { "child-1", "child-2", "root" }, execution);
        }

        [Test]
        public async Task CreateRoot_WithCustomRetryPolicy_TakesPrecedenceOverDispatcherDefault()
        {
            // Arrange
            int dispatcherPolicyInvocations = 0;
            int rootPolicyInvocations = 0;

            Func<IRetryPolicy> QueuePolicyFactory =
                () => new CountingRetryPolicy(() => Interlocked.Increment(ref dispatcherPolicyInvocations));

            Func<IRetryPolicy> rootPolicyFactory =
                () => new CountingRetryPolicy(() => Interlocked.Increment(ref rootPolicyInvocations));

            var dispatcher = _queueFactoryCore.Parallel(
                Guid.NewGuid(), 
                name: "custom-retry",
                maxParallelism: 1,
logger: _logger, retryPolicyFactory: QueuePolicyFactory);

            var root = _jobRoot.JobRoot(
                body: ct => Task.CompletedTask,
                retryPolicyFactory: rootPolicyFactory,
                name: null);

            var child = _jobFactory.Job(ct => Task.CompletedTask, name: null);
            root.After(child);

            // Act
            dispatcher.Enqueue(root, CancellationToken.None);
            dispatcher.ResumePolling();

            await root.WaitUntilFinishedAsync();
            dispatcher.Dispose();

            // Assert
            Assert.That(rootPolicyInvocations, Is.GreaterThanOrEqualTo(1), "Root-specific retry policy should be used.");
            Assert.That(dispatcherPolicyInvocations, Is.EqualTo(0), "Dispatcher-level retry policy should not be used when runner provides one.");
            Assert.That(root.Name, Is.EqualTo(string.Empty));
            Assert.That(child.Name, Is.EqualTo(string.Empty));
            Assert.That(root.ExecutionEnd > child.ExecutionEnd, Is.EqualTo(true));
        }
    }

    internal sealed class CountingRetryPolicy : IRetryPolicy
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



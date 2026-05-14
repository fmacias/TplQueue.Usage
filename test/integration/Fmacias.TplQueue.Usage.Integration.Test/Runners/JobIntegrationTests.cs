using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core.Jobs;
using Fmacias.TplQueue.Defaults;
using Microsoft.Extensions.Logging;

namespace Fmacias.TplQueue.Integration.Test.Runners
{
    [TestFixture]
    public class JobIntegrationTests
    {
        private IParallelQ _dispatcher = null!;
        private ILogger<IParallelQ> _logger = null!;
        private IRetryPolicyAbstractFactory _retryPolicyFactory = null!;
        private IQFactoryAdapter _queueFactory = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>();
            var api = Helper.GetApi(retryPolicyOptions, new Dictionary<string, IQOptions>());
            _logger = Helper.GetLogger<IParallelQ>();
            _retryPolicyFactory = api.RetryPolicyAbstractFactory;
            var dispatcherOptions = new Dictionary<string, IQOptions>();
            _queueFactory = api.QFactory;
        }

        [SetUp]
        public void SetUp()
        {
            var noRetryPolicyFactory = () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>();
            _dispatcher = _queueFactory.Parallel(
                Guid.NewGuid(), 
                "Runner integration dispatcher",
                maxParallelism: 4,
                logger: _logger, 
                retryPolicyFactory: noRetryPolicyFactory);
        }

        [TearDown]
        public void TearDown()
        {
            _dispatcher.Dispose();
        }

        [Test]
        public async Task EnqueueRoot_WithDependency_ExecutesGraphInOrder()
        {
            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>();
            var api = Helper.GetApi(retryPolicyOptions, new Dictionary<string, IQOptions>());
            List<string> execution = new();
            var jobFactory = api.JobFactory;
            var jobRootFactory = api.JobFactory;

            var child = jobFactory.Job(ct => execution.Add("child"), "child");
            var root = jobRootFactory.JobRoot(ct => execution.Add("root"), name: "root");
            root.After(child);

            _dispatcher.Enqueue(root, CancellationToken.None);
            _dispatcher.ResumePolling();

            await Task.WhenAll(
                root.WaitUntilFinishedAsync(),
                child.WaitUntilFinishedAsync()).WaitAsync(TimeSpan.FromSeconds(5));

            CollectionAssert.AreEqual(new[] { "child", "root" }, execution);
        }

        [Test]
        public async Task EnqueueRoot_CancellationToken_PreventsExecution()
        {
            bool executed = false;
            using var cts = new CancellationTokenSource();

            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>();
            var api = Helper.GetApi(retryPolicyOptions, new Dictionary<string, IQOptions>());
            List<string> execution = new();
            var jobFactory = api.JobFactory;
            var jobRootFactory = api.JobFactory;
            var root = jobRootFactory.JobRoot(ct => executed = true, name: "cancel-root");
            _dispatcher.Enqueue(root, cts.Token);
            _dispatcher.ResumePolling();

            cts.Cancel();

            var waitTask = root.WaitUntilFinishedAsync();
            var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromMilliseconds(250)));

            Assert.That(completed, Is.Not.SameAs(waitTask), "Runner should not complete when cancelled.");
            Assert.IsFalse(waitTask.IsCompleted);
            Assert.IsFalse(executed);
        }

        [Test]
        public async Task EnqueueRoot_CancellationAfterStart_CancelsJobAndAllowsFollowingRoot()
        {
            using var cts = new CancellationTokenSource();
            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>();
            var api = Helper.GetApi(retryPolicyOptions, new Dictionary<string, IQOptions>());
            var jobFactory = api.JobFactory;

            var firstStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondExecutedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var firstRoot = jobFactory.JobRoot(async ct =>
            {
                firstStartedTcs.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }, name: "cancel-after-start");

            var secondRoot = jobFactory.JobRoot(ct => secondExecutedTcs.TrySetResult(), name: "after-cancel");

            _dispatcher.Enqueue(firstRoot, cts.Token);
            _dispatcher.Enqueue(secondRoot, CancellationToken.None);
            _dispatcher.ResumePolling();

            await firstStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cts.Cancel();

            var firstWaitTask = firstRoot.WaitUntilFinishedAsync();
            var firstCompleted = await Task.WhenAny(firstWaitTask, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.That(firstCompleted, Is.SameAs(firstWaitTask), "Canceled root did not finalize in time.");
            Assert.That(firstWaitTask.IsCanceled, Is.True, "Canceled root should complete as canceled after execution starts.");

            var secondCompleted = await Task.WhenAny(secondExecutedTcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.That(secondCompleted, Is.SameAs(secondExecutedTcs.Task), "Following root did not execute after the canceled root released the dispatcher slot.");
            Assert.That(secondExecutedTcs.Task.IsCompletedSuccessfully, Is.True);
        }
    }
}


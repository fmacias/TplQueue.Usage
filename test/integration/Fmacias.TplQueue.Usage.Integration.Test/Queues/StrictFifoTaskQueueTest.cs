using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core.Factories;
using Fmacias.TplQueue.Core.Jobs;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.Integration.Test;
using Fmacias.TplQueue.RetryPolicies;
using Microsoft.Extensions.Logging;

namespace Fmacias.TplQueue.Integration.Test.Queues
{
    [TestFixture]
    public class StrictFifoTaskQueueTest
    {
        private IFifoQ _queue = null!;
        private ILogger<IFifoQ> _logger = null!;
        private IRetryPolicyAbstractFactory _retryPolicyFactory = null!;
        private IQFactory _queueFactory = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var api = Helper.GetApi(new Dictionary<string, IRetryPolicyOptions>(),
                new Dictionary<string, IQOptions>());

            _logger = Helper.GetLogger<IFifoQ>();
            _retryPolicyFactory = api.RetryPolicyAbstractFactory;
            _queueFactory = api.QFactory;
        }

        [SetUp]
        public void Setup()
        {
            _queue = _queueFactory.Fifo(
                Guid.NewGuid(), "Strict fifo test-dispatcher",
                logger: _logger,
                retryPolicy: () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>());
        }

        [TearDown]
        public void TearDown()
        {
            _queue.Dispose();
        }

        [Test]
        public async Task Enqueue_Action_JobExecutesInOrder()
        {
            var executionOrder = new List<int>();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue
                .Enqueue((ct, eo) =>
                {
                    executionOrder.Add(1);
                }, executionOrder, CancellationToken.None)
                 .Enqueue((ct, eo) =>
                 {
                     executionOrder.Add(2);
                     completion.TrySetResult();
                 }, executionOrder, CancellationToken.None);

            _queue.ResumePolling();

            Assert.IsTrue(await WaitForCompletion(completion.Task), "Dispatcher did not finish before timeout.");
            CollectionAssert.AreEqual(new[] { 1, 2 }, executionOrder);
        }

        [Test]
        public async Task Enqueue_FuncTask_JobExecutesInOrder()
        {
            var executionOrder = new List<int>();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue
                .Enqueue(async (ct, eo) =>
                {
                    executionOrder.Add(1);
                    await Task.Delay(10, ct);
                }, executionOrder, CancellationToken.None)
                .Enqueue(async (ct, eo) =>
                {
                    executionOrder.Add(2);
                    completion.TrySetResult();
                    await Task.Delay(10, ct);
                }, executionOrder, CancellationToken.None);

            _queue.ResumePolling();

            Assert.IsTrue(await WaitForCompletion(completion.Task), "Dispatcher did not finish before timeout.");
            CollectionAssert.AreEqual(new[] { 1, 2 }, executionOrder);
        }
        [Test]
        public async Task AddToQueue_EnforcesFifoOrdering()
        {
            var api = Helper.GetApi(new Dictionary<string, IRetryPolicyOptions>(), new Dictionary<string, IQOptions>());

            var executionOrder = new List<int>();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var jobRootFactory = api.JobFactory; 
            var first = jobRootFactory.JobRoot(ct =>
            {
                executionOrder.Add(1);
                return Task.CompletedTask;
            }, name: "first");

            var second = jobRootFactory.JobRoot(ct =>
            {
                executionOrder.Add(2);
                completion.TrySetResult();
                return Task.CompletedTask;
            }, name: "second");

            _queue.Enqueue(first, CancellationToken.None);
            _queue.Enqueue(second, CancellationToken.None);

            _queue.ResumePolling();

            Assert.IsTrue(await WaitForCompletion(completion.Task), "Dispatcher did not finish before timeout.");
            CollectionAssert.AreEqual(new[] { 1, 2 }, executionOrder);
        }
        private static async Task<bool> WaitForCompletion(Task task, int timeoutMs = 1000)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeoutMs)) == task;
            return completed && task.IsCompleted;
        }
    }
}


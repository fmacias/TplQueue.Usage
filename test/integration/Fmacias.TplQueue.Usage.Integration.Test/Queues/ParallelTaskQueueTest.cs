using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core.Factories;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.Integration.Test;
using Fmacias.TplQueue.RetryPolicies;
using Microsoft.Extensions.Logging;

namespace Fmacias.TplQueue.Integration.Test.Queues
{
    [TestFixture()]
    public class ParallelTaskQueueTest
    {
        private IParallelQ _queue = null!;
        private ILogger<IParallelQ> _logger = null!;
        private IRetryPolicyAbstractFactory _retryPolicyFactory = null!;
        private IQFactory _qFactory = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>();
            var api = Helper.GetApi(retryPolicyOptions, new Dictionary<string, IQOptions>());
            _logger = Helper.GetLogger<IParallelQ>();
            _retryPolicyFactory = api.RetryPolicyAbstractFactory;
            var dispatcherOptions = new Dictionary<string, IQOptions>();
            _qFactory = api.QFactory;
        }

        [SetUp]
        public void Setup()
        {
            var noRetryPolicyFactory = () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>();
            
            _queue = _qFactory.Parallel(
                Guid.NewGuid(), "Default test-TaskDipatcher",
                maxParallelism: 8,
                logger: _logger,
                retryPolicyFactory: () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>());
        }

        [TearDown]
        public void TearDown()
        {
            _queue.Dispose();
        }

        [Test]
        public async Task Enqueue_Action_JobExecutes()
        {
            int count = 0;
            string[] executionOrder = new string[2] { "", "" };

            _queue
                .Enqueue((ct, eo) =>
                {
                    Task.Delay(100).Wait();
                    count++;
                    eo[0] = count.ToString();
                }, executionOrder, CancellationToken.None)
                 .Enqueue((ct, eo) =>
                 {
                     Task.Delay(20).Wait();
                     count++;
                     eo[1] = count.ToString();
                 }, executionOrder, CancellationToken.None);

            _queue.ResumePolling();

            await _queue.Wait();
            Assert.AreEqual("2", executionOrder[0]);
            Assert.AreEqual("1", executionOrder[1]);
            _queue.Dispose();
        }

        [Test]
        public async Task Enqueue_FuncTask_JobExecutes()
        {
            int count = 0;
            string[] executionOrder = new string[2] { "", "" };

            _queue
                .Enqueue(async (ct, eo) =>
                {
                    await Task.Delay(100);
                    count++;
                    eo[0] = count.ToString();
                }, executionOrder, CancellationToken.None)
                .Enqueue(async (ct, eo) =>
                {
                    await Task.Delay(20);
                    count++;
                    eo[1] = count.ToString();
                }, executionOrder, CancellationToken.None);

            _queue.ResumePolling();

            await _queue.Wait();
            Assert.AreEqual("2", executionOrder[0]);
            Assert.AreEqual("1", executionOrder[1]);
            _queue.Dispose();
        }
    }
}


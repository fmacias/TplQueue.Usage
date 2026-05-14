using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.RetryPolicies;

namespace Fmacias.TplQueue.Integration.Test.Factories
{
    internal class QOptions : IQOptions
    {
        public QOptions(int maxParallelism, string retryPolicy, Guid id)
        {
            MaxParallelism = maxParallelism;
            RetryPolicy = retryPolicy;
            Id = id;
        }

        public int MaxParallelism { get; }
        public string RetryPolicy { get; }

        public Guid Id { get; }
    }

    [TestFixture]
    public class QFactoryAdapterTest
    {
        private IQFactoryAdapter _queueFactory = null!;
        private Dictionary<string, IQOptions> _queueOptions = null!;

        [SetUp]
        public void SetUp()
        {
            //Queue options would be collected from the configuration file of the client application.
            _queueOptions = new Dictionary<string, IQOptions>
            {
                { "parallel", new QOptions(maxParallelism: 3, retryPolicy: "linear", id: Guid.NewGuid()) },
                { "fifo", new QOptions(maxParallelism: 1, retryPolicy: "linear", id: Guid.NewGuid()) }
            };

            //Retry policies would be also collected from the configuration file of the client application.
            var retryOptions = new Dictionary<string, IRetryPolicyOptions>
            {
                { 
                    "linear", 
                    RetryPolicyOptions.Create(baseDelayMs: 5, maxRetries: 2)
                }
            };
            var api = Helper.GetApi(retryOptions, _queueOptions);

            _queueFactory = api.QFactory;
        }

        [Test]
        public void CreateParallel_FromNamedOptions_UsesRetryPolicyAndConfiguration()
        {
            var loggerFactory = Helper.GetLogger<IParallelQ>();
            var queue = _queueFactory.Parallel("parallel", Helper.GetLogger<IParallelQ>());

            Assert.That(queue.Name, Is.EqualTo("parallel"));
            Assert.That(queue.MaxParallelism, Is.EqualTo(_queueOptions["parallel"].MaxParallelism));

            var retryPolicy = queue.RetryPolicyFactory();
            Assert.That(retryPolicy, Is.InstanceOf<ILinearBackoff>());
            Assert.That(((ILinearBackoff)retryPolicy).MaxRetries, Is.EqualTo(2));

            queue.Dispose();
        }

        [Test]
        public async Task GetDispatcher_StrictFifoFromConfiguration_ExecutesSequentially()
        {
            using var queue = _queueFactory.GetCoreQ<IFifoQ>("fifo",Helper.GetLogger<IFifoQ>());
            var results = new List<int>();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            queue
                .Enqueue(ct =>
                {
                    results.Add(1);
                    return Task.CompletedTask;
                }, CancellationToken.None)
                .Enqueue(async ct =>
                {
                    await Task.Delay(10, ct);
                    results.Add(2);
                    completion.TrySetResult();
                }, CancellationToken.None);

            queue.ResumePolling();

            Assert.IsTrue(await WaitForCompletion(completion.Task), "Dispatcher did not complete work on time.");
            CollectionAssert.AreEqual(new[] { 1, 2 }, results);
        }

        private static async Task<bool> WaitForCompletion(Task task, int timeoutMs = 1000)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeoutMs)) == task;
            return completed && task.IsCompleted;
        }
    }
}


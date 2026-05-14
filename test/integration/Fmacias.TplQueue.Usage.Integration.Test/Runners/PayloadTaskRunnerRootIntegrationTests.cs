using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core.Jobs;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.Integration.Test.Cache;
using Microsoft.Extensions.Logging;

namespace Fmacias.TplQueue.Integration.Test.Runners
{
    [TestFixture]
    public class PayloadJobRootIntegrationTests
    {
        private IParallelQ _queue = null!;
        private ILogger<IParallelQ> _logger = null!;
        private IDataJobFactory _dataJobFactory = null!;
        private IQFactory _queueFactory = null!;
        private IRetryPolicyAbstractFactory _retrypolicyGenericFactory = null!;
        private IUniversalDataSerializer _serializer = null!;
        private IPayloadHandlers _handlerResolver = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>
            {
                { "none", RetryPolicyOptions.Create(0, 0) }
            };
            var api = Helper.GetApi(retryPolicyOptions, new Dictionary<string, IQOptions>());
            _logger = Helper.GetLogger<IParallelQ>();
            _retrypolicyGenericFactory = api.RetryPolicyAbstractFactory;
            _queueFactory = api.QFactory;
            _serializer = Helper.CreatePayloadSerializer();
            _handlerResolver = Helper.CreateHandlerResolver(
                new Helper.HandlerRegistration(
                    new DummyPayload(new List<string>(), "dummy-maker"),
                    DelegatePayloadHandler.Create((payload, ct) =>
                    {
                        var typed = (DummyPayload)payload;
                        typed.Execution.Add(typed.Marker);
                        return Task.CompletedTask;
                    })
                ),
                new Helper.HandlerRegistration(
                    new RecordingPayload("recording"),
                    DelegatePayloadHandler.Create((payload, ct) => {
                        return Task.CompletedTask;
                    })
                ));
            _dataJobFactory = api.DataJobFactory;
        }

        [SetUp]
        public void SetUp()
        {
            _queue = _queueFactory.Parallel(
                Guid.NewGuid(), 
                "payload-root-dispatcher",
                maxParallelism: 2,
                logger: _logger,
                retryPolicyFactory: () => _retrypolicyGenericFactory.GetPolicy<NoRetryPolicy>());
        }

        [TearDown]
        public void TearDown()
        {
            _queue.Dispose();
        }

        [Test]
        public async Task EnqueuePayloadRoot_WithDependency_ExecutesInOrder()
        {
            var execution = new List<string>();
            var rootPayload = new DummyPayload(execution, "root");
            var childPayload = new DummyPayload(execution, "child");

            var child = _dataJobFactory.DataJobRoot(
                childPayload,
                _handlerResolver.Handler(childPayload.PayloadId),
                name: "child-job",
                retryPolicy: () => NoRetryPolicy.Create());
            var root = _dataJobFactory.DataJobRoot(
                rootPayload,
                _handlerResolver.Handler(rootPayload.PayloadId),
                name: "root-node",
                retryPolicy: () => NoRetryPolicy.Create());
            root.After(child);

            _queue.Enqueue(root, CancellationToken.None);
            _queue.ResumePolling();

            await root.WaitUntilFinishedAsync();
            CollectionAssert.AreEqual(new[] { "child", "root" }, execution);
        }

        [Test]
        public async Task EnqueuePayloadRoot_UsesRootRetryPolicy()
        {
            int dispatcherCalls = 0;
            int rootCalls = 0;

            Func<IRetryPolicy> dispatcherPolicy = () => new CountingRetryPolicy(() => dispatcherCalls++);
            Func<IRetryPolicy> rootPolicy = () => new CountingRetryPolicy(() => rootCalls++);

            _queue.Dispose();
            _queue = _queueFactory.Parallel(
                Guid.NewGuid(), 
                "payload-root-dispatcher-policy",
                maxParallelism: 1,
logger: _logger, retryPolicyFactory: dispatcherPolicy);

            var payload = new DummyPayload(new List<string>(), "root");
            var root = _dataJobFactory.DataJobRoot(
                payload,
                _handlerResolver.Handler(payload.PayloadId),
                "payload-root",
                rootPolicy);

            _queue.Enqueue(root, CancellationToken.None);
            _queue.ResumePolling();

            await root.WaitUntilFinishedAsync();
            Assert.That(rootCalls, Is.GreaterThanOrEqualTo(1), "Root retry policy should be invoked.");
            Assert.That(dispatcherCalls, Is.EqualTo(0), "Dispatcher policy must not override root policy.");
        }

        [Test]
        public async Task JobRootDto_UsesProvidedRetryPolicyFactory()
        {
            var serializer = _serializer;    
            int calls = 0;
            Func<IRetryPolicy> retryFactory = () => new CountingRetryPolicy(() => calls++);
            var payload = new RecordingPayload("payload-run");
            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>
            {
                { "none", RetryPolicyOptions.Create(0, 0) }
            };
            var api = Helper.GetApi(retryPolicyOptions, new Dictionary<string, IQOptions>());
            var dataJobFactory = api.DataJobFactory;
            var rootPayloadJob = dataJobFactory.DataJobRoot(
                payload,
                _handlerResolver.Handler(payload.PayloadId),
                "rootJob",
                retryFactory);
            var policy = rootPayloadJob.GetRetryPolicyFactory()();
            await policy.ExecuteAsync(ct => Task.FromResult(true), CancellationToken.None);

            Assert.That(policy, Is.InstanceOf<CountingRetryPolicy>());
            Assert.That(calls, Is.EqualTo(1));
        }

        private sealed class DummyPayload : IPayload
        {
            public DummyPayload(IList<string> execution, string marker)
            {
                Execution = execution;
                Marker = marker;
            }

            public string PayloadId => "dummy-handler";
            public IList<string> Execution { get; }
            public string Marker { get; }

            public DateTime CollectionTime => DateTime.UtcNow;
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

            public Func<IRetryPolicy> FromDescriptor(IRetryPolicyOptions descriptor)
            {
                throw new NotImplementedException();
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



using Microsoft.Extensions.Logging;
using Fmacias.TplQueue.Core.Factories;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.Core.Jobs;

namespace Fmacias.TplQueue.Integration.Test.Runners
{
    [TestFixture]
    public class PayloadJobIntegrationTests
    {
        private IParallelQ _dispatcher = null!;
        private ILogger<IParallelQ> _logger = null!;
        private IDataJobFactory _dataJobFactory = null!;
        private IQFactoryAdapter _queueFactory = null!;
        private IRetryPolicyAbstractFactory _retryPolicyFactory = null!;
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
            _retryPolicyFactory = api.RetryPolicyAbstractFactory;
            var qOptions = new Dictionary<string, IQOptions>();
            _queueFactory = api.QFactory;
            _serializer = Helper.CreatePayloadSerializer();
            _handlerResolver = Helper.CreateHandlerResolver(
                new Helper.HandlerRegistration(
                    new DummyPayload(new List<string>(), "payload-Dummy-handler"),
                    DelegatePayloadHandler.Create((payloadObject, ct) =>
                    {
                        var typed = (DummyPayload)payloadObject;
                        typed.Execution.Add(typed.Marker);
                        return Task.CompletedTask;
                    })));
            _dataJobFactory = api.DataJobFactory;
        }   

        [SetUp]
        public void SetUp()
        {
            _dispatcher = _queueFactory.Parallel(
                Guid.NewGuid(), 
                "payload-runner-dispatcher",
                maxParallelism: 2,
                logger: _logger,
                retryPolicyFactory: () => _retryPolicyFactory.GetPolicy<NoRetryPolicy>());
        }

        [TearDown]
        public void TearDown()
        {
            _dispatcher.Dispose();
        }

        [Test]
        public async Task PayloadRunner_WithRootDependency_ExecutesPayloadBeforeRoot()
        {
            var execution = new List<string>();
            var payload = new DummyPayload(execution, "payload");
            var child = _dataJobFactory.DataJobRoot(
                payload,
                _handlerResolver.Handler(payload.PayloadId),
                name: "payload-node",
                retryPolicy: () => NoRetryPolicy.Create());

            var retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>();
            var api = Helper.GetApi(retryPolicyOptions, new Dictionary<string, IQOptions>());
            var jobFactory = api.JobFactory;
            var jobRootFactory = api.JobFactory;

            var root = jobRootFactory.JobRoot(ct =>
            {
                execution.Add("root");
                return Task.CompletedTask;
            }, name: "root");

            root.After(child);

            _dispatcher.Enqueue(root, CancellationToken.None);
            _dispatcher.ResumePolling();

            await root.WaitUntilFinishedAsync();

            CollectionAssert.AreEqual(new[] { "payload", "root" }, execution);
        }

        [Test]
        public async Task PayloadRunnerRoot_UsesProvidedRetryPolicy()
        {
            int dispatcherCalls = 0;
            int rootCalls = 0;

            Func<IRetryPolicy> dispatcherPolicy = () => new CountingRetryPolicy(() => dispatcherCalls++);
            Func<IRetryPolicy> rootPolicy = () => new CountingRetryPolicy(() => rootCalls++);

            _dispatcher.Dispose();
            _dispatcher = _queueFactory.Parallel(
                Guid.NewGuid(), 
                "payload-root-dispatcher",
                maxParallelism: 1,
logger: _logger, retryPolicyFactory: dispatcherPolicy);

            var payload = new DummyPayload(new List<string>(), "root-payload");
            var root = _dataJobFactory.DataJobRoot(
                payload,
                _handlerResolver.Handler(payload.PayloadId),
                "payload-root",
                rootPolicy);

            _dispatcher.Enqueue(root, CancellationToken.None);
            _dispatcher.ResumePolling();

            await root.WaitUntilFinishedAsync();
            Assert.That(rootCalls, Is.GreaterThanOrEqualTo(1), "Root retry policy should be invoked.");
            Assert.That(dispatcherCalls, Is.EqualTo(0), "Dispatcher policy should not override root policy.");
        }

        private sealed class DummyPayload : IPayload
        {
            public DummyPayload(IList<string> execution, string marker)
            {
                Execution = execution;
                Marker = marker;
            }

            public string PayloadId => "payload-handler";
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



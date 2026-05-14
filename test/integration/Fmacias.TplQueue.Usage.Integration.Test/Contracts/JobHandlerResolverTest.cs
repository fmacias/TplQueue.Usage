using Fmacias.TplQueue.Cache.MemCache;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Defaults;
using Fmacias.TplQueue.Integration.Test.PayloadJobs;
using static Fmacias.TplQueue.Integration.Test.ApiTests;
using static Fmacias.TplQueue.Integration.Test.Cache.CacheFactoryTests;

namespace Fmacias.TplQueue.Integration.Test.Contracts
{
    [TestFixture]
    internal class JobHandlerResolverTest
    {
        private IApi _api = null!;
        private Dictionary<string, IRetryPolicyOptions> _retryPolicyOptions = null!;
        private IReadOnlyDictionary<string, IQOptions> _queueOptions = null!;
        private ISystemTextJsonSerializerFactory _serializerFactory = null!;
        private IQFactoryAdapter _coreQFactories = null!;
        [SetUp]
        public void SetUp()
        {
            _retryPolicyOptions = new Dictionary<string, IRetryPolicyOptions>
            {
                { "no-retry", RetryPolicyOptions.Create(0, 0) }
            };
            _queueOptions = new Dictionary<string, IQOptions>()
            {
                { "main", new IntegrationDispatcherOptions(Guid.NewGuid(), maxParallelism: 2, pulseMs: 5, retryPolicy: "no-retry") }
            };
            _api = Helper.GetApi(_retryPolicyOptions, _queueOptions);
            _serializerFactory = _api.SystemTexSerializerFactory();
            _coreQFactories = _api.QFactory;
        }

        [TearDown]
        public void TearDown()
        {
        }

        [Test]
        public async Task EnqueueDelegateWithStructDtoTest()
        {
            var mesDto = new CelsiusStructPayload();
            bool executed = false;
            using var queue = _coreQFactories.Parallel(
                Guid.NewGuid(), 
                "testQueue",
                4,
                Helper.GetLogger<IParallelQ>(),
                () => NoRetryPolicy.Create());
            queue.Enqueue<CelsiusStructPayload>(
                (ct, measurementDto) =>
                {
                    executed = true;
                },
                mesDto,
                CancellationToken.None);
            queue.ResumePolling();
            await queue.Wait();
            Assert.AreEqual(0, mesDto.Temperature, "Payload input is immutable.");
            Assert.AreEqual(true, executed);
        }
        [Test]
        public async Task EnqueueDelegateWithClassDtoTest()
        {
            var measurementDto = new CelsiusClassPayload();
            var executed = false;
            using var queue = _coreQFactories.Parallel(
                Guid.NewGuid(), 
                "testQueue",
                4,
                Helper.GetLogger<IParallelQ>(),
                () => NoRetryPolicy.Create());
            queue.Enqueue<CelsiusClassPayload>(
                (ct, measurementDto) =>
                {
                    executed = true;
                },
                measurementDto,
                CancellationToken.None);
            queue.ResumePolling();
            await queue.Wait();
            Assert.AreEqual(0, measurementDto.TemperatureCelsius, "Payload input is immutable.");
            Assert.That(executed, Is.True);
        }

        [Test]
        public async Task EnqueueJobWithStructDtoTest()
        {
            var measurementDto = new CelsiusStructPayload();
            bool executed = false;
            var handlerId = measurementDto.PayloadId;
            using var queue = _coreQFactories.Parallel(
                Guid.NewGuid(), 
                "testQueue",
                4,
                Helper.GetLogger<IParallelQ>(),
                () => NoRetryPolicy.Create());
            var jobRootFactory = _api.JobFactory;
            var rootJob = jobRootFactory.JobRoot<CelsiusStructPayload>((ct, dto) => {
                executed = true;
            }, measurementDto, () => NoRetryPolicy.Create(), "RootJob");

            rootJob.Enqueue(queue, CancellationToken.None);
            queue.ResumePolling();
            await rootJob.WaitUntilFinishedAsync();
            Console.Write(measurementDto.PayloadId);
            Assert.AreEqual(0, measurementDto.Temperature, "Inmutable");
            Assert.AreEqual(handlerId, measurementDto.PayloadId);
            Assert.AreEqual(true, executed);
        }
        [Test]
        public async Task EnqueueJobWithClassDtoTest()
        {
            var measurementDto = new CelsiusClassPayload();
            var handlerId = measurementDto.PayloadId;
            using var queue = _coreQFactories.Parallel(
                Guid.NewGuid(), 
                "testQueue",
                4,
                Helper.GetLogger<IParallelQ>(),
                () => NoRetryPolicy.Create());
            var jobRootFactory = _api.JobFactory;
            var rootJob = jobRootFactory.JobRoot<CelsiusClassPayload>((ct, dto) => { },
                measurementDto, () => NoRetryPolicy.Create(), "RootJob");
            rootJob.Enqueue(queue, CancellationToken.None);
            queue.ResumePolling();
            await rootJob.WaitUntilFinishedAsync();
            Assert.AreEqual(0, measurementDto.TemperatureCelsius);
            Assert.AreEqual(handlerId, measurementDto.PayloadId);
        }

        [Test]
        public async Task EnqueueStructDtoIntoCacheableQTest()
        {
            var temperaturePayloadDto = new CelsiusStructPayload
            {
                Temperature = 2
            };
            var temperatureDtoHandler = new CelsiusDtoHandler();
            var jobHandlerResolver = GetJobHandlerResolver(temperaturePayloadDto, temperatureDtoHandler);
            var api = Helper.GetApi(
                _retryPolicyOptions,
                _queueOptions,
                new Helper.HandlerRegistration(temperaturePayloadDto, temperatureDtoHandler));

            var memCache = api.Cache<IMemCache>(
                MemCacheFactory.Create(),
                _serializerFactory.Serializer(),
                new TestTypeResolver());
            
            var factory = api.DataJobFactory;

            var measurementPayloadJob = factory.DataJobRoot(
                temperaturePayloadDto,
                jobHandlerResolver.Handler(temperaturePayloadDto.PayloadId),
                retryPolicy: () => NoRetryPolicy.Create());
           
            using var queue = api.QFactory.CacheQ(
                Helper.GetLogger<ICacheQ>(),
                memCache,
                _coreQFactories.Parallel("main", Helper.GetLogger<IParallelQ>())
            );
            queue.Enqueue<CelsiusStructPayload>(measurementPayloadJob, CancellationToken.None);
            queue.ResumePolling();

            ICacheEntry? leaseEntry = null;
            for (var i = 0; i < 120; i++)
            {
                leaseEntry = memCache.GetByJobId(measurementPayloadJob.Id);
                if (leaseEntry?.Status == EntryStatus.Acknownledged)
                {
                    break;
                }

                await Task.Delay(25).ConfigureAwait(false);
            }

            Assert.NotNull(leaseEntry);
            Assert.That(leaseEntry!.Status, Is.Not.EqualTo(EntryStatus.Failed));
            Assert.That(leaseEntry.Status, Is.Not.EqualTo(EntryStatus.Canceled));

            var updatedPayload = leaseEntry.JobNodeRecordDto.Deserialize<CelsiusStructPayload>(
                _serializerFactory.Serializer()
            );
            Assert.AreEqual(2, updatedPayload.Temperature);
        }

        private IPayloadHandlers GetJobHandlerResolver(IPayload measurementDto,
            IHandler handler)
        {
            var handlerRegistration = new Helper.HandlerRegistration(measurementDto, handler);

            return Helper.CreateHandlerResolver(handlerRegistration);
        }
    }
}

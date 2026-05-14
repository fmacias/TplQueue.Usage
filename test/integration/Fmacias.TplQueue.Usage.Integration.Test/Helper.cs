using Fmacias.TplQueue;
using Fmacias.TplQueue.Contracts;
using Fmacias.TplQueue.Core;
using Fmacias.TplQueue.RetryPolicies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fmacias.TplQueue.Integration.Test
{
    internal static class Helper
    {
        public static ILogger<T> GetLogger<T>()
        {
            return NullLogger<T>.Instance;
        }

        public static IApi GetApi(
            IReadOnlyDictionary<string, IRetryPolicyOptions> retryPolicyOptions,
            IReadOnlyDictionary<string, IQOptions> queueOptions,
            params HandlerRegistration[] registrations)
        {
            var coreApi = CoreApi.Create();
            var api = API.Create(
                coreApi,
                retryPolicyOptions,
                queueOptions);

            foreach (var registration in registrations)
            {
                api.RegisterPayloadHandler(registration.PayloadHandlerKey, registration.HandlerCallback);
            }

            return api;
        }

        public static IUniversalDataSerializer CreatePayloadSerializer()
        {
            return new TestPayloadSerializer();
        }

        public static IPayloadHandlers CreateHandlerResolver(params HandlerRegistration[] registrations)
        {
            var payloadHandlers = new TestPayloadHandlers();

            foreach (var registration in registrations)
            {
                payloadHandlers.Register(registration.PayloadHandlerKey, registration.HandlerCallback);
            }

            return payloadHandlers;
        }

        public sealed class HandlerRegistration
        {
            public HandlerRegistration(IPayload payload, IHandler payloadHandler)
            {
                if (payload == null) throw new ArgumentNullException(nameof(payload));

                PayloadType = payload.GetType();
                PayloadHandlerKey = payload.PayloadId;
                HandlerCallback = payloadHandler ?? throw new ArgumentNullException(nameof(payloadHandler));
            }

            public Type PayloadType { get; }
            public string PayloadHandlerKey { get; }
            public IHandler HandlerCallback { get; }
        }

        private sealed class TestPayloadHandlers : IPayloadHandlers
        {
            private readonly Dictionary<string, IHandler> _handlers =
                new Dictionary<string, IHandler>(StringComparer.Ordinal);

            public IHandler Handler(string payloadHandlerKey)
            {
                if (string.IsNullOrWhiteSpace(payloadHandlerKey))
                    throw new ArgumentException("Payload handler key cannot be null or empty.", nameof(payloadHandlerKey));

                if (_handlers.TryGetValue(payloadHandlerKey, out var handler))
                {
                    return handler;
                }

                throw new KeyNotFoundException(
                    $"Handler not registered for payload handler key '{payloadHandlerKey}'.");
            }

            public void Register(string payloadHandlerKey, IHandler handler)
            {
                if (string.IsNullOrWhiteSpace(payloadHandlerKey))
                    throw new ArgumentException("Payload handler key cannot be null or empty.", nameof(payloadHandlerKey));
                if (handler == null) throw new ArgumentNullException(nameof(handler));

                _handlers[payloadHandlerKey] = handler;
            }

            public void Register(string payloadHandlerKey, Func<IHandler> handlerFactory)
            {
                if (handlerFactory == null) throw new ArgumentNullException(nameof(handlerFactory));

                Register(payloadHandlerKey, new FactoryHandler(handlerFactory));
            }
        }

        private sealed class FactoryHandler : IHandler
        {
            private readonly Func<IHandler> _handlerFactory;

            public FactoryHandler(Func<IHandler> handlerFactory)
            {
                _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
            }

            public Task HandleAsync(IPayload payload, CancellationToken cancellationToken)
            {
                var handler = _handlerFactory();
                if (handler == null)
                {
                    throw new InvalidOperationException("Payload handler factory returned null.");
                }

                return handler.HandleAsync(payload, cancellationToken);
            }
        }
    }
}

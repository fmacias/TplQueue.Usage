using Fmacias.TplQueue.Contracts;

namespace Fmacias.TplQueue.Integration.Test
{
    internal static class DelegatePayloadHandler
    {
        public static IHandler Create(Func<IPayload, CancellationToken, Task> handleAsync)
        {
            return new InlinePayloadHandler(handleAsync);
        }

        private sealed class InlinePayloadHandler : IHandler
        {
            private readonly Func<IPayload, CancellationToken, Task> _handleAsync;

            public InlinePayloadHandler(Func<IPayload, CancellationToken, Task> handleAsync)
            {
                _handleAsync = handleAsync ?? throw new ArgumentNullException(nameof(handleAsync));
            }

            public Task HandleAsync(IPayload payload, CancellationToken cancellationToken)
            {
                if (payload == null) throw new ArgumentNullException(nameof(payload));

                return _handleAsync(payload, cancellationToken);
            }
        }
    }
}

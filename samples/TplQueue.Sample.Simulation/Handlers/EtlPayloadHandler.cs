using Fmacias.TplQueue.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TplQueue.Sample.Simulation.Handlers
{
    internal abstract class EtlPayloadHandler<TPayload> : IHandler
        where TPayload : class, IPayload
    {
        public Task HandleAsync(IPayload payload, CancellationToken cancellationToken)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (!(payload is TPayload typedPayload))
            {
                throw new ArgumentException(
                    $"Handler '{GetType().Name}' requires payload type '{typeof(TPayload).FullName}'.",
                    nameof(payload));
            }

            return HandleAsync(typedPayload, cancellationToken);
        }

        protected abstract Task HandleAsync(TPayload payload, CancellationToken cancellationToken);
    }
}

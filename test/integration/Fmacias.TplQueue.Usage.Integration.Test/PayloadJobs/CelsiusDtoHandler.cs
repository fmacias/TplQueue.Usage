using Fmacias.TplQueue.Contracts;

namespace Fmacias.TplQueue.Integration.Test.PayloadJobs
{
    internal sealed class CelsiusDtoHandler : IHandler
    {
        public CelsiusStructPayload StructDto { get; private set; }
        public FahrenheitDto? FahrenheitMapped { get; private set; }

        public async Task HandleAsync(IPayload payload, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                StructDto = (CelsiusStructPayload)payload;
                FahrenheitMapped = new FahrenheitDto()
                {
                    TemperatureFahrenheit = StructDto.Temperature * 33.8
                };
            }, ct);
        }
    }
}

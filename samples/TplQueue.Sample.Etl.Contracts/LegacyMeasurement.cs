using System;

namespace TplQueue.Sample.Etl.Contracts.Dto
{
    /// <summary>
    /// Consumer-owned measurement model implementing the shared ETL input 
    /// contract.
    /// </summary>
    public sealed record LegacyMeasurement
    {
        public LegacyMeasurement(
            string sensorCode,
            decimal value,
            TemperatureUnit unit,
            DateTime observedAtUtc)
        {
            SensorCode = string.IsNullOrWhiteSpace(sensorCode)
                ? throw new ArgumentException("A sensor code is required.", nameof(sensorCode))
                : sensorCode;
            Value = value;
            Unit = unit;
            ObservedAtUtc = observedAtUtc;
        }

        public string SensorCode { get; }
        public decimal Value { get; }
        public TemperatureUnit Unit { get; }
        public DateTime ObservedAtUtc { get; }
    }
}

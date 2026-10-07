using System;

namespace TplQueue.Sample.Etl.Contracts
{

    //REVIEW I think that MeasurementSnapshot should be a record type, but I am leaving it as a class for now to avoid breaking changes. If we change it to a record, we can remove the constructor and use the compiler-generated one instead.
    /// <summary>
    /// Represents an immutable measurement value persisted with an ETL payload.
    /// </summary>
    public sealed class MeasurementSnapshot
    {
        /// <summary>Initializes a persisted measurement snapshot.</summary>
        public MeasurementSnapshot(
            string sensorCode,
            decimal value,
            TemperatureUnit unit,
            DateTime observedAtUtc)
        {
            SensorCode = string.IsNullOrWhiteSpace(sensorCode)
                ? throw new ArgumentException("A sensor identifier is required.", nameof(sensorCode))
                : sensorCode;
            Value = value;
            Unit = unit;
            ObservedAtUtc = observedAtUtc;
        }

        /// <summary>Gets the source sensor identifier.</summary>
        public string SensorCode { get; }

        /// <summary>Gets the observed value.</summary>
        public decimal Value { get; }

        /// <summary>Gets the measurement unit.</summary>
        public TemperatureUnit Unit { get; }

        /// <summary>Gets the UTC observation timestamp.</summary>
        public DateTime ObservedAtUtc { get; }
    }
}

using System;

namespace TplQueue.Sample.Etl.Contracts
{
    /// <summary>Immutable bounds for one independently scheduled, finite scenario.</summary>
    public sealed class SimulationScenarioSettings
    {
        public SimulationScenarioSettings(string scenarioId, AvailableQueue queue, TimeSpan interval,
            TimeSpan startupOffset, int rootsPerTick, int repetitions, int maximumActiveRuns)
            : this(scenarioId, queue, interval, startupOffset, rootsPerTick, repetitions,
                maximumActiveRuns, SimulationScenarioKind.Etl) { }

        /// <summary>Creates finite delivery settings for the selected graph kind.</summary>
        public SimulationScenarioSettings(string scenarioId, AvailableQueue queue, TimeSpan interval,
            TimeSpan startupOffset, int rootsPerTick, int repetitions, int maximumActiveRuns,
            SimulationScenarioKind kind)
        {
            if (string.IsNullOrWhiteSpace(scenarioId)) throw new ArgumentException("A scenario ID is required.", nameof(scenarioId));
            if (!Enum.IsDefined(typeof(AvailableQueue), queue)) throw new ArgumentOutOfRangeException(nameof(queue));
            if (interval.TotalMilliseconds < 1 || interval.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(interval));
            if (startupOffset < TimeSpan.Zero || startupOffset.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(startupOffset));
            if (rootsPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(rootsPerTick));
            if (repetitions <= 0) throw new ArgumentOutOfRangeException(nameof(repetitions));
            if (maximumActiveRuns < rootsPerTick) throw new ArgumentOutOfRangeException(nameof(maximumActiveRuns));
            if (!Enum.IsDefined(typeof(SimulationScenarioKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            ScenarioId = scenarioId;
            Queue = queue;
            Interval = interval;
            StartupOffset = startupOffset;
            RootsPerTick = rootsPerTick;
            Repetitions = repetitions;
            MaximumActiveRuns = maximumActiveRuns;
            Kind = kind;
        }

        /// <summary>Gets the graph kind; the original constructor selects ETL.</summary>
        public SimulationScenarioKind Kind { get; }

        /// <summary>Gets the stable scenario name; submitted roots have unique runtime IDs.</summary>
        public string ScenarioId { get; }
        public AvailableQueue Queue { get; }
        /// <summary>Gets the recurring interval, at least one millisecond.</summary>
        public TimeSpan Interval { get; }
        /// <summary>Gets the first-tick delay. Zero schedules an asynchronous tick at timer resolution.</summary>
        public TimeSpan StartupOffset { get; }
        public int RootsPerTick { get; }
        /// <summary>Gets the finite number of ticks, including skipped and failed ticks.</summary>
        public int Repetitions { get; }
        /// <summary>Gets the per-scenario root admission bound, including queued roots.</summary>
        public int MaximumActiveRuns { get; }
    }
}

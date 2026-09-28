namespace TplQueue.Sample.Etl.Contracts
{
    /// <summary>Selects the graph submitted by a finite scenario.</summary>
    public enum SimulationScenarioKind
    {
        /// <summary>The existing Ingest, Transform and Load graph.</summary>
        Etl = 0,
        /// <summary>One independent ingest root with no composed dependencies.</summary>
        SingleJob = 1
    }
}

using Fmacias.TplQueue.Contracts;
using TplQueue.Sample.Simulation.Runtime;

namespace Fmacias.TplQueue.Integration.Test.Samples;

[TestFixture]
public sealed class SimulationGraphCatalogTests
{
    [Test]
    public void Registration_PreservesSharedMembershipAndDetachedReads()
    {
        // Arrange
        var catalog = new SimulationGraphCatalog();
        var shared = new Info();
        var first = new Info(shared);
        var second = new Info(shared);
        catalog.Register(first);
        var before = catalog.GetRootJobIds(shared.Id);

        // Act
        catalog.Register(second);
        catalog.Register(second);

        // Assert
        Assert.That(before, Is.EqualTo(new[] { first.Id }));
        Assert.That(catalog.GetRootJobIds(shared.Id), Is.EquivalentTo(new[] { first.Id, second.Id }));
        Assert.That(catalog.GetRootJobIds(first.Id), Is.EqualTo(new[] { first.Id }));
        Assert.That(catalog.GetRootJobIds(Guid.NewGuid()), Is.Empty);
        Assert.That(() => ((IList<Guid>)before).Add(Guid.NewGuid()), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Registration_RejectsNullAndTraversesRepeatedNodesOnce()
    {
        // Arrange: topology validation belongs to the graph API; the catalog must terminate even on a cycle.
        var catalog = new SimulationGraphCatalog();
        var dependencies = new List<IJobInfo>();
        var root = new Info(dependencies);
        dependencies.Add(root);

        // Act / Assert
        Assert.That(() => catalog.Register(null!), Throws.ArgumentNullException);
        catalog.Register(root);
        Assert.That(catalog.GetRootJobIds(root.Id), Is.EqualTo(new[] { root.Id }));
    }

    [Test]
    public void ConcurrentRegistrations_DoNotLoseSharedRoots()
    {
        // Arrange
        var catalog = new SimulationGraphCatalog();
        var shared = new Info();
        var roots = Enumerable.Range(0, 20).Select(_ => new Info(shared)).ToArray();

        // Act
        Parallel.ForEach(roots, root => catalog.Register(root));

        // Assert
        Assert.That(catalog.GetRootJobIds(shared.Id), Is.EquivalentTo(roots.Select(root => root.Id)));
    }

    private sealed class Info : IJobInfo
    {
        public Info(params IJobInfo[] dependencies) : this((IReadOnlyCollection<IJobInfo>)dependencies) { }
        public Info(IReadOnlyCollection<IJobInfo> dependencies) => Dependencies = dependencies;
        public Guid Id { get; } = Guid.NewGuid();
        public string Name => "composition only";
        public bool IsCompleted => false;
        public DateTime ExecutionStart => default;
        public TimeSpan ExecutionTime => default;
        public DateTime ExecutionEnd => default;
        public TaskStatus Status => TaskStatus.Created;
        public IReadOnlyCollection<IJobInfo> Dependencies { get; }
        public Guid CrossQueueId => Guid.Empty;
    }
}

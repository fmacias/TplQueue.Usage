namespace Fmacias.TplQueue.Integration.Test.Consumers
{
    /// <summary>Verifies that the assertion rejects deliberately retained references.</summary>
    [TestFixture]
    public sealed class SnapshotRetentionAssertTests
    {
        [Test]
        public void IsDetachedFrom_DirectOriginalReference_RejectsGraph()
        {
            // Arrange
            var original = new object();

            // Act / Assert
            AssertOriginalReferenceIsRejected(original, original);
        }

        [Test]
        public void IsDetachedFrom_OriginalInInheritedPrivateField_RejectsGraph()
        {
            // Arrange
            var original = new object();
            var graph = new RetainingDerived(original);

            // Act / Assert
            AssertOriginalReferenceIsRejected(graph, original);
        }

        [Test]
        public void IsDetachedFrom_OriginalInCollectionArrayAndStruct_RejectsGraph()
        {
            // Arrange
            var original = new object();
            var graph = CreateCollectionContainingReferenceInStruct(original);

            // Act / Assert
            AssertOriginalReferenceIsRejected(graph, original);
        }

        [Test]
        public void IsDetachedFrom_OriginalInCyclicGraph_RejectsGraph()
        {
            // Arrange
            var original = new object();
            var graph = CreateCycleContainingReference(original);

            // Act / Assert
            AssertOriginalReferenceIsRejected(graph, original);
        }

        private static void AssertOriginalReferenceIsRejected(object graph, object original)
        {
            var failure = Assert.Throws<AssertionException>(
                () => SnapshotRetentionAssert.IsDetachedFrom(graph, original));
            Assert.That(failure!.Message, Does.Contain("retains original reference"));
        }

        private static object CreateCollectionContainingReferenceInStruct(object original)
        {
            return new Dictionary<string, object>
            {
                ["retained"] = new[] { new ValueTuple<object>(original) }
            };
        }

        private static object CreateCycleContainingReference(object original)
        {
            var cycle = new object[2];
            cycle[0] = original;
            cycle[1] = cycle;
            return cycle;
        }

        private class RetainingBase(object retained)
        {
            private readonly object _retained = retained;
        }

        private sealed class RetainingDerived(object retained) : RetainingBase(retained);
    }
}

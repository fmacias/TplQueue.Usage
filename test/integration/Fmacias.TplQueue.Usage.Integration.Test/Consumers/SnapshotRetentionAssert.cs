using System.Reflection;
using Fmacias.TplQueue.Contracts;

namespace Fmacias.TplQueue.Integration.Test.Consumers
{
    /// <summary>Asserts that stored snapshot references cannot reach original or runtime objects.</summary>
    internal static class SnapshotRetentionAssert
    {
        /// <summary>
        /// Inspects instance storage by identity, including inherited private fields and collection backing arrays.
        /// Does not invoke property getters, non-array enumerators, or garbage collection.
        /// </summary>
        internal static void IsDetachedFrom(object snapshot, params object[] originals)
        {
            var forbidden = new HashSet<object>(originals, ReferenceEqualityComparer.Instance);
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<(object Value, string Path)>();
            pending.Push((snapshot, "snapshot"));

            while (pending.Count > 0)
            {
                var (value, path) = pending.Pop();
                AssertIsNotOriginalReference(value, path, forbidden);
                if (!visited.Add(value))
                    continue;

                AssertIsNotRuntimeObject(value, path);
                if (!IsScalarValue(value))
                    PushStoredReferences(value, path, pending);
            }
        }

        private static void AssertIsNotOriginalReference(object value, string path, HashSet<object> originals)
        {
            Assert.That(originals.Contains(value), Is.False,
                $"{path} retains original reference of type {value.GetType().FullName}.");
        }

        private static void AssertIsNotRuntimeObject(object value, string path)
        {
            Assert.That(value is IJobEvent or IJobInfo or Exception or IPayload or IHandler or Task or Delegate,
                Is.False, $"{path} retains runtime object of type {value.GetType().FullName}.");
        }

        private static bool IsScalarValue(object value)
        {
            // Other structs must be traversed because their fields can hold references.
            var type = value.GetType();
            return type.IsPrimitive || type.IsEnum || value is string or decimal or Guid or DateTime or
                DateTimeOffset or TimeSpan;
        }

        private static void PushStoredReferences(object value, string path,
            Stack<(object Value, string Path)> pending)
        {
            if (value is Array array)
                PushArrayElements(array, path, pending);
            else
                PushInstanceFields(value, path, pending);
        }

        private static void PushArrayElements(Array array, string path,
            Stack<(object Value, string Path)> pending)
        {
            var index = 0;
            foreach (var item in array)
            {
                if (item != null)
                    pending.Push((item, $"{path}[{index}]"));
                index++;
            }
        }

        private static void PushInstanceFields(object value, string path,
            Stack<(object Value, string Path)> pending)
        {
            for (var current = value.GetType(); current != null; current = current.BaseType)
            {
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var stored = field.GetValue(value);
                    if (stored != null)
                        pending.Push((stored, $"{path}.{field.Name}"));
                }
            }
        }
    }
}

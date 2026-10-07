using System;
using System.Collections.Generic;
using TplQueue.Sample.Domain.Payloads;
using TplQueue.Sample.Etl.Contracts;

namespace TplQueue.Sample.Domain.Cache
{
    /// <summary>
    /// Restricts CacheQ payload hydration to the ETL contracts supported by this sample.
    /// </summary>
    internal sealed class CacheTypeResolver : ICacheTypeResolver
    {
        private static readonly IReadOnlyList<Type> SupportedTypes = new[]
        {
            typeof(IngestMeasurementsPayload),
            typeof(TransformMeasurementsPayload),
            typeof(LoadMeasurementsPayload)
        };

        public Type Resolve(string payloadTypeName)
        {
            if (string.IsNullOrWhiteSpace(payloadTypeName))
            {
                throw new ArgumentException("A persisted payload type name is required.", nameof(payloadTypeName));
            }

            foreach (var supportedType in SupportedTypes)
            {
                if (string.Equals(payloadTypeName, supportedType.AssemblyQualifiedName, StringComparison.Ordinal) ||
                    string.Equals(payloadTypeName, supportedType.FullName, StringComparison.Ordinal))
                {
                    return supportedType;
                }
            }

            throw new InvalidOperationException(
                $"Payload type '{payloadTypeName}' is not allowed by the sample ETL workflow.");
        }
    }
}
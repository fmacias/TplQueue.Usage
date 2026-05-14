using Fmacias.TplQueue.Contracts;
using System;
using System.Text.Json;

namespace Fmacias.TplQueue.Integration.Test
{
    internal sealed class TestPayloadSerializer : IUniversalDataSerializer
    {
        private const string TypeProperty = "$type";
        private const string PayloadProperty = "$payload";

        private readonly JsonSerializerOptions _options = null!;

        public TestPayloadSerializer(JsonSerializerOptions? options = null)
        {
            _options = options ?? new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                IncludeFields = false
            };
        }

        public string Serialize(object value, Type type)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (type is null) throw new ArgumentNullException(nameof(type));

            if (!type.IsInstanceOfType(value))
            {
                throw new ArgumentException(
                    $"The provided value of type '{value.GetType().FullName}' is not an instance of '{type.FullName}'.",
                    nameof(value));
            }

            var payloadJson = JsonSerializer.Serialize(value, type, _options);

            if (typeof(IPayload).IsAssignableFrom(type))
            {
                var typeName = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
                using var doc = JsonDocument.Parse(payloadJson);
                var envelope = new PayloadEnvelope(typeName, doc.RootElement.Clone());
                return JsonSerializer.Serialize(envelope, _options);
            }

            return payloadJson;
        }

        public object Deserialize(string json, Type type)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentNullException(nameof(json));
            if (type is null) throw new ArgumentNullException(nameof(type));

            if (typeof(IPayload).IsAssignableFrom(type))
            {
                if (TryReadEnvelope(json, out var envelope))
                {
                    var payloadType = ResolveType(envelope.Type);
                    if (!type.IsAssignableFrom(payloadType))
                    {
                        throw new InvalidOperationException(
                            $"Envelope type '{payloadType.FullName}' is not assignable to '{type.FullName}'.");
                    }

                    var payload = JsonSerializer.Deserialize(envelope.Payload.GetRawText(), payloadType, _options);
                    if (payload is null)
                    {
                        throw new InvalidOperationException(
                            $"Deserialization produced null for target type '{payloadType.FullName}'.");
                    }
                    return payload;
                }

                if (type == typeof(IPayload))
                {
                    throw new NotSupportedException("Payload JSON is missing type metadata.");
                }
            }

            var result = JsonSerializer.Deserialize(json, type, _options);
            if (result is null)
            {
                throw new InvalidOperationException(
                    $"Deserialization produced null for target type '{type.FullName}'.");
            }
            return result;
        }

        public string Serialize<T>(T value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            return Serialize(value, value.GetType());
        }

        public T Deserialize<T>(string json)
        {
            return (T)Deserialize(json, typeof(T));
        }

        public string Serialize(IDataJobNode carrier)
        {
            if (carrier is null) throw new ArgumentNullException(nameof(carrier));
            var payload = carrier.GetPayload() ?? throw new InvalidOperationException("Carrier payload is null.");
            var type = carrier.PayloadType ?? payload.GetType();
            return Serialize(payload, type);
        }

        private static bool TryReadEnvelope(string json, out PayloadEnvelope envelope)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                envelope = default;
                return false;
            }

            if (!doc.RootElement.TryGetProperty(TypeProperty, out var typeProp) ||
                !doc.RootElement.TryGetProperty(PayloadProperty, out var payloadProp))
            {
                envelope = default;
                return false;
            }

            envelope = new PayloadEnvelope(typeProp.GetString() ?? string.Empty, payloadProp.Clone());
            return !string.IsNullOrWhiteSpace(envelope.Type);
        }

        private static Type ResolveType(string typeName)
        {
            var type = Type.GetType(typeName, throwOnError: false);
            if (type is null)
            {
                throw new InvalidOperationException($"Unable to resolve payload type '{typeName}'.");
            }
            return type;
        }

        private sealed class PayloadEnvelope
        {
            public PayloadEnvelope(string type, JsonElement payload)
            {
                Type = type;
                Payload = payload;
            }

            [System.Text.Json.Serialization.JsonPropertyName(TypeProperty)]
            public string Type { get; }

            [System.Text.Json.Serialization.JsonPropertyName(PayloadProperty)]
            public JsonElement Payload { get; }
        }
    }
}

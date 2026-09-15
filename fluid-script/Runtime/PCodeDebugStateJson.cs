using System.Runtime.CompilerServices;
using System.Text.Json;
using FluidScript.Parsing;

namespace FluidScript.Runtime;

/// <summary>Portable JSON transport for detached debug state without registered host objects.</summary>
public static class PCodeDebugStateJson
{
    public static string Serialize(PCodeDebugState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var encoder = new ValueEncoder();
        return JsonSerializer.Serialize(new StateDocument
        {
            PCodeHash = Convert.ToBase64String(state.PCodeHash.Span),
            Line = state.Line,
            InstructionsExecuted = state.InstructionsExecuted,
            Globals = state.Globals.Select(encoder.Encode).ToList(),
            OperandStack = state.OperandStack.Select(encoder.Encode).ToList(),
            Frames = state.Frames.Select(frame => new FrameDocument
            {
                FunctionId = frame.FunctionId,
                InstructionPointer = frame.InstructionPointer,
                StackBase = frame.StackBase,
                Locals = frame.Locals.Select(cell => encoder.Encode(FluidValue.FromCell(cell))).ToList(),
                Captures = frame.Captures.Select(cell => encoder.Encode(FluidValue.FromCell(cell))).ToList(),
                Handlers = frame.Handlers.Select(handler => new HandlerDocument
                {
                    Target = handler.Target,
                    StackDepth = handler.StackDepth,
                    FilterKind = handler.FilterKind
                }).ToList(),
                CurrentSpan = SpanDocument.From(frame.CurrentSpan),
                PendingFault = frame.PendingFault is null ? null : FaultDocument.From(frame.PendingFault, encoder)
            }).ToList(),
            Nodes = encoder.Nodes
        });
    }

    public static PCodeDebugState Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var document = JsonSerializer.Deserialize<StateDocument>(json) ?? throw new InvalidDataException("The debug state is empty.");
        if (string.IsNullOrWhiteSpace(document.PCodeHash))
            throw new InvalidDataException("The debug state has no P-code hash.");
        var decoder = new ValueDecoder(document.Nodes ?? []);
        var frames = (document.Frames ?? []).Select(frame => new PCodeDebugFrame(
            frame.FunctionId,
            frame.InstructionPointer,
            frame.StackBase,
            frame.Locals.Select(decoder.DecodeCell).ToArray(),
            frame.Captures.Select(decoder.DecodeCell).ToArray(),
            frame.Handlers.Select(handler => new PCodeDebugExceptionHandler(handler.Target, handler.StackDepth, handler.FilterKind)).ToArray(),
            frame.PendingFault?.ToFault(decoder),
            frame.CurrentSpan.ToSpan())).ToArray();
        return new PCodeDebugState(
            Convert.FromBase64String(document.PCodeHash),
            document.Line,
            frames,
            document.OperandStack.Select(decoder.Decode).ToArray(),
            document.Globals.Select(decoder.Decode).ToArray(),
            document.InstructionsExecuted);
    }

    private sealed class ValueEncoder
    {
        private readonly Dictionary<object, int> identities = new(ReferenceComparer.Instance);
        public List<ValueDocument> Nodes { get; } = [];

        public ValueDocument Encode(FluidValue value) => value.Kind switch
        {
            FluidValueKind.Null => new() { Kind = "null" },
            FluidValueKind.Bool => new() { Kind = "bool", Bool = value.AsBool() },
            FluidValueKind.Int => new() { Kind = "int", Int = value.AsInt() },
            FluidValueKind.Decimal => new() { Kind = "decimal", Decimal = value.AsDecimal() },
            FluidValueKind.String => new() { Kind = "string", String = value.AsString() },
            FluidValueKind.DateTime => new() { Kind = "dateTime", String = ((DateTimeOffset)value.Raw!).ToString("O") },
            FluidValueKind.Guid => new() { Kind = "guid", String = ((Guid)value.Raw!).ToString("D") },
            FluidValueKind.Byte => new() { Kind = "byte", Int = (byte)value.Raw! },
            FluidValueKind.Array or FluidValueKind.Dictionary or FluidValueKind.Object or FluidValueKind.Cell => EncodeReference(value),
            FluidValueKind.Function => EncodeFunction((FunctionHandle)value.Raw!),
            FluidValueKind.HostObject => throw new InvalidDataException("Registered host objects cannot be transported in a debug checkpoint."),
            _ => throw new InvalidDataException($"Value kind {value.Kind} cannot be transported in a debug checkpoint.")
        };

        private ValueDocument EncodeReference(FluidValue value)
        {
            var identity = value.Raw!;
            if (identities.TryGetValue(identity, out var existing))
                return new ValueDocument { Ref = existing };
            var id = Nodes.Count;
            identities.Add(identity, id);
            Nodes.Add(null!);
            var node = value.Kind switch
            {
                FluidValueKind.Array => new ValueDocument { Kind = "array", Items = value.AsArray().Select(Encode).ToList() },
                FluidValueKind.Dictionary => new ValueDocument
                {
                    Kind = "dictionary",
                    Entries = value.AsDictionary().Entries.ToDictionary(entry => entry.Key, entry => Encode(entry.Value), StringComparer.Ordinal)
                },
                FluidValueKind.Object => new ValueDocument
                {
                    Kind = "object",
                    TypeName = value.AsObject().TypeName,
                    ConstantFields = value.AsObject().ConstantFields.OrderBy(field => field, StringComparer.Ordinal).ToList(),
                    Entries = value.AsObject().Fields.ToDictionary(entry => entry.Key, entry => Encode(entry.Value), StringComparer.Ordinal)
                },
                FluidValueKind.Cell => new ValueDocument { Kind = "cell", Value = Encode(value.AsCell().Value) },
                _ => throw new InvalidDataException("Unsupported debug checkpoint reference.")
            };
            Nodes[id] = node;
            return new ValueDocument { Ref = id };
        }

        private ValueDocument EncodeFunction(FunctionHandle handle) => new()
        {
            Kind = "function",
            FunctionId = handle.FunctionId,
            Captures = (handle.Captures ?? []).Select(cell => Encode(FluidValue.FromCell(cell))).ToList()
        };
    }

    private sealed class ValueDecoder
    {
        private readonly IReadOnlyList<ValueDocument> documents;
        private readonly FluidValue?[] values;
        private readonly List<FluidValue>?[] arrays;

        public ValueDecoder(IReadOnlyList<ValueDocument> documents)
        {
            this.documents = documents;
            values = new FluidValue?[documents.Count];
            arrays = new List<FluidValue>?[documents.Count];
            for (var index = 0; index < documents.Count; index++)
            {
                var node = documents[index] ?? throw new InvalidDataException("The debug state contains an empty value node.");
                switch (node.Kind)
                {
                    case "array":
                        arrays[index] = [];
                        values[index] = FluidValue.FromArray(arrays[index]!);
                        break;
                    case "dictionary": values[index] = FluidValue.FromDictionary(new FluidDictionary()); break;
                    case "object": values[index] = FluidValue.FromObject(new FluidObject(node.TypeName ?? string.Empty, new Dictionary<string, FluidValue>(), new HashSet<string>(node.ConstantFields ?? [], StringComparer.Ordinal))); break;
                    case "cell": values[index] = FluidValue.FromCell(new FluidCell(FluidValue.Null)); break;
                    case "function": break;
                    default: throw new InvalidDataException("The debug state contains an unsupported value node.");
                }
            }
            for (var index = 0; index < documents.Count; index++)
            {
                var node = documents[index];
                if (node.Kind != "function")
                    continue;
                values[index] = FluidValue.FromFunction(node.FunctionId, (node.Captures ?? []).Select(DecodeCell).ToArray());
            }
            for (var index = 0; index < documents.Count; index++)
            {
                var node = documents[index];
                switch (node.Kind)
                {
                    case "array": arrays[index]!.AddRange((node.Items ?? []).Select(Decode)); break;
                    case "dictionary":
                        foreach (var entry in node.Entries ?? []) values[index]!.Value.AsDictionary().Entries.Add(entry.Key, Decode(entry.Value));
                        break;
                    case "object":
                        foreach (var entry in node.Entries ?? []) values[index]!.Value.AsObject().Fields.Add(entry.Key, Decode(entry.Value));
                        break;
                    case "cell": values[index]!.Value.AsCell().Value = Decode(node.Value); break;
                }
            }
        }

        public FluidValue Decode(ValueDocument? document)
        {
            if (document is null)
                throw new InvalidDataException("The debug state contains a missing value.");
            if (document.Ref is int reference)
            {
                if (reference < 0 || reference >= values.Length || values[reference] is not { } value)
                    throw new InvalidDataException("The debug state contains an invalid value reference.");
                return value;
            }
            return document.Kind switch
            {
                "null" => FluidValue.Null,
                "bool" => FluidValue.From(document.Bool ?? throw new InvalidDataException("A boolean value is missing.")),
                "int" => FluidValue.From(document.Int ?? throw new InvalidDataException("An integer value is missing.")),
                "decimal" => FluidValue.From(document.Decimal ?? throw new InvalidDataException("A decimal value is missing.")),
                "string" => FluidValue.From(document.String ?? string.Empty),
                "dateTime" => FluidValue.From(DateTimeOffset.Parse(document.String ?? throw new InvalidDataException("A date value is missing."), null, System.Globalization.DateTimeStyles.RoundtripKind)),
                "guid" => FluidValue.From(Guid.Parse(document.String ?? throw new InvalidDataException("A GUID value is missing."))),
                "byte" when document.Int is >= byte.MinValue and <= byte.MaxValue => FluidValue.From((byte)document.Int.Value),
                "function" => FluidValue.FromFunction(document.FunctionId, (document.Captures ?? []).Select(DecodeCell).ToArray()),
                _ => throw new InvalidDataException("The debug state contains an unsupported scalar value.")
            };
        }

        public FluidCell DecodeCell(ValueDocument? document)
        {
            var value = Decode(document);
            return value.Kind == FluidValueKind.Cell
                ? value.AsCell()
                : throw new InvalidDataException("A frame local or capture must reference a cell.");
        }
    }

    private sealed class StateDocument
    {
        public string? PCodeHash { get; set; }
        public int Line { get; set; }
        public int InstructionsExecuted { get; set; }
        public List<FrameDocument> Frames { get; set; } = [];
        public List<ValueDocument> OperandStack { get; set; } = [];
        public List<ValueDocument> Globals { get; set; } = [];
        public List<ValueDocument> Nodes { get; set; } = [];
    }

    private sealed class FrameDocument
    {
        public int FunctionId { get; set; }
        public int InstructionPointer { get; set; }
        public int StackBase { get; set; }
        public List<ValueDocument> Locals { get; set; } = [];
        public List<ValueDocument> Captures { get; set; } = [];
        public List<HandlerDocument> Handlers { get; set; } = [];
        public SpanDocument CurrentSpan { get; set; } = new();
        public FaultDocument? PendingFault { get; set; }
    }

    private sealed class HandlerDocument { public int Target { get; set; } public int StackDepth { get; set; } public int FilterKind { get; set; } }
    private sealed class SpanDocument
    {
        public int Line { get; set; }
        public int Column { get; set; }
        public int Length { get; set; }
        public static SpanDocument From(SourceSpan span) => new() { Line = span.Line, Column = span.Column, Length = span.Length };
        public SourceSpan ToSpan() => new(Line, Column, Length);
    }
    private sealed class FaultDocument
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public SpanDocument Span { get; set; } = new();
        public string? FunctionName { get; set; }
        public ValueDocument? Value { get; set; }
        public static FaultDocument From(RuntimeFaultException fault, ValueEncoder encoder) => new()
        {
            Code = fault.Code, Message = fault.Message, Span = SpanDocument.From(fault.Span), FunctionName = fault.FunctionName,
            Value = fault.Value is { } value ? encoder.Encode(value) : null
        };
        public RuntimeFaultException ToFault(ValueDecoder decoder) => new(Code, Message, Span.ToSpan(), FunctionName, Value is null ? null : decoder.Decode(Value));
    }
    private sealed class ValueDocument
    {
        public string? Kind { get; set; }
        public int? Ref { get; set; }
        public bool? Bool { get; set; }
        public long? Int { get; set; }
        public decimal? Decimal { get; set; }
        public string? String { get; set; }
        public int FunctionId { get; set; }
        public string? TypeName { get; set; }
        public List<string>? ConstantFields { get; set; }
        public List<ValueDocument>? Items { get; set; }
        public Dictionary<string, ValueDocument>? Entries { get; set; }
        public List<ValueDocument>? Captures { get; set; }
        public ValueDocument? Value { get; set; }
    }
    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static ReferenceComparer Instance { get; } = new();
        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }
}

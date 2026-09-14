using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluidScript.Parsing;

namespace FluidScript.Runtime;

public enum PCodeDebugInfo
{
    None = 0,
    SourceSpans = 1
}

/// <summary>Deterministic versioned binary representation for P-code modules.</summary>
public static class PCodeSerializer
{
    private const uint Magic = 0x43505346; // FSPC
    private const ushort Version = 0;
    private const int SourceHashLength = 32;

    public static byte[] Serialize(PCodeModule module) => Serialize(module, PCodeDebugInfo.SourceSpans);

    public static byte[] Serialize(PCodeModule module, string sourceText) =>
        Serialize(module, PCodeDebugInfo.SourceSpans, sourceText);

    public static byte[] Serialize(PCodeModule module, PCodeDebugInfo debugInfo) => Serialize(module, debugInfo, null);

    public static byte[] Serialize(PCodeModule module, PCodeDebugInfo debugInfo, string? sourceText)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (debugInfo is not PCodeDebugInfo.None and not PCodeDebugInfo.SourceSpans)
            throw new ArgumentOutOfRangeException(nameof(debugInfo));
        var sourceHash = debugInfo == PCodeDebugInfo.SourceSpans
            ? ResolveSourceHash(module, sourceText)
            : Array.Empty<byte>();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write((byte)debugInfo);
        if (debugInfo == PCodeDebugInfo.SourceSpans)
            writer.Write(sourceHash);
        writer.Write(module.EntryFunction);
        writer.Write(module.GlobalCount);
        writer.Write(module.GlobalNames.Count);
        foreach (var name in module.GlobalNames)
            writer.Write(name);
        writer.Write(module.Types.Count);
        foreach (var type in module.Types)
        {
            writer.Write(type.Name);
            writer.Write(type.FieldNames.Count);
            foreach (var field in type.FieldNames)
                writer.Write(field);
            writer.Write(type.ConstantFields.Count);
            foreach (var field in type.ConstantFields.OrderBy(item => item, StringComparer.Ordinal))
                writer.Write(field);
        }
        writer.Write(module.Constants.Count);
        foreach (var value in module.Constants)
            WriteValue(writer, value);
        writer.Write(module.Functions.Count);
        foreach (var function in module.Functions)
        {
            writer.Write(function.Name);
            writer.Write(function.Arity);
            writer.Write(function.LocalCount);
            writer.Write(function.LocalNames.Count);
            foreach (var local in function.LocalNames.OrderBy(item => item.Key))
            {
                writer.Write(local.Key);
                writer.Write(local.Value);
            }
            writer.Write(function.CaptureNames.Count);
            foreach (var capture in function.CaptureNames)
                writer.Write(capture);
            writer.Write(function.Instructions.Count);
            foreach (var instruction in function.Instructions)
            {
                writer.Write((int)instruction.OpCode);
                writer.Write(instruction.OperandA);
                writer.Write(instruction.OperandB);
                writer.Write(instruction.OperandC);
                writer.Write(instruction.OperandD);
                if (debugInfo == PCodeDebugInfo.SourceSpans)
                {
                    writer.Write(instruction.Span.Line);
                    writer.Write(instruction.Span.Column);
                    writer.Write(instruction.Span.Length);
                }
            }
        }
        writer.Flush();
        return stream.ToArray();
    }

    public static PCodeModule Deserialize(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return DeserializeCore(bytes);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The P-code payload is truncated.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The P-code payload contains an invalid value.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("The P-code payload contains an out-of-range value.", exception);
        }
    }

    /// <summary>Deserializes debug P-code and rejects it when its source hash differs.</summary>
    public static PCodeModule Deserialize(ReadOnlySpan<byte> bytes, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        var module = Deserialize(bytes);
        if (module.SourceHash.Length != SourceHashLength)
            throw new InvalidDataException("The P-code payload does not contain debug source information.");
        if (!SourceHashMatches(module, sourceText))
            throw new InvalidDataException("The P-code source hash does not match the displayed source text.");
        return module;
    }

    public static byte[] ComputeSourceHash(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        return SHA256.HashData(Encoding.UTF8.GetBytes(sourceText));
    }

    public static bool SourceHashMatches(PCodeModule module, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(sourceText);
        return module.SourceHash.Length == SourceHashLength &&
            CryptographicOperations.FixedTimeEquals(module.SourceHash.Span, ComputeSourceHash(sourceText));
    }

    private static PCodeModule DeserializeCore(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: false);
        if (reader.ReadUInt32() != Magic)
            throw new InvalidDataException("The P-code magic value is invalid.");
        if (reader.ReadUInt16() != Version)
            throw new InvalidDataException("The P-code version is not supported.");
        var debugInfo = (PCodeDebugInfo)reader.ReadByte();
        if (debugInfo is not PCodeDebugInfo.None and not PCodeDebugInfo.SourceSpans)
            throw new InvalidDataException("The P-code debug-information flag is invalid.");
        var sourceHash = debugInfo == PCodeDebugInfo.SourceSpans
            ? reader.ReadBytes(SourceHashLength)
            : Array.Empty<byte>();
        if (debugInfo == PCodeDebugInfo.SourceSpans && sourceHash.Length != SourceHashLength)
            throw new InvalidDataException("The P-code source hash is truncated.");

        var entry = reader.ReadInt32();
        var globalCount = ReadCount(reader, "global slot");
        var globals = ReadStrings(reader, "global name");
        if (globalCount != globals.Count)
            throw new InvalidDataException("Global metadata is inconsistent.");
        var types = new List<PCodeType>();
        var typeCount = ReadCount(reader, "type");
        for (var index = 0; index < typeCount; index++)
        {
            var name = reader.ReadString();
            var fields = ReadStrings(reader, "field");
            var constants = new HashSet<string>(ReadStrings(reader, "constant field"), StringComparer.Ordinal);
            types.Add(new PCodeType(name, fields, constants));
        }
        var constantsPool = new List<FluidValue>();
        var constantCount = ReadCount(reader, "constant");
        for (var index = 0; index < constantCount; index++)
            constantsPool.Add(ReadValue(reader));
        var functions = new List<PCodeFunction>();
        var functionCount = ReadCount(reader, "function");
        for (var index = 0; index < functionCount; index++)
        {
            var name = reader.ReadString();
            var arity = reader.ReadInt32();
            var localCount = reader.ReadInt32();
            var localNames = new Dictionary<int, string>();
            var localCountMetadata = ReadCount(reader, "local name");
            for (var local = 0; local < localCountMetadata; local++)
                localNames.Add(reader.ReadInt32(), reader.ReadString());
            var captures = ReadStrings(reader, "capture");
            var instructions = new List<Instruction>();
            var instructionCount = ReadCount(reader, "instruction");
            for (var instruction = 0; instruction < instructionCount; instruction++)
            {
                var opcode = (OpCode)reader.ReadInt32();
                var a = reader.ReadInt32();
                var b = reader.ReadInt32();
                var c = reader.ReadInt32();
                var d = reader.ReadInt32();
                var span = debugInfo == PCodeDebugInfo.SourceSpans
                    ? new SourceSpan(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32())
                    : SourceSpan.None;
                instructions.Add(new Instruction(opcode, a, b, c, span, d));
            }
            functions.Add(new PCodeFunction(name, arity, localCount, instructions, localNames, captures));
        }
        var module = new PCodeModule(constantsPool, functions, entry)
        {
            GlobalCount = globalCount,
            GlobalNames = globals,
            Types = types,
            SourceHash = sourceHash
        };
        var diagnostics = PCodeVerifier.Verify(module);
        if (diagnostics.Count > 0)
            throw new InvalidDataException(string.Join("; ", diagnostics));
        if (stream.Position != stream.Length)
            throw new InvalidDataException("The P-code payload contains trailing data.");
        return module;
    }

    private static byte[] ResolveSourceHash(PCodeModule module, string? sourceText)
    {
        if (sourceText is not null)
            return ComputeSourceHash(sourceText);
        if (module.SourceHash.Length == SourceHashLength)
            return module.SourceHash.ToArray();
        throw new InvalidOperationException(
            "Debug P-code serialization requires the original source text or a compiled module source hash.");
    }

    private static void WriteValue(BinaryWriter writer, FluidValue value)
    {
        writer.Write((byte)value.Kind);
        switch (value.Kind)
        {
            case FluidValueKind.Null: break;
            case FluidValueKind.Bool: writer.Write(value.AsBool()); break;
            case FluidValueKind.Int: writer.Write(value.AsInt()); break;
            case FluidValueKind.Decimal: writer.Write(value.AsDecimal()); break;
            case FluidValueKind.String: writer.Write(value.AsString()); break;
            case FluidValueKind.DateTime:
                var date = (DateTimeOffset)value.Raw!;
                writer.Write(date.Ticks); writer.Write((short)date.Offset.TotalMinutes); break;
            case FluidValueKind.Guid: writer.Write(((Guid)value.Raw!).ToByteArray()); break;
            case FluidValueKind.Byte: writer.Write((byte)value.Raw!); break;
            case FluidValueKind.Array:
                var array = value.AsArray(); writer.Write(array.Count);
                foreach (var item in array) WriteValue(writer, item);
                break;
            case FluidValueKind.Dictionary:
                var dictionary = value.AsDictionary().Entries;
                writer.Write(dictionary.Count);
                foreach (var entry in dictionary.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    writer.Write(entry.Key);
                    WriteValue(writer, entry.Value);
                }
                break;
            default:
                throw new InvalidDataException($"Value kind {value.Kind} cannot be a constant.");
        }
    }

    private static FluidValue ReadValue(BinaryReader reader)
    {
        var kind = (FluidValueKind)reader.ReadByte();
        return kind switch
        {
            FluidValueKind.Null => FluidValue.Null,
            FluidValueKind.Bool => FluidValue.From(reader.ReadBoolean()),
            FluidValueKind.Int => FluidValue.From(reader.ReadInt64()),
            FluidValueKind.Decimal => FluidValue.From(reader.ReadDecimal()),
            FluidValueKind.String => FluidValue.From(reader.ReadString()),
            FluidValueKind.DateTime => FluidValue.From(new DateTimeOffset(reader.ReadInt64(), TimeSpan.FromMinutes(reader.ReadInt16()))),
            FluidValueKind.Guid => FluidValue.From(new Guid(reader.ReadBytes(16))),
            FluidValueKind.Byte => FluidValue.From(reader.ReadByte()),
            FluidValueKind.Array => FluidValue.FromArray(Enumerable.Range(0, ReadCount(reader, "array element")).Select(_ => ReadValue(reader)).ToArray()),
            FluidValueKind.Dictionary => ReadDictionary(reader),
            _ => throw new InvalidDataException($"Value kind {kind} is not supported.")
        };
    }

    private static FluidValue ReadDictionary(BinaryReader reader)
    {
        var entries = new Dictionary<string, FluidValue>(StringComparer.Ordinal);
        var entryCount = ReadCount(reader, "dictionary entry");
        for (var index = 0; index < entryCount; index++)
            entries.Add(reader.ReadString(), ReadValue(reader));
        return FluidValue.FromDictionary(new FluidDictionary(entries));
    }

    private static List<string> ReadStrings(BinaryReader reader, string description)
    {
        var values = new List<string>();
        var count = ReadCount(reader, description);
        for (var index = 0; index < count; index++)
            values.Add(reader.ReadString());
        return values;
    }

    private static int ReadCount(BinaryReader reader, string description)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > 1_000_000)
            throw new InvalidDataException($"The {description} count is invalid.");
        return count;
    }
}

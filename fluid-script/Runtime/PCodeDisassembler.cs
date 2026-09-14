using System.Globalization;
using System.Text;

namespace FluidScript.Runtime;

/// <summary>Produces a stable, human-readable listing of a P-code module.</summary>
public static class PCodeDisassembler
{
    public static string Disassemble(PCodeModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var output = new StringBuilder();
        output.AppendLine($"entry __function{module.EntryFunction}");
        for (var index = 0; index < module.Constants.Count; index++)
            output.AppendLine($"const {index}: {module.Constants[index]}");

        for (var functionId = 0; functionId < module.Functions.Count; functionId++)
        {
            var function = module.Functions[functionId];
            output.AppendLine($"function {functionId} {function.Name}/{function.Arity} locals={function.LocalCount}");
            for (var ip = 0; ip < function.Instructions.Count; ip++)
            {
                var instruction = function.Instructions[ip];
                output.Append("  ").Append(ip.ToString("D4", CultureInfo.InvariantCulture)).Append(" ")
                    .Append(instruction.OpCode);
                AppendOperand(output, instruction.OperandA);
                AppendOperand(output, instruction.OperandB);
                AppendOperand(output, instruction.OperandC);
                if (instruction.OpCode is OpCode.ForCheckLocal or OpCode.ForCheckGlobal)
                    AppendOperand(output, instruction.OperandD);
                if (instruction.Span != Parsing.SourceSpan.None)
                    output.Append(" ; ").Append(instruction.Span);
                output.AppendLine();
            }
        }

        return output.ToString();
    }

    private static void AppendOperand(StringBuilder output, int operand) =>
        output.Append(' ').Append(operand.ToString(CultureInfo.InvariantCulture));
}

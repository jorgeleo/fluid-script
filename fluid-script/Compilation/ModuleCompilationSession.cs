namespace FluidScript.Compilation;

internal sealed class ModuleCompilationSession
{
    private readonly HashSet<string> active = new(StringComparer.Ordinal);
    private readonly HashSet<string> completed = new(StringComparer.Ordinal);

    public bool TryEnter(string moduleName, out bool cached)
    {
        cached = completed.Contains(moduleName);
        return !cached && active.Add(moduleName);
    }

    public void Exit(string moduleName, bool success)
    {
        active.Remove(moduleName);
        if (success)
            completed.Add(moduleName);
    }
}

namespace FluidScript.Compilation;

/// <summary>Host-owned source resolver used by import statements.</summary>
public interface IFluidModuleResolver
{
    bool TryResolve(string moduleName, out string source);
}

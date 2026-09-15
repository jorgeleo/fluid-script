namespace FluidScript.Runtime;

/// <summary>Registers a group of runtime native functions on a script host.</summary>
public interface IRegister
{
    void Register(FluidScriptHost host);
}

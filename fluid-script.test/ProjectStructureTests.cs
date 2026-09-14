using FluidScript;

namespace FluidScript.Test;

[TestClass]
public sealed class ProjectStructureTests
{
    [TestMethod]
    public void Library_uses_the_expected_assembly_name()
    {
        Assert.AreEqual("FluidScript", typeof(FluidScriptAssembly).Assembly.GetName().Name);
    }
}

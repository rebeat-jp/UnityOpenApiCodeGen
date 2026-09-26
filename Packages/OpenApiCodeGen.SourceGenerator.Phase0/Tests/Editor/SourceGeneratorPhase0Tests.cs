using NUnit.Framework;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.Tests
{
    internal sealed class SourceGeneratorPhase0Tests
    {
        [Test]
        public void GeneratedCodeContainsFixedAndAdditionalFileValues()
        {
            Assert.That(GeneratedCodeProbe.Fixed, Is.EqualTo("Phase0"));
            Assert.That(GeneratedCodeProbe.Root, Is.EqualTo("root-value"));
            Assert.That(GeneratedCodeProbe.Scoped, Is.EqualTo("scoped-value"));
        }
    }
}

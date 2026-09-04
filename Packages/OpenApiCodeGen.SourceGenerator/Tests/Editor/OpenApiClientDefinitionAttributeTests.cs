using System;
using NUnit.Framework;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    internal sealed class OpenApiClientDefinitionAttributeTests
    {
        [Test]
        public void AttributeUsageTargetsOneNonInheritedClassDefinition()
        {
            object[] attributes = typeof(OpenApiClientDefinitionAttribute)
                .GetCustomAttributes(typeof(AttributeUsageAttribute), false);

            Assert.That(attributes, Has.Length.EqualTo(1));
            var usage = (AttributeUsageAttribute)attributes[0];
            Assert.That(usage.ValidOn, Is.EqualTo(AttributeTargets.Class));
            Assert.That(usage.AllowMultiple, Is.False);
            Assert.That(usage.Inherited, Is.False);
        }

        [Test]
        public void ConstructorPreservesDefinitionValues()
        {
            var attribute = new OpenApiClientDefinitionAttribute(
                "0123456789abcdef0123456789abcdef",
                "PetStore",
                "Example.Generated",
                OpenApiDocumentFormat.Json);

            Assert.That(attribute.SpecId, Is.EqualTo("0123456789abcdef0123456789abcdef"));
            Assert.That(attribute.ApiName, Is.EqualTo("PetStore"));
            Assert.That(attribute.GeneratedNamespace, Is.EqualTo("Example.Generated"));
            Assert.That(attribute.DocumentFormat, Is.EqualTo(OpenApiDocumentFormat.Json));
        }

        [Test]
        public void DocumentFormatValuesRemainCompatible()
        {
            Assert.That(typeof(OpenApiDocumentFormat).IsPublic, Is.True);
            Assert.That((int)OpenApiDocumentFormat.Json, Is.EqualTo(0));
            Assert.That((int)OpenApiDocumentFormat.Yaml, Is.EqualTo(1));
        }

        [Test]
        public void AttributeMetadataNameIsStable()
        {
            Assert.That(
                typeof(OpenApiClientDefinitionAttribute).FullName,
                Is.EqualTo(
                    "Rhycol.OpenApiCodeGen.SourceGenerator.OpenApiClientDefinitionAttribute"));
            Assert.That(typeof(OpenApiClientDefinitionAttribute).IsPublic, Is.True);
            Assert.That(typeof(OpenApiClientDefinitionAttribute).IsSealed, Is.True);
        }
    }
}

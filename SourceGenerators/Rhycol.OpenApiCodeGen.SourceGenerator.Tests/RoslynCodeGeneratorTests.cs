using Xunit;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    public class RoslynCodeGeneratorTests
    {
        [Fact]
        public void Generate_EmitsTypesAndMembers()
        {
            var file = new CSharpSyntaxGenerator("Sample.g.cs", "Example")
                .AddUsing("System")
                .AddClass("Box", builder =>
                {
                    builder.AddComment("Box summary.");
                    builder.AddModifiers("public");
                    builder.AddAttribute("System.Obsolete", "\"old\"");
                    builder.AddGenericParameter("T");
                    builder.AddBaseType("BaseType");
                    builder.AddBaseType("IDisposable");
                    builder.AddMethod("Get", method =>
                    {
                        method.AddModifiers("public");
                        method.WithReturnType("int");
                        method.AddGenericParameter("TValue");
                        method.AddConstraint("where TValue : class, new()");
                        method.AddParameter("value", "TValue");
                        method.WithBody("return 1;");
                    });
                })
                .AddStruct("MyStruct", builder =>
                {
                    builder.AddModifiers("public");
                    builder.AddMember("public int Value { get; }");
                })
                .AddInterface("IMy", builder =>
                {
                    builder.AddModifiers("public");
                    builder.AddMember("int Value { get; }");
                })
                .AddEnum("MyEnum", builder =>
                {
                    builder.AddModifiers("public");
                    builder.AddMember("A", "1");
                    builder.AddMember("B");
                })
                .Build();

            var content = new RoslynCodeGenerator().Generate(file).Content;

            Assert.Contains("class Box<T>", content);
            Assert.Contains(": BaseType, IDisposable", content);
            Assert.Contains("[System.Obsolete(\"old\")]", content);
            Assert.Contains("where TValue : class, new()", content);
            Assert.Contains("int Get<TValue>(TValue value)", content);
            Assert.Contains("return 1;", content);
            Assert.Contains("struct MyStruct", content);
            Assert.Contains("interface IMy", content);
            Assert.Contains("enum MyEnum", content);
            Assert.Contains("/// <summary>", content);
            Assert.Contains("/// Box summary.", content);
        }

        [Fact]
        public void Generate_EmitsContextualKeywordModifier()
        {
            var file = new CSharpSyntaxGenerator("PartialClient.g.cs", "Example")
                .AddClass("PartialClient", builder =>
                {
                    builder.AddModifiers("public", "partial");
                })
                .Build();

            string content = new RoslynCodeGenerator().Generate(file).Content;

            Assert.Contains("public partial class PartialClient", content);
        }
    }
}

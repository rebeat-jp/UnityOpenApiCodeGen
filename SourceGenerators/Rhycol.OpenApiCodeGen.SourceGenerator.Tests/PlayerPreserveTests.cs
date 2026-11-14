using System;
using System.Linq;
using System.Reflection;

using Xunit;

using static Rhycol.OpenApiCodeGen.SourceGenerator.Tests.SerializerMatrixHarness;

namespace Rhycol.OpenApiCodeGen.SourceGenerator.Tests
{
    /// <summary>
    /// Unity's Managed Stripping Level High removes members that are only reached through reflection. The Player gate
    /// (Issue #57) showed that Json.NET could not create the generated enum converters and that System.Text.Json
    /// could not set the generated DTO properties in an IL2CPP player. Both serializers read the generated types by
    /// reflection, so the generated DTOs and wire converters carry the internal PreserveAttribute, which the Unity
    /// linker recognizes by name and which keeps all members of the type.
    /// </summary>
    public sealed class PlayerPreserveTests
    {
        public static readonly object[][] Serializers =
        {
            new object[] { NewtonsoftJson },
            new object[] { SystemTextJson },
        };

        [Theory]
        [MemberData(nameof(Serializers))]
        public void DtosAndEnumWireConvertersCarryTheInternalPreserveAttribute(string serializer)
        {
            Assembly assembly = Core(serializer);
            Type preserve = PreserveType(assembly);
            Assert.False(preserve.IsPublic);

            foreach (string name in new[] { "Widget", "Address", "Problem", "Open" })
            {
                AssertPreserved(TypeOf(assembly, name), preserve);
            }

            foreach (string name in new[] { "ColorWireConverter", "LevelWireConverter" })
            {
                AssertPreserved(TypeOf(assembly, name), preserve);
            }
        }

        // A class-level PreserveAttribute is not enough: in the Player gate the property setters of a DTO that no code
        // sets directly (a union member, a response body) were still removed at High, and both serializers then
        // ignored or rejected the property. The accessors are kept by a PreserveAttribute on the property itself.
        [Theory]
        [MemberData(nameof(Serializers))]
        public void DtoPropertiesCarryTheInternalPreserveAttribute(string serializer)
        {
            Assembly assembly = Core(serializer);
            Type preserve = PreserveType(assembly);
            foreach (string name in new[] { "Widget", "Address", "Problem", "Open" })
            {
                PropertyInfo[] properties = TypeOf(assembly, name)
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                Assert.NotEmpty(properties);
                foreach (PropertyInfo property in properties)
                {
                    Assert.True(
                        property.GetCustomAttributes(inherit: false).Any(attribute => attribute.GetType() == preserve),
                        name + "." + property.Name + " is missing the PreserveAttribute");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Serializers))]
        public void UnionConvertersKeepTheirPreserveAttribute(string serializer)
        {
            Assembly assembly = Compile("composition.json", serializer);
            Type preserve = PreserveType(assembly);
            // The converter of a union wrapper is named by the wrapper's JsonConverter attribute.
            foreach (string name in new[] { "Pet", "Value" })
            {
                object converterAttribute = TypeOf(assembly, name).GetCustomAttributes(inherit: false)
                    .Single(static attribute => attribute.GetType().Name == "JsonConverterAttribute");
                var converter = (Type)converterAttribute.GetType().GetProperty("ConverterType")!.GetValue(converterAttribute)!;
                AssertPreserved(converter, preserve);
            }
        }

        // The attribute lives in a namespace of its own per client; Unity's linker only looks at the type name.
        private static Type PreserveType(Assembly assembly) =>
            assembly.GetTypes().Single(static type => type.Name == "PreserveAttribute");

        private static void AssertPreserved(Type type, Type preserve)
        {
            Assert.Contains(
                type.GetCustomAttributes(inherit: false),
                attribute => attribute.GetType() == preserve);
        }
    }
}

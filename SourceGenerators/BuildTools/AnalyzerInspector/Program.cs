using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

string[] forbiddenReferences =
[
    "Newtonsoft.Json",
    "System.Text.Json",
    "YamlDotNet",
];
string[] allowedReferences =
[
    "Microsoft.CodeAnalysis",
    "Microsoft.CodeAnalysis.CSharp",
    "System.Collections.Immutable",
    "netstandard",
];

if (args.Length != 3)
{
    Console.Error.WriteLine(
        "Usage: AnalyzerInspector <assembly> <expected-assembly-name> <expected-target-framework>");
    return 2;
}

string assemblyPath = Path.GetFullPath(args[0]);
string expectedAssemblyName = args[1];
string expectedTargetFramework = args[2];

using FileStream stream = File.OpenRead(assemblyPath);
using var peReader = new PEReader(stream);
if (!peReader.HasMetadata)
{
    Console.Error.WriteLine($"Not a managed assembly: {assemblyPath}");
    return 1;
}

MetadataReader reader = peReader.GetMetadataReader();
if (!reader.IsAssembly)
{
    Console.Error.WriteLine($"Managed module is not an assembly: {assemblyPath}");
    return 1;
}

AssemblyDefinition definition = reader.GetAssemblyDefinition();
string assemblyName = reader.GetString(definition.Name);
if (!string.Equals(assemblyName, expectedAssemblyName, StringComparison.Ordinal))
{
    Console.Error.WriteLine(
        $"Unexpected assembly name. Expected '{expectedAssemblyName}', found '{assemblyName}'.");
    return 1;
}

string? targetFramework = ReadTargetFramework(reader, definition);
if (!string.Equals(targetFramework, expectedTargetFramework, StringComparison.Ordinal))
{
    Console.Error.WriteLine(
        $"Unexpected target framework. Expected '{expectedTargetFramework}', " +
        $"found '{targetFramework ?? "<missing>"}'.");
    return 1;
}

var referencedAssemblies = reader.AssemblyReferences
    .Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))
    .OrderBy(name => name, StringComparer.Ordinal)
    .ToArray();

foreach (string forbiddenReference in forbiddenReferences)
{
    if (referencedAssemblies.Contains(forbiddenReference, StringComparer.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            $"Analyzer has a forbidden assembly reference: {forbiddenReference}");
        return 1;
    }
}

string[] unexpectedReferences = referencedAssemblies
    .Except(allowedReferences, StringComparer.OrdinalIgnoreCase)
    .ToArray();
if (unexpectedReferences.Length > 0)
{
    Console.Error.WriteLine(
        $"Analyzer has unexpected assembly references: {string.Join(", ", unexpectedReferences)}");
    return 1;
}

Console.WriteLine($"Assembly: {assemblyName}");
Console.WriteLine($"Target framework: {targetFramework}");
Console.WriteLine($"References: {string.Join(", ", referencedAssemblies)}");
return 0;

static string? ReadTargetFramework(MetadataReader reader, AssemblyDefinition definition)
{
    const string targetFrameworkAttribute =
        "System.Runtime.Versioning.TargetFrameworkAttribute";

    foreach (CustomAttributeHandle handle in definition.GetCustomAttributes())
    {
        CustomAttribute attribute = reader.GetCustomAttribute(handle);
        if (!string.Equals(
                GetAttributeTypeFullName(reader, attribute.Constructor),
                targetFrameworkAttribute,
                StringComparison.Ordinal))
        {
            continue;
        }

        BlobReader value = reader.GetBlobReader(attribute.Value);
        if (value.ReadUInt16() != 1)
        {
            return null;
        }

        return value.ReadSerializedString();
    }

    return null;
}

static string? GetAttributeTypeFullName(MetadataReader reader, EntityHandle constructor)
{
    EntityHandle declaringType = constructor.Kind switch
    {
        HandleKind.MemberReference => reader.GetMemberReference(
            (MemberReferenceHandle)constructor).Parent,
        HandleKind.MethodDefinition => reader.GetMethodDefinition(
            (MethodDefinitionHandle)constructor).GetDeclaringType(),
        _ => default,
    };

    return declaringType.Kind switch
    {
        HandleKind.TypeReference => GetFullName(
            reader.GetString(reader.GetTypeReference((TypeReferenceHandle)declaringType).Namespace),
            reader.GetString(reader.GetTypeReference((TypeReferenceHandle)declaringType).Name)),
        HandleKind.TypeDefinition => GetFullName(
            reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)declaringType).Namespace),
            reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)declaringType).Name)),
        _ => null,
    };
}

static string GetFullName(string @namespace, string name) =>
    string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";

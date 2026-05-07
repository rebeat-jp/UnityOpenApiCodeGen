#nullable enable


namespace Rhycol.OpenApiCodeGen.UI
{
    internal class GenerationCSharpSettingDisplayDto
    {
        public bool AllowUnicodeIdentifiers { get; set; } = false;
        public string ApiName { get; set; } = "Api";
        public bool CaseInsensitiveResponseHeaders { get; set; } = false;
        public bool ConditionalSerialization { get; set; } = false;
        public bool DisallowAdditionalPropertiesIfNotPresent { get; set; } = true;
        public bool Equatable { get; set; }
        public bool HideGenerationTimestamp { get; set; }
        public string InterfacePrefix { get; set; } = "I";
        public string Library { get; set; } = "unityWebRequest";
        public string? LicenseId { get; set; } = null;
        public string ModelPropertyNaming { get; set; } = "PascalCase";
        public bool NetCoreProjectFile { get; set; } = false;
        public bool NonPublicApi { get; set; } = false;
        public bool NullableReferenceTypes { get; set; } = true;
        public bool OptionalEmitDefaultValues { get; set; } = false;
        public bool OptionalMethodArgument { get; set; } = true;
        public bool OptionalAssemblyInfo { get; set; } = true;
        public bool OptionalProjectFile { get; set; } = false;
        public string PackageName { get; set; } = "Rhycol.OpenApiCodeGen";
        public bool ReturnICollection { get; set; } = false;
        /// <summary>
        /// The target .NET framework version. To target multiple frameworks, use ; as the separator, 
        /// e.g. [netstandard2.1;netcoreapp3.1]
        /// </summary>
        public string TargetFramework { get; set; } = "netstandard2.1";
        public bool UseCollection { get; set; } = false;
        public bool UseOneOfDiscriminatorLookup { get; set; } = false;
        public bool Validatable { get; set; } = true;

    }
}
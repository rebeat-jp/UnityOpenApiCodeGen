namespace ReBeat.OpenApiCodeGen.UI
{
    public class OpenApiSettingsDto
    {
        public string PackageName { get; private set; }
        public OpenApiTargetFramework TargetFramework { get; private set; }
        public bool IncludeNullableReferenceTypes { get; private set; }
        public bool IncludeOptionalAssemblyInfo { get; private set; }
        public bool IncludeOptionalProjectFile { get; private set; }
        public OpenApiDependenceLibrary DependenceLibrary { get; private set; }
        public bool IsValidatable { get; private set; }

        public OpenApiSettingsDto(
          string packageName,
          string targetFramework,
          bool includeNullableReferenceTypes,
          bool includeOptionalAssemblyInfo,
          bool includeOptionalProjectFile,
          string dependenceLibrary,
          bool isValidatable
    )
        {
            PackageName = packageName;
            TargetFramework = OpenApiTargetFrameworkExtend.ConvertFromString(targetFramework);
            IncludeNullableReferenceTypes = includeNullableReferenceTypes;
            IncludeOptionalAssemblyInfo = includeOptionalAssemblyInfo;
            IncludeOptionalProjectFile = includeOptionalProjectFile;
            DependenceLibrary = OpenApiDependenceLibraryExtend.ConvertFromString(dependenceLibrary);
            IsValidatable = isValidatable;
        }

    }
}
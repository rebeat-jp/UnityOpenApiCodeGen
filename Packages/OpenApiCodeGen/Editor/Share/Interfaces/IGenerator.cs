#nullable enable
using System.Threading;
using System.Threading.Tasks;


namespace Rhycol.OpenApiCodeGen.Core
{
    interface IGenerator
    {
        ProcessResponse Generate(
            ProjectSetting projectSetting,
            GenerationCSharpSetting cSharpSetting,
            UserSetting userSetting);
        Task<ProcessResponse> GenerateAsync(
            ProjectSetting projectSetting,
            GenerationCSharpSetting cSharpSetting,
            UserSetting userSetting,
            CancellationToken cancellationToken = default);
    }
}
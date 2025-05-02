using Cysharp.Threading.Tasks;

namespace ReBeat.OpenApiCodeGen.Core
{
    interface IGenerable
    {
        ProcessResponse Generate(SettingSchema settingSchema);
        UniTask<ProcessResponse> GenerateAsync(SettingSchema settingSchema);
    }
}
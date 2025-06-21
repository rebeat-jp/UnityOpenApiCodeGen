using System.Threading.Tasks;


namespace ReBeat.OpenApiCodeGen.Core
{
    interface IGenerable
    {
        ProcessResponse Generate(SettingSchema settingSchema);
        Task<ProcessResponse> GenerateAsync(SettingSchema settingSchema);
    }
}
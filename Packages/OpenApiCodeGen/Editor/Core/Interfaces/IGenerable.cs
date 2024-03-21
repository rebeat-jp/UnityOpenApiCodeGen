namespace ReBeat.OpenApiCodeGen.Core
{
    interface IGenerable
    {
        ProcessResponse Generate(string documentFilePath, string outputFolderPath);
    }
}
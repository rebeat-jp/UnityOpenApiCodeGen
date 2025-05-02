#nullable enable

namespace ReBeat.OpenApiCodeGen.Core
{
    internal interface IJsonConvertible<T> where T : class
    {
        string ToJson();
        T? FromJson(string json);
    }
}
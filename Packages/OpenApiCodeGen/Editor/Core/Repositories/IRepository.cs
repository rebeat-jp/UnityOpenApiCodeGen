#nullable enable
using System.Threading.Tasks;

namespace ReBeat.OpenApiCodeGen.Core
{
    public interface IRepository<T> where T : class
    {
        T? Read();
        void Save(T value);
        void Delete();
        Task<T?> ReadAsync();
        Task SaveAsync(T value);

    }
}
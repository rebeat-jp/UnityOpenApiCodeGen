#nullable enable
using System.Threading.Tasks;

namespace Rhycol.OpenApiCodeGen.Core
{
    public interface IAsyncRepository<T> where T : class
    {
        Task DeleteAsync();
        Task<T?> ReadAsync();
        Task SaveAsync(T value);

    }
}
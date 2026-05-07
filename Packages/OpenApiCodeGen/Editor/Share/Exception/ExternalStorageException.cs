#nullable enable

using System;

namespace Rhycol.OpenApiCodeGen.Core
{
    internal class ExternalStorageException : Exception
    {
        public ExternalStorageException(string message) : base(message)
        {
        }

        public ExternalStorageException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

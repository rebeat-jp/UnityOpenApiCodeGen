#nullable enable

using System;

namespace ReBeat.OpenApiCodeGen.Core
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

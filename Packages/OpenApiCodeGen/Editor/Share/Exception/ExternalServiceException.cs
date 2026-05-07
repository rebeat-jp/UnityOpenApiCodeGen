#nullable enable

using System;

namespace Rhycol.OpenApiCodeGen.Core
{
    internal class ExternalServiceException : Exception
    {
        public ExternalServiceException(string message) : base(message)
        {
        }

        public ExternalServiceException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

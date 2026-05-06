#nullable enable

using System;

namespace ReBeat.OpenApiCodeGen.Core
{
    public class ApplicationServiceException : Exception
    {
        public ApplicationServiceException(string message) : base(message)
        {
        }

        public ApplicationServiceException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

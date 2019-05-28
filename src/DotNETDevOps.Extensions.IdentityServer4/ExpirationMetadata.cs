using System;

namespace DotNETDevOps.Extensions.IdentityServer4
{
    internal struct ExpirationMetadata<T>
    {
        public T Result { get; set; }

        public DateTimeOffset ValidUntil { get; set; }
    }
}

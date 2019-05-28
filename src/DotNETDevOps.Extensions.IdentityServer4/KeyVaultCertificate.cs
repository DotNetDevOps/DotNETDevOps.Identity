using System.Security.Cryptography.X509Certificates;

namespace DotNETDevOps.Extensions.IdentityServer4
{
    public class KeyVaultCertificate
    {
        public string Identifier { get; set; }
        public X509Certificate2 Cert { get; set; }
    }
}

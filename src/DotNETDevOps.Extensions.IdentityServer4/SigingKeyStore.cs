using IdentityServer4.Stores;
using Microsoft.Azure.KeyVault;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace DotNETDevOps.Extensions.IdentityServer4
{
    public class SigingKeyStore : ISigningCredentialStore, IValidationKeysStore
    {
        private readonly IServiceScopeFactory serviceScopeFactory;

        private readonly AsyncExpiringLazy<KeyVaultCertificate[]> certs;

        public SigingKeyStore(IServiceScopeFactory serviceScopeFactory)
        {
            this.serviceScopeFactory = serviceScopeFactory;

            certs = new AsyncExpiringLazy<KeyVaultCertificate[]>(Factory);
        }

        private async Task<ExpirationMetadata<KeyVaultCertificate[]>> Factory(ExpirationMetadata<KeyVaultCertificate[]> arg)
        {
            using (var scope = serviceScopeFactory.CreateScope())
            {
                var old = (arg.Result ?? Array.Empty<KeyVaultCertificate>()).ToLookup(k => k.Identifier);
                var options = scope.ServiceProvider.GetService<IOptions<SigingKeyStoreOptions>>();

                var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var tokenProvider = scope.ServiceProvider.GetRequiredService<ManagedIdentityTokenProvider>();

                var client = new KeyVaultClient((string authority, string resource, string _) =>
                tokenProvider.GetTokenForResourceAsync(authority, resource), scope.ServiceProvider.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient());
                
                var certsVersions = await client.GetSecretVersionsAsync(options.Value.KeyVaultUri, options.Value.SecretName);
                var certsIds = certsVersions.Where(k => !old.Contains(k.Identifier.Identifier) && (k.Attributes?.Enabled ?? false)).ToArray();
                var secrets = await Task.WhenAll(certsIds.Select(k => client.GetSecretAsync(k.Identifier.Identifier)));

                return new ExpirationMetadata<KeyVaultCertificate[]>
                {

                    ValidUntil = DateTimeOffset.UtcNow.AddHours(1),
                    Result = secrets.Select((e, i) =>
                        new KeyVaultCertificate
                        {
                            Cert = new X509Certificate2(Convert.FromBase64String(e.Value), string.Empty, X509KeyStorageFlags.MachineKeySet),
                            Identifier = certsIds[i].Identifier.Identifier
                        }).Concat(arg.Result ?? Array.Empty<KeyVaultCertificate>()).ToArray()
                };

            }
        }

        public async Task<SigningCredentials> GetSigningCredentialsAsync()
        {
            var certs = await this.certs;
            return new SigningCredentials(new X509SecurityKey(certs.First().Cert), "RS256");
        }

        public async Task<IEnumerable<SecurityKey>> GetValidationKeysAsync()
        {
            var certs = await this.certs;
            return certs.Select(c => new X509SecurityKey(c.Cert) as AsymmetricSecurityKey).ToArray();
        }
    }
}

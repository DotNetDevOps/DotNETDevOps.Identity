namespace DotNETDevOps.Extensions.IdentityServer4
{
    public class SigingKeyStoreOptions
    {
        public string KeyVaultUri { get; set; }
        public string SecretName { get; set; } = "certificate";
    }
}

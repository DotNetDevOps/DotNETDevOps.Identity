using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using DotNETDevOps.Extensions.IdentityServer4;
using IdentityModel;
using IdentityModel.Client;
using IdentityServer4.Extensions;
using IdentityServer4.Models;
using IdentityServer4.Services;
using IdentityServer4.Stores;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.WebKey;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;

namespace DotNETDevOps.Identity.HeadlessApp
{
    public class FileCache : TokenCache
    {
        private readonly ILogger Logger;
        private readonly IDataProtector Protector;
        public string CacheFilePath;
        private static readonly object FileLock = new object();


        public FileCache(
           ILoggerFactory loggerFactory,
           IDataProtectionProvider dataProtectionProvider
            ) : this(loggerFactory, dataProtectionProvider, @".\TokenCache.dat")
        {

        }
        // Initializes the cache against a local file.
        // If the file is already present, it loads its content in the ADAL cache
        public FileCache(
            ILoggerFactory loggerFactory,
            IDataProtectionProvider dataProtectionProvider,
            string filePath)
        {
            CacheFilePath = filePath;
            Logger = loggerFactory.CreateLogger<FileCache>();
            Protector = dataProtectionProvider.CreateProtector(typeof(FileCache).FullName);

            this.AfterAccess = AfterAccessNotification;
            this.BeforeAccess = BeforeAccessNotification;
            lock (FileLock)
            {
                this.Deserialize(File.Exists(CacheFilePath) ?
                    Protector.Unprotect(File.ReadAllBytes(CacheFilePath))
                    : null);
            }
        }

        // Empties the persistent store.
        public override void Clear()
        {
            base.Clear();
            File.Delete(CacheFilePath);
        }

        // Triggered right before ADAL needs to access the cache.
        // Reload the cache from the persistent store in case it changed since the last access.
        void BeforeAccessNotification(TokenCacheNotificationArgs args)
        {
            lock (FileLock)
            {
                this.Deserialize(File.Exists(CacheFilePath) ?
                    Protector.Unprotect(File.ReadAllBytes(CacheFilePath))
                    : null);
            }
        }

        // Triggered right after ADAL accessed the cache.
        void AfterAccessNotification(TokenCacheNotificationArgs args)
        {
            // if the access operation resulted in a cache update
            if (this.HasStateChanged)
            {
                lock (FileLock)
                {
                    // reflect changes in the persistent store
                    File.WriteAllBytes(CacheFilePath,
                        Protector.Protect(this.Serialize()));
                    // once the write operation took place, restore the HasStateChanged bit to false
                    this.HasStateChanged = false;
                }
            }
        }
    }

    

   

    public class ClientStore : IClientStore
    {
        public Task<Client> FindClientByIdAsync(string clientId)
        {
            throw new NotImplementedException();
        }
    }
    public class ResourceStore : IResourceStore
    {
        private Resources inmem = new Resources(new IdentityResource[]
        {
                new IdentityResources.OpenId(),
                new IdentityResources.Profile(),
                new IdentityResources.Address(),
                new IdentityResources.Phone(),
                new IdentityResources.Email(),
                new IdentityResource("roles", new[] { JwtClaimTypes.Role }),
        },new ApiResource[0]);

        public Task<ApiResource> FindApiResourceAsync(string name)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<ApiResource>> FindApiResourcesByScopeAsync(IEnumerable<string> scopeNames)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<IdentityResource>> FindIdentityResourcesByScopeAsync(IEnumerable<string> scopeNames)
        {
            throw new NotImplementedException();
        }

        public Task<Resources> GetAllResourcesAsync()
        {
            return Task.FromResult(inmem);
        }
    }

   
    //public class KeyVaultTokenCreationService : DefaultTokenCreationService
    //{
    //    public KeyVaultTokenCreationService(ISystemClock clock, IKeyMaterialService keys, ILogger<DefaultTokenCreationService> logger) : base(clock, keys, logger)
    //    {
    //    }

    //    protected override async Task<string> CreateJwtAsync(JwtSecurityToken jwt)
    //    {
    //        var rawDataBytes = System.Text.Encoding.UTF8.GetBytes(jwt.EncodedHeader + "." + jwt.EncodedPayload);

    //        /** KeyVault keys' KeyIdentifierFormat: https://{vaultname}.vault.azure.net/keys/{keyname} */
    //        var keyIdentifier = "https://dotnetdevopsidentity.vault.azure.net/keys/identityserver"; // string.Format(KeyVaultConstants.KeyIdentifierFormat, settings.VaultName, settings.KeyName);
    //        //var signatureProvider = new KeyVaultSignatureProvider(keyIdentifier, JsonWebKeySignatureAlgorithm.RS256, authentication.KeyVaultAuthenticationCallback);
    //        var a = new AzureServiceTokenProvider();
    //        var keyvault = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(a.KeyVaultTokenCallback));
    //        var _hashAlgorithm = SHA256.Create();
    //        var digest = _hashAlgorithm.ComputeHash(rawDataBytes);
    //        ;
    //        var signedResult = await keyvault.SignAsync(keyIdentifier, JsonWebKeySignatureAlgorithm.RS256, digest);
    //        var rawSignature = Base64UrlEncoder.Encode(signedResult.Result);

    //        return jwt.EncodedHeader + "." + jwt.EncodedPayload + "." + rawSignature;
    //    }

    //    protected override async Task<JwtHeader> CreateHeaderAsync(Token token)
    //    {
             
    //        var credentials = await GetSigningCredentialsAsync();
    //        var header = new JwtHeader(credentials);
    //        return header;
    //    }

    //    private async Task<SigningCredentials> GetSigningCredentialsAsync()
    //    {
    //        //var jwk = await publicKeyProvider.GetSigningCredentialsAsync();

    //        var a = new AzureServiceTokenProvider();
    //        var keyvault = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(a.KeyVaultTokenCallback));
    //        var keyIdentifier = "https://dotnetdevopsidentity.vault.azure.net/keys/identityserver";
    //        var keyBundle = await keyvault.GetKeyAsync(keyIdentifier);

    //        var jwk = keyBundle.Key; //new JsonWebKey(keyBundle.Key.ToString());

    //        var parameters = new RSAParameters
    //        {
    //            Exponent = jwk.E, // Base64UrlEncoder.DecodeBytes(jwk.E),
    //            Modulus = jwk.N// Base64UrlEncoder.DecodeBytes(jwk.N)
    //        };

    //        var securityKey = new RsaSecurityKey(parameters)
    //        {
    //            KeyId = jwk.Kid,
    //        };

    //        return new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
    //    }
    //}
    public class Startup
    {
        private readonly IConfiguration configuration;

        public Startup(IConfiguration configuration)
        {
            this.configuration = configuration;
        }
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient();
            services.AddSingleton<TokenCache, FileCache>();
            services.AddSingleton<SigingKeyStore>();
            services.AddSingleton<ManagedIdentityTokenProvider>();
            services.AddSingleton<IValidationKeysStore, SigingKeyStore>();
            services.AddSingleton<ISigningCredentialStore, SigingKeyStore>();
            services.Configure<ManagedIdentityTokenProviderOptions>(configuration.GetSection("client"));
            services.Configure<SigingKeyStoreOptions>(configuration);

            var authority = configuration.GetValue<string>("authority");
            var resourceApiEndpoint = configuration.GetValue<string>("resourceApiEndpoint");

            services.AddMvc()
               .SetCompatibilityVersion(Microsoft.AspNetCore.Mvc.CompatibilityVersion.Latest)
               .AddJsonOptions((options) =>
               {
                   options.SerializerSettings.Converters.Add(new IdentityServer4.Stores.Serialization.ClaimConverter());
               });

            var idsrvBuilder = services.AddIdentityServer(options =>
            {

                options.UserInteraction.ErrorUrl = "/error";
                options.Events.RaiseFailureEvents = true;
                options.Events.RaiseErrorEvents = true;
                options.Events.RaiseSuccessEvents = true;
            }).AddClientStore<ClientStore>()
            .AddInMemoryApiResources(new[]
           {

                new ApiResource($"{resourceApiEndpoint}/providers/storage.io", "Storage.IO Provider")
                {
                },
                new ApiResource($"{resourceApiEndpoint}/providers/io-board.identity", "IO-Board Identity Services", new []{ "name","isTrial","namespace" })
                {
                }
               })
            .AddInMemoryIdentityResources(new[]
            {
                // some standard scopes from the OIDC spec
                 new IdentityResources.OpenId(),
                new IdentityResources.Profile(),
                new IdentityResources.Address(),
                new IdentityResources.Phone(),
                new IdentityResources.Email(),
                new IdentityResource("roles", new[] { JwtClaimTypes.Role }),
            });
            //.AddResourceStore<ResourceStore>();

       //  idsrvBuilder.AddValidationKeys
            
            services.AddSingleton(new DiscoveryCache(authority));

            services.AddAuthentication()
                .AddCookie("ApplicationTrialAccounts", o =>
                {
                    o.LoginPath = new PathString("/Account/TrialAccount/");
                    o.AccessDeniedPath = new PathString("/Account/Forbidden/");
                    o.ExpireTimeSpan = TimeSpan.FromDays(60);
                    o.Cookie.SameSite = SameSiteMode.None;
                    //   o.Cookie.Domain = ".earthml.com";

                }).AddIdentityServerAuthentication((options =>
                {
                    options.Authority = authority;
                    options.RequireHttpsMetadata = true;
                    options.EnableCaching = true;
                    options.ApiName = $"{resourceApiEndpoint}/providers/dotnetdevops.identity";
                    options.SaveToken = true;


                }))
            .AddCookie(o =>
            {

            });


            services.AddCors();
        }
        [DebuggerStepThrough]
        public  string CleanUrlPath( string url)
        {
            if (String.IsNullOrWhiteSpace(url)) url = "/";

            if (url != "/" && url.EndsWith("/"))
            {
                url = url.Substring(0, url.Length - 1);
            }

            return url;
        }
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {


           // if (!env.IsProduction())
            {
                app.UseDeveloperExceptionPage();
            }

            app.Use((ctx, next) =>
            {

                var http = ctx.RequestServices.GetService<IHttpContextAccessor>();

                return next();
            });

            app.UseIdentityServer();

            app.UseCors(
                     builder => builder.WithOrigins("https://localhost:44338", "https://io-board.com", "https://io-board.eu.ngrok.io")
                     .AllowAnyMethod()
                      .AllowAnyHeader().WithExposedHeaders("Location")
                     .AllowCredentials()
                     .SetPreflightMaxAge(TimeSpan.FromHours(1))
                     );

            app.UseMvc();
        }
    }
}

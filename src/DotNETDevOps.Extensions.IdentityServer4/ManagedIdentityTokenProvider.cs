using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace DotNETDevOps.Extensions.IdentityServer4
{
    public class ManagedIdentityTokenProvider
    {

        private readonly ILogger logger;
        private readonly IServiceScopeFactory serviceScopeFactory;
        //private readonly IOptions<DevTokenProviderOptions> options;
        //private readonly AuthenticationContext ctx;
        private readonly ConcurrentDictionary<string, AsyncExpiringLazy<string>> _tokens = new ConcurrentDictionary<string, AsyncExpiringLazy<string>>();




        public ManagedIdentityTokenProvider(
            ILogger<ManagedIdentityTokenProvider> logger, IServiceScopeFactory serviceScopeFactory)
        {
            //this.ctx = new AuthenticationContext($"https://login.microsoftonline.com/{options.Value.TenantId}", tokenCache);
            this.logger = logger;
            this.serviceScopeFactory = serviceScopeFactory;

        }

        async Task<ExpirationMetadata<string>> GetTokenViaCode(string authority, string resource)
        {


            using (var scope = serviceScopeFactory.CreateScope())
            {

                var http = scope.ServiceProvider.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient();

                var req = new HttpRequestMessage(HttpMethod.Get, $"http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource={resource}");
                req.Headers.TryAddWithoutValidation("Metadata", "true");




                try
                {
                    var tokenresponse = await http.SendAsync(req);

                    tokenresponse.EnsureSuccessStatusCode();


                    var token = JToken.Parse(await tokenresponse.Content.ReadAsStringAsync()).SelectToken("$.access_token").ToString();
                    //TODO get proper validuntil
                    return new ExpirationMetadata<string> { Result = token, ValidUntil = DateTimeOffset.UtcNow.AddMinutes(2) };

                }
                catch (HttpRequestException ex)
                {

                }

                try
                {
                    var a = new AzureServiceTokenProvider();

                    var accessToken = await a.GetAccessTokenAsync(resource, authority);
                    if (accessToken != null)
                    {
                        return new ExpirationMetadata<string> { Result = accessToken, ValidUntil = DateTimeOffset.UtcNow.AddMinutes(2) };
                    }
                }catch(Exception ex)
                {

                }


                IOptions<ManagedIdentityTokenProviderOptions> options = scope.ServiceProvider.GetRequiredService<IOptions<ManagedIdentityTokenProviderOptions>>();
                var ctx = new AuthenticationContext(authority ?? $"https://login.microsoftonline.com/{options.Value.TenantId}", scope.ServiceProvider.GetRequiredService<TokenCache>());
                AuthenticationResult result = null;
                try
                {
                    result = await ctx.AcquireTokenSilentAsync(resource, options.Value.ApplicationId);
                }
                catch (AdalException adalException)
                {
                    if (adalException.ErrorCode == AdalError.FailedToAcquireTokenSilently
                     || adalException.ErrorCode == AdalError.UserInteractionRequired)
                    {
                        try
                        {
                            DeviceCodeResult codeResult = await ctx.AcquireDeviceCodeAsync(resource, options.Value.ApplicationId);

                            logger.LogInformation(codeResult.Message);
                            var url = codeResult.VerificationUrl + "#" + codeResult.UserCode;
                            //Process.Start( codeResult.VerificationUrl+"#"+codeResult.UserCode);
                            var startInfo = new ProcessStartInfo("explorer.exe", url);
                            Process.Start(startInfo);

                            result = await ctx.AcquireTokenByDeviceCodeAsync(codeResult);
                        }
                        catch (Exception exc)
                        {
                            logger.LogError(exc, "Failed to get token");

                        }
                    }

                }

                return new ExpirationMetadata<string> { Result = result?.AccessToken, ValidUntil = result?.ExpiresOn ?? DateTimeOffset.UtcNow.AddSeconds(10) };

            }



        }

        public async Task<string> GetTokenForResourceAsync(string authority, string resource)
        {
            return await _tokens.GetOrAdd(authority + resource, (key) => new AsyncExpiringLazy<string>(async old => await GetTokenViaCode(authority, resource))).Value();

        }
    }
}

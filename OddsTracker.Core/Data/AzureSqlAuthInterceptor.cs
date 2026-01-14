using System.Data.Common;
using Azure.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OddsTracker.Core.Data
{
    /// <summary>
    /// EF Core interceptor that adds Azure AD token authentication to SQL connections.
    /// Enables passwordless authentication using Managed Identity or Azure CLI credentials.
    /// </summary>
    public class AzureSqlAuthInterceptor(TokenCredential credential) : DbConnectionInterceptor
    {
        private static readonly string[] AzureSqlScopes = ["https://database.windows.net/.default"];

        public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (connection is SqlConnection sqlConnection)
            {
                var token = await GetAccessTokenAsync(cancellationToken);
                sqlConnection.AccessToken = token;
            }

            return result;
        }

        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result)
        {
            if (connection is SqlConnection sqlConnection)
            {
                var token = GetAccessTokenAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                sqlConnection.AccessToken = token;
            }

            return result;
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            var tokenRequestContext = new TokenRequestContext(AzureSqlScopes);
            var accessToken = await credential.GetTokenAsync(tokenRequestContext, cancellationToken);
            return accessToken.Token;
        }
    }
}
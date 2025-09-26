using Duende.IdentityModel.Client;
using Microsoft.Extensions.Logging;

namespace Crucible.Common.Authentication.ServiceAccounts;

public interface ICrucibleServiceAccountTokenService
{
    public Task<TokenResponse> GetTokenAsync(CancellationToken cancellationToken);
    void InvalidateToken();
}

internal sealed class CrucibleServiceAccountTokenService
(
    CrucibleServiceAccountAuthenticationConfig crucibleServiceAccountAuthConfig,
    HttpClient httpClient,
    ILogger<CrucibleServiceAccountTokenService> logger
) : ICrucibleServiceAccountTokenService
{
    private TokenResponse? _currentToken = null;
    private readonly SemaphoreSlim _semaphore = new(1);

    public async Task<TokenResponse> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_currentToken is null || _currentToken.ExpiresIn <= crucibleServiceAccountAuthConfig.RenewTokenThreshold)
        {
            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                logger.LogInformation("Renewing auth token...");
                _currentToken = await RenewToken(cancellationToken);
                logger.LogInformation("Auth token renewed.");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        return _currentToken;
    }

    public void InvalidateToken()
    {
        _currentToken = null;
    }

    private async Task<TokenResponse> RenewToken(CancellationToken cancellationToken)
    {
        logger.LogInformation("Renewing auth token...");
        logger.LogInformation("Discovering token endpoint...");
        var discoDoc = await httpClient.GetDiscoveryDocumentAsync(crucibleServiceAccountAuthConfig.OidcAuthority, cancellationToken);

        logger.LogInformation("Requesting new token...");
        var tokenResponse = await httpClient.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
        {
            Address = discoDoc.TokenEndpoint,
            ClientId = crucibleServiceAccountAuthConfig.ClientId,
            ClientSecret = crucibleServiceAccountAuthConfig.ClientSecret
        }, cancellationToken);

        if (tokenResponse.IsError)
        {
            logger.LogError("Exception renewing auth token. {exMessage} {exDescription}", tokenResponse.Error, tokenResponse.ErrorDescription);
            throw new Exception(tokenResponse.Error);
        }

        logger.LogInformation("Auth token renewed.");
        return tokenResponse;
    }
}

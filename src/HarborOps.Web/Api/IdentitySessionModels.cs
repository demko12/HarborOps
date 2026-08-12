namespace HarborOps.Web.Api;

internal sealed record IdentityAccessTokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    string TokenType);

internal sealed record CurrentAccountResponse(Guid Id, string Email);

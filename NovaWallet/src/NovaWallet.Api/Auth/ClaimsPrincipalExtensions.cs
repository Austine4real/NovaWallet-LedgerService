using System.Security.Claims;

namespace NovaWallet.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    private const string CustomerIdClaimType = "sub";

    /// <summary>
    /// Reads the authenticated customer id from the JWT's "sub" claim.
    /// Relies on JwtBearerOptions.MapInboundClaims being set to false (see
    /// Program.cs) - without that, some JWT handler configurations silently
    /// remap "sub" to a legacy XML claim type
    /// (ClaimTypes.NameIdentifier), and a literal "sub" lookup here would
    /// always return null.
    /// </summary>
    public static string GetCustomerId(this ClaimsPrincipal user)
        => user.FindFirst(CustomerIdClaimType)?.Value
           ?? throw new InvalidOperationException(
               "Authenticated principal is missing the 'sub' claim - this should be impossible for a request that passed [Authorize].");
}

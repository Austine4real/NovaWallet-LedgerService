using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Auth;

namespace NovaWallet.Api.Controllers;

public record TokenRequest(string CustomerId);
public record TokenResponse(string AccessToken, string TokenType, int ExpiresInMinutes);

/// <summary>
/// Mock token issuer, purely to make the API exercisable end-to-end without
/// standing up a real identity provider. See JwtTokenService for context.
/// </summary>
[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly JwtTokenService _tokenService;

    public AuthController(JwtTokenService tokenService) => _tokenService = tokenService;

    [HttpPost("token")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    public IActionResult IssueToken([FromBody] TokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId))
            return ValidationProblem("customerId is required.");

        var token = _tokenService.IssueToken(request.CustomerId);
        return Ok(new TokenResponse(token, "Bearer", 60));
    }
}

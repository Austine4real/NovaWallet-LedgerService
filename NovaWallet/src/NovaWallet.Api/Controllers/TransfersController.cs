using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NovaWallet.Api.Auth;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;
using NovaWallet.Domain.Exceptions;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Authorize]
[Route("transfers")]
[EnableRateLimiting("transfers")]
public class TransfersController : ControllerBase
{
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private readonly TransferService _transferService;

    public TransfersController(TransferService transferService) => _transferService = transferService;

    [HttpPost]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Transfer(
        [FromBody] TransferRequest request,
        [FromHeader(Name = IdempotencyHeaderName)] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new IdempotencyKeyMissingException();

        var result = await _transferService.TransferAsync(request, idempotencyKey, User.GetCustomerId(), ct);
        return Ok(result);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.Auth;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Authorize]
[Route("wallets")]
public class WalletsController : ControllerBase
{
    private readonly WalletService _walletService;

    public WalletsController(WalletService walletService) => _walletService = walletService;

    [HttpPost]
    [ProducesResponseType(typeof(WalletResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateWallet([FromBody] CreateWalletRequest request, CancellationToken ct)
    {
        var wallet = await _walletService.CreateWalletAsync(request, User.GetCustomerId(), ct);
        return CreatedAtAction(nameof(GetBalance), new { walletId = wallet.WalletId }, wallet);
    }

    [HttpGet("{walletId:guid}/balance")]
    [ProducesResponseType(typeof(BalanceResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBalance(Guid walletId, CancellationToken ct)
        => Ok(await _walletService.GetBalanceAsync(walletId, User.GetCustomerId(), ct));

    [HttpPost("{walletId:guid}/credit")]
    [ProducesResponseType(typeof(BalanceResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Credit(Guid walletId, [FromBody] CreditWalletRequest request, CancellationToken ct)
        => Ok(await _walletService.CreditWalletAsync(walletId, request, User.GetCustomerId(), ct));

    [HttpGet("{walletId:guid}/statement")]
    [ProducesResponseType(typeof(StatementResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatement(
        Guid walletId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _walletService.GetStatementAsync(walletId, page, pageSize, User.GetCustomerId(), ct));
}

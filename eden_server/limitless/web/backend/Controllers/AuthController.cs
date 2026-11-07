using Limitless.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Limitless.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    public AuthController(AuthService auth) => _auth = auth;

    public record LoginReq(string Username, string Password);

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginReq req)
    {
        var v = await _auth.ValidateAsync(req.Username, req.Password);
        if (v is null) return Unauthorized(new { error = "bad credentials" });
        var token = _auth.IssueToken(v.Value.Id, req.Username, v.Value.Role);
        return Ok(new { token, accountId = v.Value.Id, role = v.Value.Role });
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new
    {
        id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
        name = User.Identity?.Name,
        role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
    });
}

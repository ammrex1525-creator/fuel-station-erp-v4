using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FuelStationERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace FuelStationERP.API.Controllers;

[ApiController, Route("api/v1/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ErpDbContext db,
    IConfiguration configuration) : ControllerBase
{
    [AllowAnonymous, HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByNameAsync(request.Username);
        if (user is null) return Unauthorized(new { error = "Invalid username or password." });
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized(new { error = "Invalid username or password." });

        var memberships = await db.UserBranchMemberships
            .Where(x => x.UserId == user.Id && x.IsActive)
            .Select(x => new { x.BranchId, x.RoleName })
            .ToListAsync(ct);
        var roles = await users.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? request.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (user.Email is not null) claims.Add(new Claim(ClaimTypes.Email, user.Email));
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(memberships.Select(m => new Claim("branch_id", m.BranchId.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        claims.AddRange(memberships.Select(m => new Claim("branch_role", $"{m.BranchId}:{m.RoleName}")));

        var now = DateTime.UtcNow;
        var expiry = now.AddMinutes(15);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:SigningKey"]!));
        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"], audience: configuration["Jwt:Audience"], claims: claims,
            notBefore: now, expires: expiry, signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(token), tokenType = "Bearer", expiresAtUtc = expiry, branchIds = memberships.Select(x => x.BranchId) });
    }
}

public sealed record LoginRequest([property: Required, StringLength(256)] string Username, [property: Required, StringLength(256)] string Password);

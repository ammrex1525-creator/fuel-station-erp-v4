using System.ComponentModel.DataAnnotations;
using FuelStationERP.Domain.Entities;
using FuelStationERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelStationERP.API.Controllers;

[ApiController, Authorize(Roles = "OWNER"), Route("api/v1/users")]
public sealed class UsersController(UserManager<ApplicationUser> users, ErpDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await db.Users.AsNoTracking()
        .Select(x => new { x.Id, x.UserName, x.Email, x.DefaultBranchId })
        .OrderBy(x => x.UserName).ToListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        if (!new[] { "ADMIN", "MANAGER", "STAFF" }.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
            return BadRequest("New branch users must have the ADMIN, MANAGER or STAFF role.");
        if (!await db.Branches.AnyAsync(x => x.Id == request.BranchId && x.IsActive, ct)) return BadRequest("Select an active branch.");

        var user = new ApplicationUser { UserName = request.Username.Trim(), Email = request.Email.Trim(), DefaultBranchId = request.BranchId };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) return BadRequest(new ValidationProblemDetails(result.Errors.ToDictionary(x => x.Code, x => new[] { x.Description })));
        await users.AddToRoleAsync(user, request.Role.ToUpperInvariant());
        db.UserBranchMemberships.Add(new UserBranchMembership { UserId = user.Id, BranchId = request.BranchId, RoleName = request.Role.ToUpperInvariant() });
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/users/{user.Id}", new { user.Id, user.UserName, user.Email, branchId = request.BranchId, role = request.Role.ToUpperInvariant() });
    }
}

public sealed record CreateUserRequest(
    [property: Required, StringLength(256, MinimumLength = 3)] string Username,
    [property: Required, EmailAddress, StringLength(256)] string Email,
    [property: Required, StringLength(128, MinimumLength = 12)] string Password,
    [property: Range(1, long.MaxValue)] long BranchId,
    [property: Required] string Role);

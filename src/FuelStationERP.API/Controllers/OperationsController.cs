using FuelStationERP.Domain.Entities;
using FuelStationERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;

namespace FuelStationERP.API.Controllers;

[ApiController, Route("api/v1/branches/{branchId:long}/operations")]
[Authorize]
public sealed class OperationsController(ErpDbContext db) : ControllerBase
{
    // Mobile, desktop and web submissions converge here; branch access policy is enforced before persistence.
    [HttpPost("submissions")]
    public async Task<IActionResult> Submit(long branchId, [FromBody] SubmissionRequest request, CancellationToken ct)
    {
        if (!CanAccessBranch(branchId)) return Forbid();
        if (!CanSubmitToBranch(branchId)) return Forbid();
        if (!await db.Branches.AnyAsync(x => x.Id == branchId && x.IsActive, ct)) return NotFound();
        var externalId = string.IsNullOrWhiteSpace(request.ExternalId) ? null : request.ExternalId.Trim();
        if (externalId is not null)
        {
            var existing = await db.SyncSubmissions.Where(x => x.BranchId == branchId && x.ExternalId == externalId)
                .Select(x => new { x.Id, x.Status }).SingleOrDefaultAsync(ct);
            if (existing is not null) return AcceptedAtAction(nameof(GetSubmission), new { branchId, id = existing.Id }, existing);
        }
        var item = new SyncSubmission { BranchId = branchId, Source = request.Source, SubmissionType = request.Type,
            PayloadJson = request.Payload.GetRawText(), ExternalId = externalId, Status = "Pending", SubmittedAt = DateTimeOffset.UtcNow };
        db.SyncSubmissions.Add(item); await db.SaveChangesAsync(ct);
        return AcceptedAtAction(nameof(GetSubmission), new { branchId, id = item.Id }, new { item.Id, item.Status });
    }

    [HttpGet("submissions/{id:long}")]
    public async Task<IActionResult> GetSubmission(long branchId, long id, CancellationToken ct)
    {
        if (!CanAccessBranch(branchId)) return Forbid();
        var item = await db.SyncSubmissions.Where(x => x.Id == id && x.BranchId == branchId)
            .Select(x => new { x.Id, x.SubmissionType, x.Status, x.SubmittedAt }).SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : Ok(item);
    }

    private bool CanAccessBranch(long branchId) => User.IsInRole("OWNER") ||
        User.FindAll("branch_id").Any(c => c.Value == branchId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private bool CanSubmitToBranch(long branchId) => User.IsInRole("OWNER") || User.FindAll("branch_role").Any(c =>
    {
        var prefix = branchId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":";
        return c.Value.StartsWith(prefix, StringComparison.Ordinal) &&
            new[] { "STAFF", "MANAGER", "ADMIN" }.Contains(c.Value[prefix.Length..], StringComparer.OrdinalIgnoreCase);
    });
}

public sealed record SubmissionRequest(
    [property: Required, StringLength(30)] string Source,
    [property: Required, StringLength(80)] string Type,
    [property: Required] JsonElement Payload,
    [property: StringLength(120)] string? ExternalId);

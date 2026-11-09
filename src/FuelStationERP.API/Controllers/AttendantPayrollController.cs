using FuelStationERP.Infrastructure.Persistence;
using FuelStationERP.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelStationERP.API.Controllers;

[ApiController, Authorize, Route("api/v1/branches/{branchId:long}/payroll")]
public sealed class AttendantPayrollController(ErpDbContext db, AttendantPayrollService payroll) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(long branchId, [FromQuery] DateOnly? asOf, CancellationToken ct)
    {
        if (!CanManage(branchId)) return Forbid();
        var date = asOf ?? DateOnly.FromDateTime(DateTime.Today);
        var attendants = await db.StaffAttendants.AsNoTracking().Where(x => x.BranchId == branchId).OrderBy(x => x.Name).ToListAsync(ct);
        var previews = new List<object>(attendants.Count);
        foreach (var attendant in attendants)
        {
            try
            {
                var preview = await payroll.PreviewAsync(branchId, attendant.Id, date, ct);
                previews.Add(new
                {
                    attendantId = attendant.Id,
                    attendantName = attendant.Name,
                    preview.Period.Payday,
                    preview.Period.PeriodStart,
                    preview.Period.IsDue,
                    preview.BaseSalary,
                    preview.OutstandingShort,
                    preview.OverageTotal,
                    preview.OverageOffset,
                    preview.ShortDeduction,
                    preview.OverageCredit,
                    preview.NetSalary
                });
            }
            catch (InvalidOperationException error)
            {
                previews.Add(new { attendantId = attendant.Id, attendantName = attendant.Name, error = error.Message });
            }
        }

        var payments = await (from payment in db.AttendantSalaryPayments.AsNoTracking()
                              join attendant in db.StaffAttendants.AsNoTracking() on payment.AttendantId equals attendant.Id
                              join account in db.Accounts.AsNoTracking() on payment.AccountId equals account.Id
                              where payment.BranchId == branchId
                              orderby payment.PaidDate descending, payment.Id descending
                              select new
                              {
                                  payment.Id,
                                  payment.AttendantId,
                                  attendantName = attendant.Name,
                                  payment.Payday,
                                  payment.PaidDate,
                                  payment.BaseSalary,
                                  payment.ShortDeduction,
                                  payment.OverageOffset,
                                  payment.OverageCredit,
                                  payment.NetSalary,
                                  accountName = account.Name,
                                  payment.Notes
                              }).Take(100).ToListAsync(ct);
        return Ok(new { asOf = date, payroll = previews, payments });
    }

    [HttpPost("payments")]
    public async Task<IActionResult> Process(long branchId, [FromBody] ProcessSalaryPaymentRequest request, CancellationToken ct)
    {
        if (!CanManage(branchId)) return Forbid();
        if (request.AttendantId <= 0 || request.AccountId <= 0 || request.PaymentDate == default)
            return BadRequest("Choose an attendant, a payment account and a valid payment date.");
        try
        {
            var payment = await payroll.ProcessPaymentAsync(branchId, request.AttendantId, request.AccountId, request.PaymentDate, request.Notes, ct);
            return Created($"/api/v1/branches/{branchId}/payroll/payments/{payment.Id}", payment);
        }
        catch (InvalidOperationException error)
        {
            return BadRequest(new { error = error.Message });
        }
    }

    private bool CanManage(long branchId) => User.IsInRole("OWNER") ||
        User.FindAll("branch_role").Any(x => x.Value == $"{branchId}:ADMIN" || x.Value == $"{branchId}:MANAGER");
}

public sealed record ProcessSalaryPaymentRequest(long AttendantId, long AccountId, DateOnly PaymentDate, string? Notes);

using Microsoft.AspNetCore.Identity;
namespace FuelStationERP.Infrastructure.Persistence;
public sealed class ApplicationUser : IdentityUser { public long? DefaultBranchId { get; set; } }

using AccessRequestHub.Domain;
using AccessRequestHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.API;

public sealed class DemoIdentity(HubDbContext db)
{
    public DemoUser User { get; private set; } = null!;

    public async Task<bool> Resolve(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("X-Demo-User", out var values) || values.Count != 1)
            return false;
        var email = values[0]?.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, context.RequestAborted);
        if (user is null)
            return false;
        User = user;
        return true;
    }
}

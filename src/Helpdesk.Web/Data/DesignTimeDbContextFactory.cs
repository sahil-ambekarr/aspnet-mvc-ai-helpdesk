using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Helpdesk.Web.Data;

/// <summary>
/// Used only by `dotnet ef migrations add`. Migrations target SQL Server (the production database);
/// no connection is opened when generating them.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HelpdeskDbContext>
{
    public HelpdeskDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HelpdeskDbContext>()
            .UseSqlServer("Server=localhost;Database=Helpdesk;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new HelpdeskDbContext(options);
    }
}

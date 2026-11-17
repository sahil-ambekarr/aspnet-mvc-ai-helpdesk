using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Data;

public static class DatabaseSetup
{
    /// <summary>
    /// "Database:Provider" selects SQL Server (production) or SQLite (local runs and tests).
    /// </summary>
    public static IServiceCollection AddHelpdeskDatabase(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Database:Provider"] ?? "Sqlite";
        services.AddDbContext<HelpdeskDbContext>(options =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(
                    config.GetConnectionString("SqlServer")
                        ?? throw new InvalidOperationException("ConnectionStrings:SqlServer is required when Database:Provider=SqlServer."),
                    sql => sql.EnableRetryOnFailure(maxRetryCount: 5));
            }
            else
            {
                options.UseSqlite(config.GetConnectionString("Sqlite") ?? "Data Source=helpdesk.db");
            }
        });
        return services;
    }

    /// <summary>
    /// SQL Server: applies EF Core migrations. SQLite: EnsureCreated, because the checked-in migrations
    /// are generated for SQL Server types. Then seeds demo data when Seed:Enabled is true.
    /// </summary>
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();

        if (db.Database.IsSqlServer())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }

        if (app.Configuration.GetValue<bool>("Seed:Enabled"))
        {
            await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();
        }
    }
}

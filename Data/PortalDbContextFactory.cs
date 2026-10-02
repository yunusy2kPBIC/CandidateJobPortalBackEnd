using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CandidatePortal.Api.Data;

public sealed class PortalDbContextFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    public PortalDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=CandidatePortalDesignTime;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new PortalDbContext(options);
    }
}

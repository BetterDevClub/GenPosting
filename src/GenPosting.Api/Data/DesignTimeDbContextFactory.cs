using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GenPosting.Api.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GenPostingDbContext>
{
    public GenPostingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GenPostingDbContext>();
        optionsBuilder.UseSqlite("Data Source=genposting-designtime.db");
        return new GenPostingDbContext(optionsBuilder.Options);
    }
}

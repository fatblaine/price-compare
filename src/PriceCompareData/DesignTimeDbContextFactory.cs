using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PriceCompareData.Data;

namespace PriceCompareData
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            // Design-time only (migrations, `dotnet ef dbcontext optimize`). The provider must match
            // runtime (Npgsql) so the generated compiled model is valid; no connection is opened.
            optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=pricecompare_designtime;Username=postgres");

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
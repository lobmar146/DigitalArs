using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DigitalArs.Api.Data.Context;

// Independiente del contexto generado por scaffolding y de las transacciones de negocio.
public sealed class DataProtectionDbContext(DbContextOptions<DataProtectionDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
}

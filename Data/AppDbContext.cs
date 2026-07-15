using Microsoft.EntityFrameworkCore;
using BarberPro.Dominio;

namespace BarberPro.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Cliente> Clientes { get; set; } = default!;
    public DbSet<Barbero> Barberos { get; set; } = default!;
    public DbSet<Servicio> Servicios { get; set; } = default!;
    public DbSet<Cita> Citas { get; set; } = default!;
    public DbSet<CierreCaja> CierreCaja { get; set; } = default!;
    public DbSet<Usuario> Usuarios { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Cliente>(entity =>
        {
            entity.HasIndex(e => e.Codigo).IsUnique();
            entity.HasIndex(e => e.Telefono);
        });

        modelBuilder.Entity<Barbero>(entity =>
        {
            entity.HasIndex(e => e.Codigo).IsUnique();
        });

        modelBuilder.Entity<Servicio>(entity =>
        {
            entity.HasIndex(e => e.Codigo).IsUnique();
        });

        modelBuilder.Entity<Cita>(entity =>
        {
            entity.HasIndex(e => e.Codigo).IsUnique();
            entity.HasIndex(e => e.Fecha);
            entity.HasIndex(e => e.Estado);
            entity.HasIndex(e => new { e.BarberoId, e.Fecha, e.Hora });
        });

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<Cita>()
            .Property(e => e.Hora)
            .HasColumnType("time");
    }
}

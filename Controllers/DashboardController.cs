using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using BarberPro.Data;
using BarberPro.DTOs.Dashboard;

namespace BarberPro.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly string _connectionString;

    public DashboardController(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }

    [HttpGet("stats")]
    [Authorize(Roles = "Encargado")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats()
    {
        var hoy = DateTime.UtcNow.Date;
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT
                COUNT(*) FILTER (WHERE ""Estado"" = 'Pendiente') AS ""citasPendientes"",
                COUNT(*) FILTER (WHERE ""Estado"" = 'Confirmada') AS ""citasConfirmadas"",
                COUNT(*) FILTER (WHERE ""Estado"" IN ('Completada','Terminada') AND ""Fecha""::date = @hoy) AS ""citasCompletadas"",
                COUNT(*) FILTER (WHERE ""Fecha""::date = @hoy AND ""Estado"" != 'Inactivo') AS ""citasHoy"",
                COALESCE(SUM(s.""Precio"") FILTER (WHERE c.""Fecha""::date = @hoy AND c.""Estado"" IN ('Completada','Terminada')), 0) AS ""totalRecaudadoHoy""
            FROM ""Citas"" c
            JOIN ""Servicios"" s ON c.""ServicioId"" = s.""Id""", conn);
        cmd.Parameters.AddWithValue("@hoy", hoy);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return Ok(new DashboardStatsDto
        {
            CitasPendientes = reader.GetInt32(0),
            CitasConfirmadas = reader.GetInt32(1),
            CitasCompletadas = reader.GetInt32(2),
            CitasHoy = reader.GetInt32(3),
            TotalRecaudadoHoy = reader.GetDecimal(4)
        });
    }

    [HttpGet("stats-personales")]
    [Authorize(Roles = "Barbero")]
    public async Task<ActionResult<DashboardStatsPersonalesDto>> GetStatsPersonales()
    {
        var barberoId = int.Parse(User.FindFirstValue("BarberoId") ?? "0");
        if (barberoId == 0)
            return BadRequest(new { mensaje = "No se encontró el barbero asociado" });

        var hoy = DateTime.UtcNow.Date;
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT
                COUNT(*) FILTER (WHERE ""Fecha""::date = @hoy AND ""Estado"" != 'Inactivo') AS ""citasHoy"",
                COUNT(*) FILTER (WHERE ""Fecha""::date = @hoy AND ""Estado"" IN ('Completada','Terminada')) AS ""citasCompletadasHoy""
            FROM ""Citas""
            WHERE ""BarberoId"" = @barberoId", conn);
        cmd.Parameters.AddWithValue("@barberoId", barberoId);
        cmd.Parameters.AddWithValue("@hoy", hoy);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return Ok(new DashboardStatsPersonalesDto
        {
            CitasHoy = reader.GetInt32(0),
            CitasCompletadasHoy = reader.GetInt32(1)
        });
    }

    [HttpGet("buscar")]
    [Authorize(Roles = "Encargado")]
    public async Task<ActionResult<IEnumerable<BuscarCitaDto>>> Buscar([FromQuery] string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return Ok(new List<BuscarCitaDto>());

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT c.""CodigoGenerado"", cl.""Nombre"", cl.""Telefono"",
                   b.""Nombre"", s.""Nombre"", s.""Precio"",
                   c.""Fecha"", to_char(c.""Hora"", 'HH24:MI'), c.""Estado""
            FROM ""Citas"" c
            JOIN ""Clientes"" cl ON c.""ClienteId"" = cl.""Id""
            JOIN ""Barberos"" b ON c.""BarberoId"" = b.""Id""
            JOIN ""Servicios"" s ON c.""ServicioId"" = s.""Id""
            WHERE cl.""Nombre"" ILIKE @nombre AND c.""Estado"" != 'Inactivo'
            ORDER BY c.""Fecha"" DESC
            LIMIT 50", conn);
        cmd.Parameters.AddWithValue("@nombre", $"%{nombre}%");
        await using var reader = await cmd.ExecuteReaderAsync();

        var citas = new List<BuscarCitaDto>();
        while (await reader.ReadAsync())
        {
            citas.Add(new BuscarCitaDto
            {
                CodigoGenerado = reader.GetString(0),
                ClienteNombre = reader.GetString(1),
                ClienteTelefono = reader.GetString(2),
                BarberoNombre = reader.GetString(3),
                ServicioNombre = reader.GetString(4),
                ServicioPrecio = reader.GetDecimal(5),
                Fecha = reader.GetDateTime(6),
                Hora = reader.GetString(7),
                Estado = reader.GetString(8)
            });
        }
        return Ok(citas);
    }
}

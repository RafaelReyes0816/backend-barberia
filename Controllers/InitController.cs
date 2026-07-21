using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BarberPro.Data;
using BarberPro.DTOs.Barberos;
using BarberPro.DTOs.Clientes;
using BarberPro.DTOs.Servicios;
using BarberPro.DTOs.Citas;
using BarberPro.DTOs.Dashboard;
using BarberPro.DTOs.CierreCaja;
using BarberPro.DTOs.Init;
using Npgsql;

namespace BarberPro.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class InitController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly string _connectionString;

    public InitController(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _connectionString = context.Database.GetConnectionString()!;
    }

    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    [HttpGet("encargado")]
    [Authorize(Roles = "Encargado")]
    public async Task<ActionResult<InitEncargadoDto>> GetEncargado()
    {
        var hoy = DateTime.UtcNow.Date;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT 'stats', row_to_json(t) FROM (
                SELECT
                    (SELECT COUNT(*) FROM ""Citas"" WHERE ""Estado"" = 'Pendiente') AS ""citasPendientes"",
                    (SELECT COUNT(*) FROM ""Citas"" WHERE ""Estado"" = 'Confirmada') AS ""citasConfirmadas"",
                    (SELECT COUNT(*) FROM ""Citas"" WHERE ""Estado"" IN ('Completada','Terminada') AND ""Fecha""::date = @hoy) AS ""citasCompletadas"",
                    (SELECT COUNT(*) FROM ""Citas"" WHERE ""Fecha""::date = @hoy AND ""Estado"" != 'Inactivo') AS ""citasHoy"",
                    COALESCE((SELECT SUM(s.""Precio"") FROM ""Citas"" c JOIN ""Servicios"" s ON c.""ServicioId"" = s.""Id"" WHERE c.""Fecha""::date = @hoy AND c.""Estado"" IN ('Completada','Terminada')), 0) AS ""totalRecaudadoHoy""
            ) t
            UNION ALL
            SELECT 'barberos', COALESCE((SELECT json_agg(row_to_json(b)) FROM (SELECT ""Codigo"" as ""codigo"", ""Nombre"" as ""nombre"", ""Estado"" as ""estado"", ""FechaCreacion"" as ""fechaCreacion"" FROM ""Barberos"" WHERE ""Estado"" != 'Inactivo' ORDER BY ""Nombre"") b), '[]'::json)
            UNION ALL
            SELECT 'servicios', COALESCE((SELECT json_agg(row_to_json(s)) FROM (SELECT ""Codigo"" as ""codigo"", ""Nombre"" as ""nombre"", ""Precio"" as ""precio"", ""Estado"" as ""estado"", ""FechaCreacion"" as ""fechaCreacion"" FROM ""Servicios"" WHERE ""Estado"" != 'Inactivo' ORDER BY ""Nombre"") s), '[]'::json)
            UNION ALL
            SELECT 'clientes', COALESCE((SELECT json_agg(row_to_json(c)) FROM (SELECT ""Codigo"" as ""codigo"", ""Nombre"" as ""nombre"", ""Telefono"" as ""telefono"", ""Estado"" as ""estado"", ""FechaCreacion"" as ""fechaCreacion"" FROM ""Clientes"" WHERE ""Estado"" != 'Inactivo' ORDER BY ""Nombre"") c), '[]'::json)
            UNION ALL
            SELECT 'citas', COALESCE((SELECT json_agg(row_to_json(c)) FROM (
                SELECT c2.""Codigo"" as ""codigo"", c2.""CodigoGenerado"" as ""codigoGenerado"",
                    cl.""Nombre"" as ""clienteNombre"", cl.""Telefono"" as ""clienteTelefono"",
                    b.""Nombre"" as ""barberoNombre"",
                    s.""Nombre"" as ""servicioNombre"", s.""Precio"" as ""servicioPrecio"",
                    c2.""Fecha"" as ""fecha"", to_char(c2.""Hora"", 'HH24:MI') as ""hora"",
                    c2.""Estado"" as ""estado"", c2.""FechaCreacion"" as ""fechaCreacion""
                FROM ""Citas"" c2
                JOIN ""Clientes"" cl ON c2.""ClienteId"" = cl.""Id""
                JOIN ""Barberos"" b ON c2.""BarberoId"" = b.""Id""
                JOIN ""Servicios"" s ON c2.""ServicioId"" = s.""Id""
                WHERE c2.""Estado"" IN ('Pendiente','Confirmada','Completada','Terminada')
                ORDER BY c2.""Fecha"" DESC, c2.""Hora""
            ) c), '[]'::json)
            UNION ALL
            SELECT 'cierres', COALESCE((SELECT json_agg(row_to_json(cc)) FROM (
                SELECT ""Id"" as ""id"", ""Fecha"" as ""fecha"", ""TotalRecaudado"" as ""totalRecaudado"",
                    ""TotalCitas"" as ""totalCitas"", CASE WHEN ""DetallesJson"" IS NOT NULL THEN ""DetallesJson""::json ELSE null END as ""detalles"", ""FechaCreacion"" as ""fechaCreacion""
                FROM ""CierreCaja"" ORDER BY ""Fecha"" DESC
            ) cc), '[]'::json)
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@hoy", hoy);

        await using var reader = await cmd.ExecuteReaderAsync();

        var stats = new DashboardStatsDto();
        var barberos = new List<BarberoResponseDto>();
        var servicios = new List<ServicioResponseDto>();
        var clientes = new List<ClienteResponseDto>();
        var citas = new List<CitaResponseDto>();
        var cierres = new List<CierreCajaResponseDto>();

        while (await reader.ReadAsync())
        {
            var tipo = reader.GetString(0);
            var data = reader.GetString(1);

            switch (tipo)
            {
                case "stats":
                    stats = JsonSerializer.Deserialize<DashboardStatsDto>(data, _jsonOpts) ?? new();
                    break;
                case "barberos":
                    barberos = JsonSerializer.Deserialize<List<BarberoResponseDto>>(data, _jsonOpts) ?? new();
                    break;
                case "servicios":
                    servicios = JsonSerializer.Deserialize<List<ServicioResponseDto>>(data, _jsonOpts) ?? new();
                    break;
                case "clientes":
                    clientes = JsonSerializer.Deserialize<List<ClienteResponseDto>>(data, _jsonOpts) ?? new();
                    break;
                case "citas":
                    citas = JsonSerializer.Deserialize<List<CitaResponseDto>>(data, _jsonOpts) ?? new();
                    break;
                case "cierres":
                    cierres = JsonSerializer.Deserialize<List<CierreCajaResponseDto>>(data, _jsonOpts) ?? new();
                    break;
            }
        }

        return Ok(new InitEncargadoDto
        {
            Stats = stats,
            Barberos = barberos,
            Servicios = servicios,
            Clientes = clientes,
            Citas = citas,
            CierresCaja = cierres
        });
    }

    [HttpGet("barbero")]
    [Authorize(Roles = "Barbero")]
    public async Task<ActionResult<InitBarberoDto>> GetBarbero()
    {
        var barberoId = int.Parse(User.FindFirstValue("BarberoId") ?? "0");
        if (barberoId == 0)
            return BadRequest(new { mensaje = "No se encontró el barbero asociado" });

        var hoy = DateTime.UtcNow.Date;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT 'stats', row_to_json(t) FROM (
                SELECT
                    (SELECT COUNT(*) FROM ""Citas"" WHERE ""BarberoId"" = @barberoId AND ""Fecha""::date = @hoy AND ""Estado"" != 'Inactivo') AS ""citasHoy"",
                    (SELECT COUNT(*) FROM ""Citas"" WHERE ""BarberoId"" = @barberoId AND ""Fecha""::date = @hoy AND ""Estado"" IN ('Completada','Terminada')) AS ""citasCompletadasHoy"",
                    (SELECT COUNT(*) FROM ""Citas"" WHERE ""BarberoId"" = @barberoId AND ""Estado"" != 'Inactivo') AS ""totalCitas""
            ) t
            UNION ALL
            SELECT 'citas', COALESCE((SELECT json_agg(row_to_json(c)) FROM (
                SELECT c2.""Codigo"" as ""codigo"", c2.""CodigoGenerado"" as ""codigoGenerado"",
                    cl.""Nombre"" as ""clienteNombre"", cl.""Telefono"" as ""clienteTelefono"",
                    b.""Nombre"" as ""barberoNombre"",
                    s.""Nombre"" as ""servicioNombre"", s.""Precio"" as ""servicioPrecio"",
                    c2.""Fecha"" as ""fecha"", to_char(c2.""Hora"", 'HH24:MI') as ""hora"",
                    c2.""Estado"" as ""estado"", c2.""FechaCreacion"" as ""fechaCreacion""
                FROM ""Citas"" c2
                JOIN ""Clientes"" cl ON c2.""ClienteId"" = cl.""Id""
                JOIN ""Barberos"" b ON c2.""BarberoId"" = b.""Id""
                JOIN ""Servicios"" s ON c2.""ServicioId"" = s.""Id""
                WHERE c2.""BarberoId"" = @barberoId AND c2.""Estado"" IN ('Pendiente','Confirmada','Completada','Terminada')
                ORDER BY c2.""Fecha"" DESC, c2.""Hora""
            ) c), '[]'::json)
        ";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@barberoId", barberoId);
        cmd.Parameters.AddWithValue("@hoy", hoy);

        await using var reader = await cmd.ExecuteReaderAsync();

        var stats = new DashboardStatsPersonalesDto();
        var citas = new List<CitaResponseDto>();

        while (await reader.ReadAsync())
        {
            var tipo = reader.GetString(0);
            var data = reader.GetString(1);

            switch (tipo)
            {
                case "stats":
                    stats = JsonSerializer.Deserialize<DashboardStatsPersonalesDto>(data, _jsonOpts) ?? new();
                    break;
                case "citas":
                    citas = JsonSerializer.Deserialize<List<CitaResponseDto>>(data, _jsonOpts) ?? new();
                    break;
            }
        }

        return Ok(new InitBarberoDto
        {
            Stats = stats,
            Citas = citas
        });
    }
}

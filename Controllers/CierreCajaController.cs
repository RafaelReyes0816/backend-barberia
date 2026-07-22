using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using BarberPro.Data;
using BarberPro.Dominio;
using BarberPro.DTOs.CierreCaja;

namespace BarberPro.Controllers;

[Route("api/cierre-caja")]
[ApiController]
[Authorize(Roles = "Encargado")]
public class CierreCajaController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly string _connectionString;

    public CierreCajaController(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }

    [HttpPost]
    public async Task<ActionResult> ExecuteClosing()
    {
        var hoy = DateTime.UtcNow.Date;

        var citasDelDia = await _context.Citas
            .Include(c => c.Cliente)
            .Include(c => c.Barbero)
            .Include(c => c.Servicio)
            .Where(c => c.Fecha.Date == hoy
                && (c.Estado == "Completada" || c.Estado == "Terminada"))
            .ToListAsync();

        if (!citasDelDia.Any())
            return BadRequest(new { mensaje = "No hay citas completadas o terminadas para cerrar hoy" });

        var totalRecaudado = citasDelDia.Sum(c => c.Servicio!.Precio);

        var detalles = citasDelDia
            .GroupBy(c => c.Servicio!.Nombre)
            .Select(g => new DetalleServicioDto
            {
                Servicio = g.Key,
                Cantidad = g.Count(),
                Total = g.Sum(c => c.Servicio!.Precio)
            })
            .ToList();

        var cierre = new CierreCaja
        {
            Fecha = hoy,
            TotalRecaudado = totalRecaudado,
            TotalCitas = citasDelDia.Count,
            DetallesJson = JsonSerializer.Serialize(detalles),
            FechaCreacion = DateTime.UtcNow
        };

        _context.CierreCaja.Add(cierre);

        foreach (var cita in citasDelDia)
        {
            cita.Estado = "Cerrada";
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            mensaje = "Cierre de caja ejecutado exitosamente",
            datos = new CierreCajaResponseDto
            {
                Id = cierre.Id,
                Fecha = cierre.Fecha,
                TotalRecaudado = cierre.TotalRecaudado,
                TotalCitas = cierre.TotalCitas,
                Detalles = detalles,
                FechaCreacion = cierre.FechaCreacion
            }
        });
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CierreCajaResponseDto>>> GetAll([FromQuery] int? mes, [FromQuery] int? anio)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"SELECT ""Id"", ""Fecha"", ""TotalRecaudado"", ""TotalCitas"", ""DetallesJson"", ""FechaCreacion""
                    FROM ""CierreCaja""";

        if (mes.HasValue && anio.HasValue)
            sql += @" WHERE ""Fecha""::date >= @desde AND ""Fecha""::date < @hasta";

        sql += @" ORDER BY ""Fecha"" DESC LIMIT 50";

        await using var cmd = new NpgsqlCommand(sql, conn);
        if (mes.HasValue && anio.HasValue)
        {
            var desde = new DateTime(anio.Value, mes.Value, 1);
            var hasta = desde.AddMonths(1);
            cmd.Parameters.AddWithValue("@desde", desde);
            cmd.Parameters.AddWithValue("@hasta", hasta);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        var cierres = new List<CierreCajaResponseDto>();
        while (await reader.ReadAsync())
        {
            cierres.Add(new CierreCajaResponseDto
            {
                Id = reader.GetInt32(0),
                Fecha = reader.GetDateTime(1),
                TotalRecaudado = reader.GetDecimal(2),
                TotalCitas = reader.GetInt32(3),
                Detalles = reader.IsDBNull(4) ? null
                    : JsonSerializer.Deserialize<List<DetalleServicioDto>>(reader.GetString(4)),
                FechaCreacion = reader.GetDateTime(5)
            });
        }
        return Ok(cierres);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CierreCajaResponseDto>> GetById(int id)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT ""Id"", ""Fecha"", ""TotalRecaudado"", ""TotalCitas"", ""DetallesJson"", ""FechaCreacion""
            FROM ""CierreCaja"" WHERE ""Id"" = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return NotFound(new { mensaje = "Cierre de caja no encontrado" });

        return Ok(new CierreCajaResponseDto
        {
            Id = reader.GetInt32(0),
            Fecha = reader.GetDateTime(1),
            TotalRecaudado = reader.GetDecimal(2),
            TotalCitas = reader.GetInt32(3),
            Detalles = reader.IsDBNull(4) ? null
                : JsonSerializer.Deserialize<List<DetalleServicioDto>>(reader.GetString(4)),
            FechaCreacion = reader.GetDateTime(5)
        });
    }
}

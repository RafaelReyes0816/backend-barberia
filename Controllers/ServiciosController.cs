using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using BarberPro.Data;
using BarberPro.Dominio;
using BarberPro.DTOs.Servicios;
using BarberPro.Services;

namespace BarberPro.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class ServiciosController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly CodigoService _codigoService;
    private readonly string _connectionString;

    public ServiciosController(AppDbContext context, CodigoService codigoService, IConfiguration configuration)
    {
        _context = context;
        _codigoService = codigoService;
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServicioResponseDto>>> GetAll()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT ""Codigo"", ""Nombre"", ""Precio"", ""Estado"", ""FechaCreacion""
            FROM ""Servicios""
            WHERE ""Estado"" != 'Inactivo'
            ORDER BY ""Nombre""", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var servicios = new List<ServicioResponseDto>();
        while (await reader.ReadAsync())
        {
            servicios.Add(new ServicioResponseDto
            {
                Codigo = reader.GetString(0),
                Nombre = reader.GetString(1),
                Precio = reader.GetDecimal(2),
                Estado = reader.GetString(3),
                FechaCreacion = reader.GetDateTime(4)
            });
        }
        return Ok(servicios);
    }

    [HttpGet("{codigo}")]
    public async Task<ActionResult<ServicioResponseDto>> GetByCodigo(string codigo)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT ""Codigo"", ""Nombre"", ""Precio"", ""Estado"", ""FechaCreacion""
            FROM ""Servicios""
            WHERE ""Codigo"" = @codigo AND ""Estado"" != 'Inactivo'", conn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return NotFound(new { mensaje = "Servicio no encontrado" });

        return Ok(new ServicioResponseDto
        {
            Codigo = reader.GetString(0),
            Nombre = reader.GetString(1),
            Precio = reader.GetDecimal(2),
            Estado = reader.GetString(3),
            FechaCreacion = reader.GetDateTime(4)
        });
    }

    [HttpPost]
    [Authorize(Roles = "Encargado")]
    public async Task<ActionResult<ServicioResponseDto>> Create([FromBody] ServicioRequestDto dto)
    {
        var servicio = new Servicio
        {
            Codigo = await _codigoService.GenerarCodigoServicio(),
            Nombre = dto.Nombre,
            Precio = dto.Precio,
            Estado = "Activo",
            FechaCreacion = DateTime.UtcNow
        };

        _context.Servicios.Add(servicio);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetByCodigo), new { codigo = servicio.Codigo }, new
        {
            mensaje = "Servicio creado exitosamente",
            datos = new ServicioResponseDto
            {
                Codigo = servicio.Codigo,
                Nombre = servicio.Nombre,
                Precio = servicio.Precio,
                Estado = servicio.Estado,
                FechaCreacion = servicio.FechaCreacion
            }
        });
    }

    [HttpPut("{codigo}")]
    [Authorize(Roles = "Encargado")]
    public async Task<IActionResult> Update(string codigo, [FromBody] ServicioUpdateDto dto)
    {
        var servicio = await _context.Servicios
            .FirstOrDefaultAsync(s => s.Codigo == codigo && s.Estado != "Inactivo");

        if (servicio == null)
            return NotFound(new { mensaje = "Servicio no encontrado" });

        servicio.Nombre = dto.NuevoNombre;
        servicio.Precio = dto.NuevoPrecio;
        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Servicio actualizado exitosamente" });
    }

    [HttpDelete("{codigo}")]
    [Authorize(Roles = "Encargado")]
    public async Task<IActionResult> SoftDelete(string codigo)
    {
        var servicio = await _context.Servicios
            .FirstOrDefaultAsync(s => s.Codigo == codigo && s.Estado != "Inactivo");

        if (servicio == null)
            return NotFound(new { mensaje = "Servicio no encontrado" });

        servicio.Estado = "Inactivo";
        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Servicio eliminado exitosamente" });
    }
}

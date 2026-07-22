using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using BarberPro.Data;
using BarberPro.Dominio;
using BarberPro.DTOs.Clientes;
using BarberPro.Services;

namespace BarberPro.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class ClientesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly CodigoService _codigoService;
    private readonly string _connectionString;

    public ClientesController(AppDbContext context, CodigoService codigoService, IConfiguration configuration)
    {
        _context = context;
        _codigoService = codigoService;
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClienteResponseDto>>> GetAll()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT ""Codigo"", ""Nombre"", ""Telefono"", ""Estado"", ""FechaCreacion""
            FROM ""Clientes""
            WHERE ""Estado"" != 'Inactivo'
            ORDER BY ""Nombre""", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var clientes = new List<ClienteResponseDto>();
        while (await reader.ReadAsync())
        {
            clientes.Add(new ClienteResponseDto
            {
                Codigo = reader.GetString(0),
                Nombre = reader.GetString(1),
                Telefono = reader.GetString(2),
                Estado = reader.GetString(3),
                FechaCreacion = reader.GetDateTime(4)
            });
        }
        return Ok(clientes);
    }

    [HttpGet("{codigo}")]
    public async Task<ActionResult<ClienteResponseDto>> GetByCodigo(string codigo)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT ""Codigo"", ""Nombre"", ""Telefono"", ""Estado"", ""FechaCreacion""
            FROM ""Clientes""
            WHERE ""Codigo"" = @codigo AND ""Estado"" != 'Inactivo'", conn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return NotFound(new { mensaje = "Cliente no encontrado" });

        return Ok(new ClienteResponseDto
        {
            Codigo = reader.GetString(0),
            Nombre = reader.GetString(1),
            Telefono = reader.GetString(2),
            Estado = reader.GetString(3),
            FechaCreacion = reader.GetDateTime(4)
        });
    }

    [HttpPost]
    [Authorize(Roles = "Encargado")]
    public async Task<ActionResult<ClienteResponseDto>> Create([FromBody] ClienteRequestDto dto)
    {
        var telefonoExistente = await _context.Clientes
            .FirstOrDefaultAsync(c => c.Telefono == dto.Telefono && c.Estado != "Inactivo");

        if (telefonoExistente != null)
        {
            return Ok(new
            {
                mensaje = "Ya existe un cliente con este teléfono",
                datos = new ClienteResponseDto
                {
                    Codigo = telefonoExistente.Codigo,
                    Nombre = telefonoExistente.Nombre,
                    Telefono = telefonoExistente.Telefono,
                    Estado = telefonoExistente.Estado,
                    FechaCreacion = telefonoExistente.FechaCreacion
                }
            });
        }

        var cliente = new Cliente
        {
            Codigo = await _codigoService.GenerarCodigoCliente(),
            Nombre = dto.Nombre,
            Telefono = dto.Telefono,
            Estado = "Activo",
            FechaCreacion = DateTime.UtcNow
        };

        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetByCodigo), new { codigo = cliente.Codigo }, new
        {
            mensaje = "Cliente creado exitosamente",
            datos = new ClienteResponseDto
            {
                Codigo = cliente.Codigo,
                Nombre = cliente.Nombre,
                Telefono = cliente.Telefono,
                Estado = cliente.Estado,
                FechaCreacion = cliente.FechaCreacion
            }
        });
    }

    [HttpPut("{codigo}")]
    [Authorize(Roles = "Encargado")]
    public async Task<IActionResult> Update(string codigo, [FromBody] ClienteUpdateDto dto)
    {
        var cliente = await _context.Clientes
            .FirstOrDefaultAsync(c => c.Codigo == codigo && c.Estado != "Inactivo");

        if (cliente == null)
            return NotFound(new { mensaje = "Cliente no encontrado" });

        cliente.Nombre = dto.NuevoNombre;
        cliente.Telefono = dto.NuevoTelefono;

        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Cliente actualizado exitosamente" });
    }

    [HttpDelete("{codigo}")]
    [Authorize(Roles = "Encargado")]
    public async Task<IActionResult> SoftDelete(string codigo)
    {
        var cliente = await _context.Clientes
            .FirstOrDefaultAsync(c => c.Codigo == codigo && c.Estado != "Inactivo");

        if (cliente == null)
            return NotFound(new { mensaje = "Cliente no encontrado" });

        cliente.Estado = "Inactivo";
        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Cliente eliminado exitosamente" });
    }
}

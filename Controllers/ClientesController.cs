using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    public ClientesController(AppDbContext context, CodigoService codigoService)
    {
        _context = context;
        _codigoService = codigoService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClienteResponseDto>>> GetAll()
    {
        var clientes = await _context.Clientes
            .Where(c => c.Estado != "Inactivo")
            .Select(c => new ClienteResponseDto
            {
                Codigo = c.Codigo,
                Nombre = c.Nombre,
                Telefono = c.Telefono,
                Estado = c.Estado,
                FechaCreacion = c.FechaCreacion
            })
            .ToListAsync();

        return Ok(clientes);
    }

    [HttpGet("{codigo}")]
    public async Task<ActionResult<ClienteResponseDto>> GetByCodigo(string codigo)
    {
        var cliente = await _context.Clientes
            .Where(c => c.Codigo == codigo && c.Estado != "Inactivo")
            .Select(c => new ClienteResponseDto
            {
                Codigo = c.Codigo,
                Nombre = c.Nombre,
                Telefono = c.Telefono,
                Estado = c.Estado,
                FechaCreacion = c.FechaCreacion
            })
            .FirstOrDefaultAsync();

        if (cliente == null)
            return NotFound(new { mensaje = "Cliente no encontrado" });

        return Ok(cliente);
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

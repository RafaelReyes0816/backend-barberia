using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BarberPro.Data;
using BarberPro.Dominio;
using BarberPro.DTOs.Barberos;
using BarberPro.Services;

namespace BarberPro.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class BarberosController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly CodigoService _codigoService;

    public BarberosController(AppDbContext context, CodigoService codigoService)
    {
        _context = context;
        _codigoService = codigoService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BarberoResponseDto>>> GetAll()
    {
        var barberos = await _context.Barberos
            .Where(b => b.Estado != "Inactivo")
            .Select(b => new BarberoResponseDto
            {
                Codigo = b.Codigo,
                Nombre = b.Nombre,
                Estado = b.Estado,
                FechaCreacion = b.FechaCreacion
            })
            .ToListAsync();

        return Ok(barberos);
    }

    [HttpGet("{codigo}")]
    public async Task<ActionResult<BarberoResponseDto>> GetByCodigo(string codigo)
    {
        var barbero = await _context.Barberos
            .Where(b => b.Codigo == codigo && b.Estado != "Inactivo")
            .Select(b => new BarberoResponseDto
            {
                Codigo = b.Codigo,
                Nombre = b.Nombre,
                Estado = b.Estado,
                FechaCreacion = b.FechaCreacion
            })
            .FirstOrDefaultAsync();

        if (barbero == null)
            return NotFound(new { mensaje = "Barbero no encontrado" });

        return Ok(barbero);
    }

    [HttpPost]
    [Authorize(Roles = "Encargado")]
    public async Task<ActionResult<BarberoResponseDto>> Create([FromBody] BarberoRequestDto dto)
    {
        var barbero = new Barbero
        {
            Codigo = await _codigoService.GenerarCodigoBarbero(),
            Nombre = dto.Nombre,
            Estado = "Activo",
            FechaCreacion = DateTime.UtcNow
        };

        _context.Barberos.Add(barbero);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetByCodigo), new { codigo = barbero.Codigo }, new
        {
            mensaje = "Barbero creado exitosamente",
            datos = new BarberoResponseDto
            {
                Codigo = barbero.Codigo,
                Nombre = barbero.Nombre,
                Estado = barbero.Estado,
                FechaCreacion = barbero.FechaCreacion
            }
        });
    }

    [HttpPut("{codigo}")]
    [Authorize(Roles = "Encargado")]
    public async Task<IActionResult> Update(string codigo, [FromBody] BarberoUpdateDto dto)
    {
        var barbero = await _context.Barberos
            .FirstOrDefaultAsync(b => b.Codigo == codigo && b.Estado != "Inactivo");

        if (barbero == null)
            return NotFound(new { mensaje = "Barbero no encontrado" });

        barbero.Nombre = dto.NuevoNombre;
        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Barbero actualizado exitosamente" });
    }

    [HttpDelete("{codigo}")]
    [Authorize(Roles = "Encargado")]
    public async Task<IActionResult> SoftDelete(string codigo)
    {
        var barbero = await _context.Barberos
            .FirstOrDefaultAsync(b => b.Codigo == codigo && b.Estado != "Inactivo");

        if (barbero == null)
            return NotFound(new { mensaje = "Barbero no encontrado" });

        barbero.Estado = "Inactivo";
        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Barbero eliminado exitosamente" });
    }
}

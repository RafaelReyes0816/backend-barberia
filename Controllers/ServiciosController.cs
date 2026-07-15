using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    public ServiciosController(AppDbContext context, CodigoService codigoService)
    {
        _context = context;
        _codigoService = codigoService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServicioResponseDto>>> GetAll()
    {
        var servicios = await _context.Servicios
            .Where(s => s.Estado != "Inactivo")
            .Select(s => new ServicioResponseDto
            {
                Codigo = s.Codigo,
                Nombre = s.Nombre,
                Precio = s.Precio,
                Estado = s.Estado,
                FechaCreacion = s.FechaCreacion
            })
            .ToListAsync();

        return Ok(servicios);
    }

    [HttpGet("{codigo}")]
    public async Task<ActionResult<ServicioResponseDto>> GetByCodigo(string codigo)
    {
        var servicio = await _context.Servicios
            .Where(s => s.Codigo == codigo && s.Estado != "Inactivo")
            .Select(s => new ServicioResponseDto
            {
                Codigo = s.Codigo,
                Nombre = s.Nombre,
                Precio = s.Precio,
                Estado = s.Estado,
                FechaCreacion = s.FechaCreacion
            })
            .FirstOrDefaultAsync();

        if (servicio == null)
            return NotFound(new { mensaje = "Servicio no encontrado" });

        return Ok(servicio);
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

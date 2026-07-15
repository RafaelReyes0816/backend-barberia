using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    public CierreCajaController(AppDbContext context)
    {
        _context = context;
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
        var query = _context.CierreCaja.AsQueryable();

        if (mes.HasValue && anio.HasValue)
        {
            query = query.Where(c => c.Fecha.Month == mes.Value && c.Fecha.Year == anio.Value);
        }

        var cierresRaw = await query
            .OrderByDescending(c => c.Fecha)
            .ToListAsync();

        var cierres = cierresRaw.Select(c => new CierreCajaResponseDto
        {
            Id = c.Id,
            Fecha = c.Fecha,
            TotalRecaudado = c.TotalRecaudado,
            TotalCitas = c.TotalCitas,
            Detalles = c.DetallesJson != null
                ? JsonSerializer.Deserialize<List<DetalleServicioDto>>(c.DetallesJson)
                : null,
            FechaCreacion = c.FechaCreacion
        }).ToList();

        return Ok(cierres);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CierreCajaResponseDto>> GetById(int id)
    {
        var cierreRaw = await _context.CierreCaja
            .FirstOrDefaultAsync(c => c.Id == id);

        if (cierreRaw == null)
            return NotFound(new { mensaje = "Cierre de caja no encontrado" });

        var cierre = new CierreCajaResponseDto
        {
            Id = cierreRaw.Id,
            Fecha = cierreRaw.Fecha,
            TotalRecaudado = cierreRaw.TotalRecaudado,
            TotalCitas = cierreRaw.TotalCitas,
            Detalles = cierreRaw.DetallesJson != null
                ? JsonSerializer.Deserialize<List<DetalleServicioDto>>(cierreRaw.DetallesJson)
                : null,
            FechaCreacion = cierreRaw.FechaCreacion
        };

        return Ok(cierre);
    }
}

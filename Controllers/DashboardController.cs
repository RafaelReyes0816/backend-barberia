using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BarberPro.Data;
using BarberPro.DTOs.Dashboard;

namespace BarberPro.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _context;

    public DashboardController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("stats")]
    [Authorize(Roles = "Encargado")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats()
    {
        var hoy = DateTime.UtcNow.Date;

        var stats = new DashboardStatsDto
        {
            CitasPendientes = await _context.Citas.CountAsync(c => c.Estado == "Pendiente"),
            CitasConfirmadas = await _context.Citas.CountAsync(c => c.Estado == "Confirmada"),
            CitasCompletadas = await _context.Citas.CountAsync(c => (c.Estado == "Completada" || c.Estado == "Terminada") && c.Fecha.Date == hoy),
            CitasHoy = await _context.Citas.CountAsync(c => c.Fecha.Date == hoy && c.Estado != "Inactivo"),
            TotalRecaudadoHoy = await _context.Citas
                .Where(c => c.Fecha.Date == hoy && (c.Estado == "Completada" || c.Estado == "Terminada"))
                .Join(_context.Servicios, c => c.ServicioId, s => s.Id, (c, s) => s.Precio)
                .SumAsync(p => p)
        };

        return Ok(stats);
    }

    [HttpGet("stats-personales")]
    [Authorize(Roles = "Barbero")]
    public async Task<ActionResult<DashboardStatsPersonalesDto>> GetStatsPersonales()
    {
        var barberoId = int.Parse(User.FindFirstValue("BarberoId") ?? "0");
        var hoy = DateTime.UtcNow.Date;

        if (barberoId == 0)
            return BadRequest(new { mensaje = "No se encontró el barbero asociado" });

        var stats = new DashboardStatsPersonalesDto
        {
            CitasHoy = await _context.Citas.CountAsync(c => c.BarberoId == barberoId
                && c.Fecha.Date == hoy && c.Estado != "Inactivo"),
            CitasCompletadasHoy = await _context.Citas.CountAsync(c => c.BarberoId == barberoId
                && c.Fecha.Date == hoy
                && (c.Estado == "Completada" || c.Estado == "Terminada")),
            TotalCitas = await _context.Citas.CountAsync(c => c.BarberoId == barberoId && c.Estado != "Inactivo")
        };

        return Ok(stats);
    }

    [HttpGet("buscar")]
    [Authorize(Roles = "Encargado")]
    public async Task<ActionResult<IEnumerable<BuscarCitaDto>>> Buscar([FromQuery] string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return Ok(new List<BuscarCitaDto>());

        var citas = await _context.Citas
            .Include(c => c.Cliente)
            .Include(c => c.Barbero)
            .Include(c => c.Servicio)
            .Where(c => c.Cliente!.Nombre.ToLower().Contains(nombre.ToLower())
                && c.Estado != "Inactivo")
            .OrderByDescending(c => c.Fecha)
            .Select(c => new BuscarCitaDto
            {
                CodigoGenerado = c.CodigoGenerado,
                ClienteNombre = c.Cliente!.Nombre,
                ClienteTelefono = c.Cliente!.Telefono,
                BarberoNombre = c.Barbero!.Nombre,
                ServicioNombre = c.Servicio!.Nombre,
                ServicioPrecio = c.Servicio!.Precio,
                Fecha = c.Fecha,
                Hora = c.Hora.ToString(@"hh\:mm"),
                Estado = c.Estado
            })
            .ToListAsync();

        return Ok(citas);
    }
}

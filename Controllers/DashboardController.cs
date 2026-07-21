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

        var pendientesTask = _context.Citas.CountAsync(c => c.Estado == "Pendiente");
        var confirmadasTask = _context.Citas.CountAsync(c => c.Estado == "Confirmada");
        var completadasTask = _context.Citas.CountAsync(c => (c.Estado == "Completada" || c.Estado == "Terminada") && c.Fecha.Date == hoy);
        var hoyTask = _context.Citas.CountAsync(c => c.Fecha.Date == hoy && c.Estado != "Inactivo");
        var recaudadoTask = _context.Citas
            .Where(c => c.Fecha.Date == hoy && (c.Estado == "Completada" || c.Estado == "Terminada"))
            .Join(_context.Servicios,
                c => c.ServicioId,
                s => s.Id,
                (c, s) => s.Precio)
            .SumAsync(p => p);

        await Task.WhenAll(pendientesTask, confirmadasTask, completadasTask, hoyTask, recaudadoTask);

        var stats = new DashboardStatsDto
        {
            CitasPendientes = pendientesTask.Result,
            CitasConfirmadas = confirmadasTask.Result,
            CitasCompletadas = completadasTask.Result,
            CitasHoy = hoyTask.Result,
            TotalRecaudadoHoy = recaudadoTask.Result
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

        var hoyTask = _context.Citas.CountAsync(c => c.BarberoId == barberoId
                && c.Fecha.Date == hoy && c.Estado != "Inactivo");
        var completadasTask = _context.Citas.CountAsync(c => c.BarberoId == barberoId
                && c.Fecha.Date == hoy
                && (c.Estado == "Completada" || c.Estado == "Terminada"));
        var totalTask = _context.Citas.CountAsync(c => c.BarberoId == barberoId && c.Estado != "Inactivo");

        await Task.WhenAll(hoyTask, completadasTask, totalTask);

        var stats = new DashboardStatsPersonalesDto
        {
            CitasHoy = hoyTask.Result,
            CitasCompletadasHoy = completadasTask.Result,
            TotalCitas = totalTask.Result
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

using System.Security.Claims;
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

namespace BarberPro.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class InitController : ControllerBase
{
    private readonly AppDbContext _context;

    public InitController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("encargado")]
    [Authorize(Roles = "Encargado")]
    public async Task<ActionResult<InitEncargadoDto>> GetEncargado()
    {
        var hoy = DateTime.UtcNow.Date;

        var statsTask = GetStatsHoy(hoy);
        var barberosTask = GetBarberos();
        var serviciosTask = GetServicios();
        var clientesTask = GetClientes();
        var citasTask = GetCitas();
        var cierresTask = GetCierresCaja();

        await Task.WhenAll(statsTask, barberosTask, serviciosTask, clientesTask, citasTask, cierresTask);

        return Ok(new InitEncargadoDto
        {
            Stats = statsTask.Result,
            Barberos = barberosTask.Result,
            Servicios = serviciosTask.Result,
            Clientes = clientesTask.Result,
            Citas = citasTask.Result,
            CierresCaja = cierresTask.Result
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

        var statsTask = _context.Citas
            .Where(c => c.BarberoId == barberoId)
            .GroupBy(c => 1)
            .Select(g => new DashboardStatsPersonalesDto
            {
                CitasHoy = g.Count(c => c.Fecha.Date == hoy && c.Estado != "Inactivo"),
                CitasCompletadasHoy = g.Count(c => c.Fecha.Date == hoy
                    && (c.Estado == "Completada" || c.Estado == "Terminada")),
                TotalCitas = g.Count(c => c.Estado != "Inactivo")
            })
            .FirstOrDefaultAsync() ?? Task.FromResult(new DashboardStatsPersonalesDto());

        var citasTask = _context.Citas
            .Include(c => c.Cliente)
            .Include(c => c.Barbero)
            .Include(c => c.Servicio)
            .Where(c => c.BarberoId == barberoId && c.Estado != "Inactivo")
            .OrderByDescending(c => c.Fecha)
            .ThenBy(c => c.Hora)
            .Select(c => new CitaResponseDto
            {
                Codigo = c.Codigo,
                CodigoGenerado = c.CodigoGenerado,
                ClienteNombre = c.Cliente!.Nombre,
                ClienteTelefono = c.Cliente!.Telefono,
                BarberoNombre = c.Barbero!.Nombre,
                ServicioNombre = c.Servicio!.Nombre,
                ServicioPrecio = c.Servicio!.Precio,
                Fecha = c.Fecha,
                Hora = c.Hora.ToString(@"hh\:mm"),
                Estado = c.Estado,
                FechaCreacion = c.FechaCreacion
            })
            .ToListAsync();

        await Task.WhenAll(statsTask, citasTask);

        return Ok(new InitBarberoDto
        {
            Stats = statsTask.Result,
            Citas = citasTask.Result
        });
    }

    private Task<DashboardStatsDto> GetStatsHoy(DateTime hoy)
    {
        var pendientesTask = _context.Citas.CountAsync(c => c.Estado == "Pendiente");
        var confirmadasTask = _context.Citas.CountAsync(c => c.Estado == "Confirmada");
        var completadasTask = _context.Citas.CountAsync(c => (c.Estado == "Completada" || c.Estado == "Terminada") && c.Fecha.Date == hoy);
        var hoyTask = _context.Citas.CountAsync(c => c.Fecha.Date == hoy && c.Estado != "Inactivo");
        var recaudadoTask = _context.Citas
            .Where(c => c.Fecha.Date == hoy && (c.Estado == "Completada" || c.Estado == "Terminada"))
            .Join(_context.Servicios, c => c.ServicioId, s => s.Id, (c, s) => s.Precio)
            .SumAsync(p => p);

        return Task.WhenAll(pendientesTask, confirmadasTask, completadasTask, hoyTask, recaudadoTask)
            .ContinueWith(t => new DashboardStatsDto
            {
                CitasPendientes = pendientesTask.Result,
                CitasConfirmadas = confirmadasTask.Result,
                CitasCompletadas = completadasTask.Result,
                CitasHoy = hoyTask.Result,
                TotalRecaudadoHoy = recaudadoTask.Result
            });
    }

    private Task<List<BarberoResponseDto>> GetBarberos()
    {
        return _context.Barberos
            .Where(b => b.Estado != "Inactivo")
            .OrderBy(b => b.Nombre)
            .Select(b => new BarberoResponseDto
            {
                Codigo = b.Codigo,
                Nombre = b.Nombre,
                Estado = b.Estado,
                FechaCreacion = b.FechaCreacion
            })
            .ToListAsync();
    }

    private Task<List<ServicioResponseDto>> GetServicios()
    {
        return _context.Servicios
            .Where(s => s.Estado != "Inactivo")
            .OrderBy(s => s.Nombre)
            .Select(s => new ServicioResponseDto
            {
                Codigo = s.Codigo,
                Nombre = s.Nombre,
                Precio = s.Precio,
                Estado = s.Estado,
                FechaCreacion = s.FechaCreacion
            })
            .ToListAsync();
    }

    private Task<List<ClienteResponseDto>> GetClientes()
    {
        return _context.Clientes
            .Where(c => c.Estado != "Inactivo")
            .OrderBy(c => c.Nombre)
            .Select(c => new ClienteResponseDto
            {
                Codigo = c.Codigo,
                Nombre = c.Nombre,
                Telefono = c.Telefono,
                Estado = c.Estado,
                FechaCreacion = c.FechaCreacion
            })
            .ToListAsync();
    }

    private Task<List<CitaResponseDto>> GetCitas()
    {
        return _context.Citas
            .Include(c => c.Cliente)
            .Include(c => c.Barbero)
            .Include(c => c.Servicio)
            .Where(c => c.Estado != "Inactivo")
            .OrderByDescending(c => c.Fecha)
            .ThenBy(c => c.Hora)
            .Select(c => new CitaResponseDto
            {
                Codigo = c.Codigo,
                CodigoGenerado = c.CodigoGenerado,
                ClienteNombre = c.Cliente!.Nombre,
                ClienteTelefono = c.Cliente!.Telefono,
                BarberoNombre = c.Barbero!.Nombre,
                ServicioNombre = c.Servicio!.Nombre,
                ServicioPrecio = c.Servicio!.Precio,
                Fecha = c.Fecha,
                Hora = c.Hora.ToString(@"hh\:mm"),
                Estado = c.Estado,
                FechaCreacion = c.FechaCreacion
            })
            .ToListAsync();
    }

    private Task<List<CierreCajaResponseDto>> GetCierresCaja()
    {
        return _context.CierreCaja
            .OrderByDescending(cc => cc.Fecha)
            .Select(cc => new CierreCajaResponseDto
            {
                Id = cc.Id,
                Fecha = cc.Fecha,
                TotalRecaudado = cc.TotalRecaudado,
                TotalCitas = cc.TotalCitas,
                Detalles = null,
                FechaCreacion = cc.FechaCreacion
            })
            .ToListAsync();
    }
}

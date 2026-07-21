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

        var stats = await GetStatsHoy(hoy);
        var barberos = await GetBarberos();
        var servicios = await GetServicios();
        var clientes = await GetClientes();
        var citas = await GetCitas();
        var cierres = await GetCierresCaja();

        return Ok(new InitEncargadoDto
        {
            Stats = stats,
            Barberos = barberos,
            Servicios = servicios,
            Clientes = clientes,
            Citas = citas,
            CierresCaja = cierres
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

        var stats = await GetStatsBarbero(barberoId, hoy);
        var citas = await GetCitasBarbero(barberoId);

        return Ok(new InitBarberoDto
        {
            Stats = stats,
            Citas = citas
        });
    }

    private async Task<DashboardStatsDto> GetStatsHoy(DateTime hoy)
    {
        var pendientes = await _context.Citas.CountAsync(c => c.Estado == "Pendiente");
        var confirmadas = await _context.Citas.CountAsync(c => c.Estado == "Confirmada");
        var completadas = await _context.Citas.CountAsync(c => (c.Estado == "Completada" || c.Estado == "Terminada") && c.Fecha.Date == hoy);
        var totalHoy = await _context.Citas.CountAsync(c => c.Fecha.Date == hoy && c.Estado != "Inactivo");
        var recaudado = await _context.Citas
            .Where(c => c.Fecha.Date == hoy && (c.Estado == "Completada" || c.Estado == "Terminada"))
            .Join(_context.Servicios, c => c.ServicioId, s => s.Id, (c, s) => s.Precio)
            .SumAsync(p => p);

        return new DashboardStatsDto
        {
            CitasPendientes = pendientes,
            CitasConfirmadas = confirmadas,
            CitasCompletadas = completadas,
            CitasHoy = totalHoy,
            TotalRecaudadoHoy = recaudado
        };
    }

    private async Task<DashboardStatsPersonalesDto> GetStatsBarbero(int barberoId, DateTime hoy)
    {
        var totalHoy = await _context.Citas.CountAsync(c => c.BarberoId == barberoId
            && c.Fecha.Date == hoy && c.Estado != "Inactivo");
        var completadasHoy = await _context.Citas.CountAsync(c => c.BarberoId == barberoId
            && c.Fecha.Date == hoy && (c.Estado == "Completada" || c.Estado == "Terminada"));
        var total = await _context.Citas.CountAsync(c => c.BarberoId == barberoId && c.Estado != "Inactivo");

        return new DashboardStatsPersonalesDto
        {
            CitasHoy = totalHoy,
            CitasCompletadasHoy = completadasHoy,
            TotalCitas = total
        };
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

    private Task<List<CitaResponseDto>> GetCitasBarbero(int barberoId)
    {
        return _context.Citas
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

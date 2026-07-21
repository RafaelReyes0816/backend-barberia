using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BarberPro.Data;
using BarberPro.Dominio;
using BarberPro.DTOs.Citas;
using BarberPro.Services;

namespace BarberPro.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class CitasController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly CodigoService _codigoService;

    public CitasController(AppDbContext context, CodigoService codigoService)
    {
        _context = context;
        _codigoService = codigoService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CitaResponseDto>>> GetAll()
    {
        var citas = await _context.Citas
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

        return Ok(citas);
    }

    [HttpGet("mis-citas")]
    [Authorize(Roles = "Barbero")]
    public async Task<ActionResult<IEnumerable<CitaResponseDto>>> GetMisCitas()
    {
        var barberoId = int.Parse(User.FindFirstValue("BarberoId") ?? "0");

        if (barberoId == 0)
            return BadRequest(new { mensaje = "No se encontró el barbero asociado" });

        var citas = await _context.Citas
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

        return Ok(citas);
    }

    [HttpGet("{codigo}")]
    public async Task<ActionResult<CitaResponseDto>> GetByCodigo(string codigo)
    {
        var cita = await _context.Citas
            .Include(c => c.Cliente)
            .Include(c => c.Barbero)
            .Include(c => c.Servicio)
            .Where(c => c.Codigo == codigo && c.Estado != "Inactivo")
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
            .FirstOrDefaultAsync();

        if (cita == null)
            return NotFound(new { mensaje = "Cita no encontrada" });

        return Ok(cita);
    }

    [HttpPost]
    [Authorize(Roles = "Encargado")]
    public async Task<ActionResult> Create([FromBody] CitaRequestDto dto)
    {
        var clienteTask = _context.Clientes
            .FirstOrDefaultAsync(c => c.Codigo == dto.ClienteCodigo && c.Estado != "Inactivo");
        var barberoTask = _context.Barberos
            .FirstOrDefaultAsync(b => b.Codigo == dto.BarberoCodigo && b.Estado != "Inactivo");
        var servicioTask = _context.Servicios
            .FirstOrDefaultAsync(s => s.Codigo == dto.ServicioCodigo && s.Estado != "Inactivo");

        await Task.WhenAll(clienteTask, barberoTask, servicioTask);

        var cliente = clienteTask.Result;
        if (cliente == null)
            return BadRequest(new { mensaje = "Cliente no encontrado" });

        var barbero = barberoTask.Result;
        if (barbero == null)
            return BadRequest(new { mensaje = "Barbero no encontrado" });

        var servicio = servicioTask.Result;
        if (servicio == null)
            return BadRequest(new { mensaje = "Servicio no encontrado" });

        if (!TimeSpan.TryParse(dto.Hora, out var hora))
            return BadRequest(new { mensaje = "Formato de hora inválido (usar HH:mm)" });

        var dobleReservaTask = _context.Citas
            .AnyAsync(c => c.BarberoId == barbero.Id
                && c.Fecha.Date == dto.Fecha.Date
                && c.Hora == hora
                && c.Estado != "Cancelada"
                && c.Estado != "Cerrada");

        var codigoCitaTask = _codigoService.GenerarCodigoCita();
        var codigoGeneradoTask = _codigoService.GenerarCodigoGenerado(dto.Fecha);

        await Task.WhenAll(dobleReservaTask, codigoCitaTask, codigoGeneradoTask);

        if (dobleReservaTask.Result)
            return BadRequest(new { mensaje = "El barbero ya tiene una cita en esa fecha y hora" });

        var cita = new Cita
        {
            Codigo = codigoCitaTask.Result,
            ClienteId = cliente.Id,
            BarberoId = barbero.Id,
            ServicioId = servicio.Id,
            Fecha = dto.Fecha.Date,
            Hora = hora,
            Estado = "Pendiente",
            CodigoGenerado = codigoGeneradoTask.Result,
            FechaCreacion = DateTime.UtcNow
        };

        _context.Citas.Add(cita);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            mensaje = "Cita creada exitosamente",
            datos = new CitaResponseDto
            {
                Codigo = cita.Codigo,
                CodigoGenerado = cita.CodigoGenerado,
                ClienteNombre = cliente.Nombre,
                ClienteTelefono = cliente.Telefono,
                BarberoNombre = barbero.Nombre,
                ServicioNombre = servicio.Nombre,
                ServicioPrecio = servicio.Precio,
                Fecha = cita.Fecha,
                Hora = cita.Hora.ToString(@"hh\:mm"),
                Estado = cita.Estado,
                FechaCreacion = cita.FechaCreacion
            }
        });
    }

    [HttpPut("{codigo}")]
    [Authorize(Roles = "Encargado")]
    public async Task<IActionResult> Update(string codigo, [FromBody] CitaUpdateDto dto)
    {
        var cita = await _context.Citas
            .FirstOrDefaultAsync(c => c.Codigo == codigo && c.Estado != "Inactivo");

        if (cita == null)
            return NotFound(new { mensaje = "Cita no encontrada" });

        if (cita.Estado != "Pendiente" && cita.Estado != "Confirmada")
            return BadRequest(new { mensaje = "Solo se pueden editar citas pendientes o confirmadas" });

        if (!TimeSpan.TryParse(dto.NuevaHora, out var nuevaHora))
            return BadRequest(new { mensaje = "Formato de hora inválido" });

        var dobleReserva = await _context.Citas
            .AnyAsync(c => c.BarberoId == cita.BarberoId
                && c.Fecha.Date == dto.NuevaFecha.Date
                && c.Hora == nuevaHora
                && c.Id != cita.Id
                && c.Estado != "Cancelada"
                && c.Estado != "Cerrada");

        if (dobleReserva)
            return BadRequest(new { mensaje = "El barbero ya tiene una cita en esa fecha y hora" });

        cita.Fecha = dto.NuevaFecha.Date;
        cita.Hora = nuevaHora;

        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Cita actualizada exitosamente" });
    }

    [HttpPut("{codigo}/status")]
    public async Task<IActionResult> UpdateStatus(string codigo, [FromBody] CitaStatusDto dto)
    {
        var cita = await _context.Citas
            .Include(c => c.Barbero)
            .FirstOrDefaultAsync(c => c.Codigo == codigo && c.Estado != "Inactivo");

        if (cita == null)
            return NotFound(new { mensaje = "Cita no encontrada" });

        var estadosPermitidos = new[] { "Pendiente", "Confirmada", "Completada", "Terminada", "Cancelada", "Cerrada" };
        if (!estadosPermitidos.Contains(dto.Estado))
            return BadRequest(new { mensaje = $"Estado no válido. Estados permitidos: {string.Join(", ", estadosPermitidos)}" });

        if (User.IsInRole("Barbero"))
        {
            var barberoId = int.Parse(User.FindFirstValue("BarberoId") ?? "0");
            if (cita.BarberoId != barberoId)
                return Forbid();

            if (dto.Estado != "Completada" && dto.Estado != "Cancelada")
                return BadRequest(new { mensaje = "Los barberos solo pueden marcar como completada o cancelada" });
        }

        cita.Estado = dto.Estado;
        await _context.SaveChangesAsync();

        return Ok(new { mensaje = $"Cita actualizada a estado: {dto.Estado}" });
    }
}

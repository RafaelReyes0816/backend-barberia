using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    private readonly string _connectionString;

    public CitasController(AppDbContext context, CodigoService codigoService, IConfiguration configuration)
    {
        _context = context;
        _codigoService = codigoService;
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }

    private async Task<List<CitaResponseDto>> FetchCitas(string where, params NpgsqlParameter[] parameters)
    {
        var sql = $@"
            SELECT c.""Codigo"", c.""CodigoGenerado"",
                cl.""Nombre"", cl.""Telefono"",
                b.""Nombre"",
                s.""Nombre"", s.""Precio"",
                c.""Fecha"", to_char(c.""Hora"", 'HH24:MI'),
                c.""Estado"", c.""FechaCreacion""
            FROM ""Citas"" c
            JOIN ""Clientes"" cl ON c.""ClienteId"" = cl.""Id""
            JOIN ""Barberos"" b ON c.""BarberoId"" = b.""Id""
            JOIN ""Servicios"" s ON c.""ServicioId"" = s.""Id""
            WHERE {where}
            ORDER BY c.""Fecha"" DESC, c.""Hora""";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters) cmd.Parameters.Add(p);
        await using var reader = await cmd.ExecuteReaderAsync();

        var citas = new List<CitaResponseDto>();
        while (await reader.ReadAsync())
        {
            citas.Add(new CitaResponseDto
            {
                Codigo = reader.GetString(0),
                CodigoGenerado = reader.GetString(1),
                ClienteNombre = reader.GetString(2),
                ClienteTelefono = reader.GetString(3),
                BarberoNombre = reader.GetString(4),
                ServicioNombre = reader.GetString(5),
                ServicioPrecio = reader.GetDecimal(6),
                Fecha = reader.GetDateTime(7),
                Hora = reader.GetString(8),
                Estado = reader.GetString(9),
                FechaCreacion = reader.GetDateTime(10)
            });
        }
        return citas;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CitaResponseDto>>> GetAll()
    {
        return Ok(await FetchCitas("c.\"Estado\" != 'Inactivo'"));
    }

    [HttpGet("mis-citas")]
    [Authorize(Roles = "Barbero")]
    public async Task<ActionResult<IEnumerable<CitaResponseDto>>> GetMisCitas()
    {
        var barberoId = int.Parse(User.FindFirstValue("BarberoId") ?? "0");
        if (barberoId == 0)
            return BadRequest(new { mensaje = "No se encontró el barbero asociado" });

        return Ok(await FetchCitas(
            "c.\"BarberoId\" = @barberoId AND c.\"Estado\" != 'Inactivo'",
            new NpgsqlParameter("@barberoId", barberoId)));
    }

    [HttpGet("{codigo}")]
    public async Task<ActionResult<CitaResponseDto>> GetByCodigo(string codigo)
    {
        var citas = await FetchCitas(
            "c.\"Codigo\" = @codigo AND c.\"Estado\" != 'Inactivo'",
            new NpgsqlParameter("@codigo", codigo));

        if (citas.Count == 0)
            return NotFound(new { mensaje = "Cita no encontrada" });

        return Ok(citas[0]);
    }

    [HttpPost]
    [Authorize(Roles = "Encargado")]
    public async Task<ActionResult> Create([FromBody] CitaRequestDto dto)
    {
        var cliente = await _context.Clientes
            .FirstOrDefaultAsync(c => c.Codigo == dto.ClienteCodigo && c.Estado != "Inactivo");
        if (cliente == null)
            return BadRequest(new { mensaje = "Cliente no encontrado" });

        var barbero = await _context.Barberos
            .FirstOrDefaultAsync(b => b.Codigo == dto.BarberoCodigo && b.Estado != "Inactivo");
        if (barbero == null)
            return BadRequest(new { mensaje = "Barbero no encontrado" });

        var servicio = await _context.Servicios
            .FirstOrDefaultAsync(s => s.Codigo == dto.ServicioCodigo && s.Estado != "Inactivo");
        if (servicio == null)
            return BadRequest(new { mensaje = "Servicio no encontrado" });

        if (!TimeSpan.TryParse(dto.Hora, out var hora))
            return BadRequest(new { mensaje = "Formato de hora inválido (usar HH:mm)" });

        var dobleReserva = await _context.Citas
            .AnyAsync(c => c.BarberoId == barbero.Id
                && c.Fecha.Date == dto.Fecha.Date
                && c.Hora == hora
                && c.Estado != "Cancelada"
                && c.Estado != "Cerrada");

        if (dobleReserva)
            return BadRequest(new { mensaje = "El barbero ya tiene una cita en esa fecha y hora" });

        var codigo = await _codigoService.GenerarCodigoCita();
        var codigoGenerado = await _codigoService.GenerarCodigoGenerado(dto.Fecha);

        var cita = new Cita
        {
            Codigo = codigo,
            ClienteId = cliente.Id,
            BarberoId = barbero.Id,
            ServicioId = servicio.Id,
            Fecha = dto.Fecha.Date,
            Hora = hora,
            Estado = "Pendiente",
            CodigoGenerado = codigoGenerado,
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

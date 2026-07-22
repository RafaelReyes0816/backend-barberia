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
                Email = _context.Usuarios
                    .Where(u => u.BarberoId == b.Id && u.Estado == "Activo")
                    .Select(u => u.Email)
                    .FirstOrDefault(),
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
                Email = _context.Usuarios
                    .Where(u => u.BarberoId == b.Id && u.Estado == "Activo")
                    .Select(u => u.Email)
                    .FirstOrDefault(),
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
        if (!string.IsNullOrEmpty(dto.Email) && string.IsNullOrEmpty(dto.Password))
            return BadRequest(new { mensaje = "La contraseña es requerida cuando se provee un email" });

        if (!string.IsNullOrEmpty(dto.Password) && string.IsNullOrEmpty(dto.Email))
            return BadRequest(new { mensaje = "El email es requerido cuando se provee una contraseña" });

        if (!string.IsNullOrEmpty(dto.Email))
        {
            var emailExiste = await _context.Usuarios
                .AnyAsync(u => u.Email == dto.Email.ToLower() && u.Estado == "Activo");
            if (emailExiste)
                return BadRequest(new { mensaje = "Ya existe un usuario con este email" });
        }

        var barbero = new Barbero
        {
            Codigo = await _codigoService.GenerarCodigoBarbero(),
            Nombre = dto.Nombre,
            Estado = "Activo",
            FechaCreacion = DateTime.UtcNow
        };

        _context.Barberos.Add(barbero);
        await _context.SaveChangesAsync();

        if (!string.IsNullOrEmpty(dto.Email) && !string.IsNullOrEmpty(dto.Password))
        {
            var usuario = new Usuario
            {
                Nombre = dto.Nombre,
                Email = dto.Email.ToLower(),
                PasswordHash = PasswordService.HashPassword(dto.Password),
                Rol = "Barbero",
                BarberoId = barbero.Id,
                Estado = "Activo",
                FechaCreacion = DateTime.UtcNow
            };

            _context.Usuarios.Add(usuario);
            await _context.SaveChangesAsync();
        }

        return CreatedAtAction(nameof(GetByCodigo), new { codigo = barbero.Codigo }, new
        {
            mensaje = "Barbero creado exitosamente",
            datos = new BarberoResponseDto
            {
                Codigo = barbero.Codigo,
                Nombre = barbero.Nombre,
                Email = dto.Email?.ToLower(),
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

        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.BarberoId == barbero.Id && u.Estado == "Activo");
        if (usuario != null)
            usuario.Nombre = dto.NuevoNombre;

        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Barbero actualizado exitosamente" });
    }

    [HttpPost("{codigo}/credenciales")]
    [Authorize(Roles = "Encargado")]
    public async Task<IActionResult> AsignarCredenciales(string codigo, [FromBody] BarberoCredencialesDto dto)
    {
        var barbero = await _context.Barberos
            .FirstOrDefaultAsync(b => b.Codigo == codigo && b.Estado != "Inactivo");

        if (barbero == null)
            return NotFound(new { mensaje = "Barbero no encontrado" });

        var emailExiste = await _context.Usuarios
            .AnyAsync(u => u.Email == dto.Email.ToLower() && u.Estado == "Activo"
                && u.BarberoId != barbero.Id);
        if (emailExiste)
            return BadRequest(new { mensaje = "Ya existe otro usuario con este email" });

        var usuarioExistente = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.BarberoId == barbero.Id && u.Estado == "Activo");

        if (usuarioExistente != null)
        {
            usuarioExistente.Email = dto.Email.ToLower();
            usuarioExistente.PasswordHash = PasswordService.HashPassword(dto.Password);
        }
        else
        {
            var usuario = new Usuario
            {
                Nombre = barbero.Nombre,
                Email = dto.Email.ToLower(),
                PasswordHash = PasswordService.HashPassword(dto.Password),
                Rol = "Barbero",
                BarberoId = barbero.Id,
                Estado = "Activo",
                FechaCreacion = DateTime.UtcNow
            };
            _context.Usuarios.Add(usuario);
        }

        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Credenciales actualizadas exitosamente" });
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

        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.BarberoId == barbero.Id && u.Estado == "Activo");
        if (usuario != null)
            usuario.Estado = "Inactivo";

        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Barbero eliminado exitosamente" });
    }
}

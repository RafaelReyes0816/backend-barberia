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
    private readonly IServiceScopeFactory _scopeFactory;

    public BarberosController(AppDbContext context, CodigoService codigoService, IServiceScopeFactory scopeFactory)
    {
        _context = context;
        _codigoService = codigoService;
        _scopeFactory = scopeFactory;
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

        var codigo = await _codigoService.GenerarCodigoBarbero();
        var nombre = dto.Nombre;
        var email = dto.Email?.ToLower();
        var passwordHash = !string.IsNullOrEmpty(dto.Password) ? PasswordService.HashPassword(dto.Password) : null;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                if (!string.IsNullOrEmpty(email))
                {
                    var emailExiste = await ctx.Usuarios
                        .AnyAsync(u => u.Email == email && u.Estado == "Activo");
                    if (emailExiste) return;
                }

                var barbero = new Barbero
                {
                    Codigo = codigo,
                    Nombre = nombre,
                    Estado = "Activo",
                    FechaCreacion = DateTime.UtcNow
                };
                ctx.Barberos.Add(barbero);

                if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(passwordHash))
                {
                    ctx.Usuarios.Add(new Usuario
                    {
                        Nombre = nombre,
                        Email = email,
                        PasswordHash = passwordHash,
                        Rol = "Barbero",
                        BarberoId = barbero.Id,
                        Estado = "Activo",
                        FechaCreacion = DateTime.UtcNow
                    });
                }

                await ctx.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BARBERO-FIRE-FORGET] {ex.Message}");
            }
        });

        return Accepted(new
        {
            mensaje = "Barbero creado exitosamente",
            datos = new BarberoResponseDto
            {
                Codigo = codigo,
                Nombre = nombre,
                Email = email,
                Estado = "Activo",
                FechaCreacion = DateTime.UtcNow
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

        var nombreActualizado = dto.NuevoNombre;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var b = await ctx.Barberos.FirstOrDefaultAsync(b => b.Codigo == codigo);
                if (b != null)
                {
                    b.Nombre = nombreActualizado;
                    var u = await ctx.Usuarios.FirstOrDefaultAsync(u => u.BarberoId == b.Id && u.Estado == "Activo");
                    if (u != null) u.Nombre = nombreActualizado;
                    await ctx.SaveChangesAsync();
                }
            }
            catch { }
        });

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

        var barberoId = barbero.Id;
        var barberoNombre = barbero.Nombre;
        var emailLower = dto.Email.ToLower();
        var passwordHash = PasswordService.HashPassword(dto.Password);

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var usuarioExistente = await ctx.Usuarios
                    .FirstOrDefaultAsync(u => u.BarberoId == barberoId && u.Estado == "Activo");

                if (usuarioExistente != null)
                {
                    usuarioExistente.Email = emailLower;
                    usuarioExistente.PasswordHash = passwordHash;
                }
                else
                {
                    ctx.Usuarios.Add(new Usuario
                    {
                        Nombre = barberoNombre,
                        Email = emailLower,
                        PasswordHash = passwordHash,
                        Rol = "Barbero",
                        BarberoId = barberoId,
                        Estado = "Activo",
                        FechaCreacion = DateTime.UtcNow
                    });
                }
                await ctx.SaveChangesAsync();
            }
            catch { }
        });

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

        var barberoId = barbero.Id;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var b = await ctx.Barberos.FirstOrDefaultAsync(b => b.Id == barberoId);
                if (b != null)
                {
                    b.Estado = "Inactivo";
                    var u = await ctx.Usuarios.FirstOrDefaultAsync(u => u.BarberoId == barberoId && u.Estado == "Activo");
                    if (u != null) u.Estado = "Inactivo";
                    await ctx.SaveChangesAsync();
                }
            }
            catch { }
        });

        return Ok(new { mensaje = "Barbero eliminado exitosamente" });
    }
}

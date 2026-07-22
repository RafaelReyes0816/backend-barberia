using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    private readonly string _connectionString;

    public BarberosController(AppDbContext context, CodigoService codigoService, IConfiguration configuration)
    {
        _context = context;
        _codigoService = codigoService;
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
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
        var now = DateTime.UtcNow;

        _ = Task.Run(async () =>
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                if (!string.IsNullOrEmpty(email))
                {
                    await using var checkCmd = new NpgsqlCommand(
                        "SELECT 1 FROM \"Usuarios\" WHERE \"Email\" = @email AND \"Estado\" = 'Activo' LIMIT 1", conn);
                    checkCmd.Parameters.AddWithValue("@email", email);
                    var exists = await checkCmd.ExecuteScalarAsync();
                    if (exists != null) return;
                }

                await using var tx = await conn.BeginTransactionAsync();

                await using var insertBarbero = new NpgsqlCommand(
                    "INSERT INTO \"Barberos\" (\"Codigo\", \"Nombre\", \"Estado\", \"FechaCreacion\") VALUES (@codigo, @nombre, 'Activo', @now) RETURNING \"Id\"", conn, tx);
                insertBarbero.Parameters.AddWithValue("@codigo", codigo);
                insertBarbero.Parameters.AddWithValue("@nombre", nombre);
                insertBarbero.Parameters.AddWithValue("@now", now);
                var barberoId = (int)(await insertBarbero.ExecuteScalarAsync())!;

                if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(passwordHash))
                {
                    await using var insertUser = new NpgsqlCommand(
                        "INSERT INTO \"Usuarios\" (\"Nombre\", \"Email\", \"PasswordHash\", \"Rol\", \"BarberoId\", \"Estado\", \"FechaCreacion\") VALUES (@nombre, @email, @hash, 'Barbero', @barberoId, 'Activo', @now)", conn, tx);
                    insertUser.Parameters.AddWithValue("@nombre", nombre);
                    insertUser.Parameters.AddWithValue("@email", email);
                    insertUser.Parameters.AddWithValue("@hash", passwordHash);
                    insertUser.Parameters.AddWithValue("@barberoId", barberoId);
                    insertUser.Parameters.AddWithValue("@now", now);
                    await insertUser.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BARBERO-CREATE] {ex}");
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
                FechaCreacion = now
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
        var barberoId = barbero.Id;

        _ = Task.Run(async () =>
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    "UPDATE \"Barberos\" SET \"Nombre\" = @nombre WHERE \"Codigo\" = @codigo; UPDATE \"Usuarios\" SET \"Nombre\" = @nombre WHERE \"BarberoId\" = (SELECT \"Id\" FROM \"Barberos\" WHERE \"Codigo\" = @codigo) AND \"Estado\" = 'Activo'", conn);
                cmd.Parameters.AddWithValue("@nombre", nombreActualizado);
                cmd.Parameters.AddWithValue("@codigo", codigo);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BARBERO-UPDATE] {ex.Message}");
            }
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
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                await using var checkCmd = new NpgsqlCommand(
                    "SELECT \"Id\" FROM \"Usuarios\" WHERE \"BarberoId\" = @barberoId AND \"Estado\" = 'Activo' LIMIT 1", conn);
                checkCmd.Parameters.AddWithValue("@barberoId", barberoId);
                var existingId = await checkCmd.ExecuteScalarAsync();

                if (existingId != null)
                {
                    await using var updateCmd = new NpgsqlCommand(
                        "UPDATE \"Usuarios\" SET \"Email\" = @email, \"PasswordHash\" = @hash WHERE \"Id\" = @id", conn);
                    updateCmd.Parameters.AddWithValue("@email", emailLower);
                    updateCmd.Parameters.AddWithValue("@hash", passwordHash);
                    updateCmd.Parameters.AddWithValue("@id", existingId);
                    await updateCmd.ExecuteNonQueryAsync();
                }
                else
                {
                    await using var insertCmd = new NpgsqlCommand(
                        "INSERT INTO \"Usuarios\" (\"Nombre\", \"Email\", \"PasswordHash\", \"Rol\", \"BarberoId\", \"Estado\", \"FechaCreacion\") VALUES (@nombre, @email, @hash, 'Barbero', @barberoId, 'Activo', @now)", conn);
                    insertCmd.Parameters.AddWithValue("@nombre", barberoNombre);
                    insertCmd.Parameters.AddWithValue("@email", emailLower);
                    insertCmd.Parameters.AddWithValue("@hash", passwordHash);
                    insertCmd.Parameters.AddWithValue("@barberoId", barberoId);
                    insertCmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                    await insertCmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BARBERO-CREDENCIALES] {ex.Message}");
            }
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
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    "UPDATE \"Barberos\" SET \"Estado\" = 'Inactivo' WHERE \"Id\" = @id; UPDATE \"Usuarios\" SET \"Estado\" = 'Inactivo' WHERE \"BarberoId\" = @id AND \"Estado\" = 'Activo'", conn);
                cmd.Parameters.AddWithValue("@id", barberoId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BARBERO-DELETE] {ex.Message}");
            }
        });

        return Ok(new { mensaje = "Barbero eliminado exitosamente" });
    }
}

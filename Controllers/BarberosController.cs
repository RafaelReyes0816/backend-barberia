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
    public async Task<ActionResult> Create([FromBody] BarberoRequestDto dto)
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

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            if (!string.IsNullOrEmpty(email))
            {
                await using var checkCmd = new NpgsqlCommand(
                    "SELECT 1 FROM \"Usuarios\" WHERE \"Email\" = @email AND \"Estado\" = 'Activo' LIMIT 1", conn, tx);
                checkCmd.Parameters.AddWithValue("@email", email);
                var exists = await checkCmd.ExecuteScalarAsync();
                if (exists != null)
                {
                    await tx.RollbackAsync();
                    return BadRequest(new { mensaje = "Ya existe un usuario con este email" });
                }
            }

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
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return CreatedAtAction(nameof(GetByCodigo), new { codigo }, new
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
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "UPDATE \"Barberos\" SET \"Nombre\" = @nombre WHERE \"Codigo\" = @codigo AND \"Estado\" != 'Inactivo'; UPDATE \"Usuarios\" SET \"Nombre\" = @nombre WHERE \"BarberoId\" = (SELECT \"Id\" FROM \"Barberos\" WHERE \"Codigo\" = @codigo) AND \"Estado\" = 'Activo'", conn);
        cmd.Parameters.AddWithValue("@nombre", dto.NuevoNombre);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        var affected = await cmd.ExecuteNonQueryAsync();

        if (affected == 0) return NotFound(new { mensaje = "Barbero no encontrado" });

        return Ok(new { mensaje = "Barbero actualizado exitosamente" });
    }

    [HttpPost("{codigo}/credenciales")]
    [Authorize(Roles = "Encargado")]
    public async Task<IActionResult> AsignarCredenciales(string codigo, [FromBody] BarberoCredencialesDto dto)
    {
        int barberoId;
        string barberoNombre;

        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            await using var getCmd = new NpgsqlCommand(
                "SELECT \"Id\", \"Nombre\" FROM \"Barberos\" WHERE \"Codigo\" = @codigo AND \"Estado\" != 'Inactivo'", conn);
            getCmd.Parameters.AddWithValue("@codigo", codigo);
            await using var reader = await getCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return NotFound(new { mensaje = "Barbero no encontrado" });
            barberoId = reader.GetInt32(0);
            barberoNombre = reader.GetString(1);
        }

        var emailLower = dto.Email.ToLower();
        var passwordHash = PasswordService.HashPassword(dto.Password);

        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync();

            await using var checkCmd = new NpgsqlCommand(
                "SELECT 1 FROM \"Usuarios\" WHERE \"Email\" = @email AND \"Estado\" = 'Activo' AND \"BarberoId\" != @barberoId LIMIT 1", conn);
            checkCmd.Parameters.AddWithValue("@email", emailLower);
            checkCmd.Parameters.AddWithValue("@barberoId", barberoId);
            var exists = await checkCmd.ExecuteScalarAsync();
            if (exists != null)
                return BadRequest(new { mensaje = "Ya existe otro usuario con este email" });

            await using var updateCmd = new NpgsqlCommand(
                "UPDATE \"Usuarios\" SET \"Email\" = @email, \"PasswordHash\" = @hash WHERE \"BarberoId\" = @barberoId AND \"Estado\" = 'Activo'; INSERT INTO \"Usuarios\" (\"Nombre\", \"Email\", \"PasswordHash\", \"Rol\", \"BarberoId\", \"Estado\", \"FechaCreacion\") SELECT @nombre, @email, @hash, 'Barbero', @barberoId, 'Activo', @now WHERE NOT EXISTS (SELECT 1 FROM \"Usuarios\" WHERE \"BarberoId\" = @barberoId AND \"Estado\" = 'Activo')", conn);
            updateCmd.Parameters.AddWithValue("@email", emailLower);
            updateCmd.Parameters.AddWithValue("@hash", passwordHash);
            updateCmd.Parameters.AddWithValue("@barberoId", barberoId);
            updateCmd.Parameters.AddWithValue("@nombre", barberoNombre);
            updateCmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
            await updateCmd.ExecuteNonQueryAsync();
        }

        return Ok(new { mensaje = "Credenciales actualizadas exitosamente" });
    }

    [HttpDelete("{codigo}")]
    [Authorize(Roles = "Encargado")]
    public async Task<IActionResult> SoftDelete(string codigo)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE \"Barberos\" SET \"Estado\" = 'Inactivo' WHERE \"Codigo\" = @codigo AND \"Estado\" != 'Inactivo'; UPDATE \"Usuarios\" SET \"Estado\" = 'Inactivo' WHERE \"BarberoId\" = (SELECT \"Id\" FROM \"Barberos\" WHERE \"Codigo\" = @codigo) AND \"Estado\" = 'Activo'", conn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        var affected = await cmd.ExecuteNonQueryAsync();

        if (affected == 0) return NotFound(new { mensaje = "Barbero no encontrado" });

        return Ok(new { mensaje = "Barbero eliminado exitosamente" });
    }
}

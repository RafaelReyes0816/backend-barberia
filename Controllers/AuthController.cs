using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using BarberPro.Data;
using BarberPro.Dominio;
using BarberPro.DTOs.Auth;
using BarberPro.Services;

namespace BarberPro.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly TokenService _tokenService;
    private readonly string _connectionString;

    public AuthController(AppDbContext context, TokenService tokenService, IConfiguration configuration)
    {
        _context = context;
        _tokenService = tokenService;
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }

    [HttpPost("setup")]
    public async Task<ActionResult> Setup([FromBody] SetupDto dto)
    {
        var existenUsuarios = await _context.Usuarios.AnyAsync();
        if (existenUsuarios)
            return BadRequest(new { mensaje = "El setup ya fue completado. No se pueden crear más usuarios administradores desde aquí." });

        var refreshToken = _tokenService.GenerateRefreshToken();
        var refreshTokenExpiry = _tokenService.GetRefreshTokenExpiration();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO \"Usuarios\" (\"Nombre\", \"Email\", \"PasswordHash\", \"Rol\", \"Estado\", \"FechaCreacion\", \"RefreshToken\", \"RefreshTokenExpiry\") VALUES (@nombre, @email, @hash, 'Encargado', 'Activo', @now, @rt, @rte) RETURNING \"Id\"", conn);
        cmd.Parameters.AddWithValue("@nombre", dto.Nombre);
        cmd.Parameters.AddWithValue("@email", dto.Email.ToLower());
        cmd.Parameters.AddWithValue("@hash", PasswordService.HashPassword(dto.Password));
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@rt", refreshToken);
        cmd.Parameters.AddWithValue("@rte", refreshTokenExpiry);
        var id = (int)(await cmd.ExecuteScalarAsync())!;

        var usuario = new Usuario { Id = id, Nombre = dto.Nombre, Email = dto.Email.ToLower(), Rol = "Encargado" };
        var token = _tokenService.GenerateAccessToken(usuario);

        return Ok(new AuthResponseDto
        {
            Token = token,
            RefreshToken = refreshToken,
            ExpiresAt = _tokenService.GetAccessTokenExpiration(),
            Usuario = new UsuarioResponseDto
            {
                Id = id,
                Nombre = dto.Nombre,
                Email = dto.Email.ToLower(),
                Rol = "Encargado",
                Estado = "Activo"
            }
        });
    }

    [HttpPost("login")]
    public async Task<ActionResult> Login([FromBody] LoginDto dto)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT u.""Id"", u.""Nombre"", u.""Email"", u.""Rol"", u.""BarberoId"",
                   u.""Estado"", u.""PasswordHash"",
                   b.""Nombre"" as ""BarberoNombre""
            FROM ""Usuarios"" u
            LEFT JOIN ""Barberos"" b ON u.""BarberoId"" = b.""Id""
            WHERE u.""Email"" = @email AND u.""Estado"" = 'Activo'
            LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@email", dto.Email.ToLower());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Unauthorized(new { mensaje = "Email o contraseña incorrectos" });

        var passwordHash = reader.GetString(6);
        if (!PasswordService.VerifyPassword(dto.Password, passwordHash))
            return Unauthorized(new { mensaje = "Email o contraseña incorrectos" });

        var userId = reader.GetInt32(0);
        var nombre = reader.GetString(1);
        var email = reader.GetString(2);
        var rol = reader.GetString(3);
        var barberoId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
        var estado = reader.GetString(5);
        var barberoNombre = reader.IsDBNull(7) ? null : reader.GetString(7);

        var refreshToken = _tokenService.GenerateRefreshToken();
        var refreshTokenExpiry = _tokenService.GetRefreshTokenExpiration();

        var usuario = new Usuario { Id = userId, Nombre = nombre, Email = email, Rol = rol, BarberoId = barberoId, Estado = estado };
        var token = _tokenService.GenerateAccessToken(usuario);

        _ = Task.Run(async () =>
        {
            try
            {
                await using var c = new NpgsqlConnection(_connectionString);
                await c.OpenAsync();
                await using var u = new NpgsqlCommand(
                    "UPDATE \"Usuarios\" SET \"RefreshToken\" = @rt, \"RefreshTokenExpiry\" = @rte WHERE \"Id\" = @id", c);
                u.Parameters.AddWithValue("@rt", refreshToken);
                u.Parameters.AddWithValue("@rte", refreshTokenExpiry);
                u.Parameters.AddWithValue("@id", userId);
                await u.ExecuteNonQueryAsync();
            }
            catch { }
        });

        return Ok(new AuthResponseDto
        {
            Token = token,
            RefreshToken = refreshToken,
            ExpiresAt = _tokenService.GetAccessTokenExpiration(),
            Usuario = new UsuarioResponseDto
            {
                Id = userId,
                Nombre = nombre,
                Email = email,
                Rol = rol,
                BarberoId = barberoId,
                BarberoNombre = barberoNombre,
                Estado = estado
            }
        });
    }

    [HttpPost("refresh")]
    public async Task<ActionResult> Refresh([FromBody] RefreshDto dto)
    {
        if (string.IsNullOrEmpty(dto.RefreshToken))
            return BadRequest(new { mensaje = "Refresh token requerido" });

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT ""Id"", ""Nombre"", ""Email"", ""Rol"", ""BarberoId"", ""Estado"",
                   ""RefreshToken"", ""RefreshTokenExpiry""
            FROM ""Usuarios""
            WHERE ""Estado"" = 'Activo' AND ""RefreshToken"" = @rt
            LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@rt", dto.RefreshToken);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Unauthorized(new { mensaje = "Refresh token inválido o expirado" });

        var userId = reader.GetInt32(0);
        var nombre = reader.GetString(1);
        var email = reader.GetString(2);
        var rol = reader.GetString(3);
        var barberoId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
        var estado = reader.GetString(5);
        var storedRt = reader.GetString(6);
        var rtExpiry = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7);

        if (storedRt != dto.RefreshToken || !rtExpiry.HasValue || rtExpiry.Value <= DateTime.UtcNow)
            return Unauthorized(new { mensaje = "Refresh token inválido o expirado" });

        var newRefreshToken = _tokenService.GenerateRefreshToken();
        var newRefreshTokenExpiry = _tokenService.GetRefreshTokenExpiration();

        var usuario = new Usuario { Id = userId, Nombre = nombre, Email = email, Rol = rol, BarberoId = barberoId, Estado = estado };
        var token = _tokenService.GenerateAccessToken(usuario);

        _ = Task.Run(async () =>
        {
            try
            {
                await using var c = new NpgsqlConnection(_connectionString);
                await c.OpenAsync();
                await using var u = new NpgsqlCommand(
                    "UPDATE \"Usuarios\" SET \"RefreshToken\" = @rt, \"RefreshTokenExpiry\" = @rte WHERE \"Id\" = @id", c);
                u.Parameters.AddWithValue("@rt", newRefreshToken);
                u.Parameters.AddWithValue("@rte", newRefreshTokenExpiry);
                u.Parameters.AddWithValue("@id", userId);
                await u.ExecuteNonQueryAsync();
            }
            catch { }
        });

        return Ok(new AuthResponseDto
        {
            Token = token,
            RefreshToken = newRefreshToken,
            ExpiresAt = _tokenService.GetAccessTokenExpiration(),
            Usuario = new UsuarioResponseDto
            {
                Id = userId,
                Nombre = nombre,
                Email = email,
                Rol = rol,
                BarberoId = barberoId,
                Estado = estado
            }
        });
    }

    [HttpGet("usuarios")]
    [Authorize(Roles = "Encargado")]
    public async Task<ActionResult<IEnumerable<UsuarioResponseDto>>> GetAllUsuarios()
    {
        var usuarios = await _context.Usuarios
            .Where(u => u.Estado != "Inactivo")
            .Select(u => new UsuarioResponseDto
            {
                Id = u.Id,
                Nombre = u.Nombre,
                Email = u.Email,
                Rol = u.Rol,
                BarberoId = u.BarberoId,
                Estado = u.Estado
            })
            .ToListAsync();

        return Ok(usuarios);
    }

    [HttpPost("usuarios")]
    [Authorize(Roles = "Encargado")]
    public async Task<ActionResult> CreateUsuario([FromBody] UsuarioRequestDto dto)
    {
        var emailExiste = await _context.Usuarios
            .AnyAsync(u => u.Email == dto.Email.ToLower() && u.Estado == "Activo");

        if (emailExiste)
            return BadRequest(new { mensaje = "Ya existe un usuario con este email" });

        var usuario = new Usuario
        {
            Nombre = dto.Nombre,
            Email = dto.Email.ToLower(),
            PasswordHash = PasswordService.HashPassword(dto.Password),
            Rol = dto.Rol,
            BarberoId = dto.BarberoId,
            Estado = "Activo",
            FechaCreacion = DateTime.UtcNow
        };

        _context.Usuarios.Add(usuario);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            mensaje = "Usuario creado exitosamente",
            datos = new UsuarioResponseDto
            {
                Id = usuario.Id,
                Nombre = usuario.Nombre,
                Email = usuario.Email,
                Rol = usuario.Rol,
                BarberoId = usuario.BarberoId,
                Estado = usuario.Estado
            }
        });
    }

    [HttpPut("usuarios/{id}")]
    [Authorize(Roles = "Encargado")]
    public async Task<IActionResult> UpdateUsuario(int id, [FromBody] UsuarioUpdateDto dto)
    {
        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Id == id && u.Estado != "Inactivo");

        if (usuario == null)
            return NotFound(new { mensaje = "Usuario no encontrado" });

        if (!string.IsNullOrEmpty(dto.NuevoNombre))
            usuario.Nombre = dto.NuevoNombre;

        if (!string.IsNullOrEmpty(dto.NuevoEmail))
            usuario.Email = dto.NuevoEmail.ToLower();

        if (!string.IsNullOrEmpty(dto.NuevaPassword))
            usuario.PasswordHash = PasswordService.HashPassword(dto.NuevaPassword);

        if (!string.IsNullOrEmpty(dto.NuevoRol))
            usuario.Rol = dto.NuevoRol;

        if (dto.NuevoBarberoId.HasValue)
            usuario.BarberoId = dto.NuevoBarberoId;

        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Usuario actualizado exitosamente" });
    }

    [HttpDelete("usuarios/{id}")]
    [Authorize(Roles = "Encargado")]
    public async Task<IActionResult> DeleteUsuario(int id)
    {
        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Id == id && u.Estado != "Inactivo");

        if (usuario == null)
            return NotFound(new { mensaje = "Usuario no encontrado" });

        usuario.Estado = "Inactivo";
        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Usuario desactivado exitosamente" });
    }
}

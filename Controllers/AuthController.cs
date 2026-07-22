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
        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Email == dto.Email.ToLower() && u.Estado == "Activo");

        if (usuario == null || !PasswordService.VerifyPassword(dto.Password, usuario.PasswordHash))
            return Unauthorized(new { mensaje = "Email o contraseña incorrectos" });

        var refreshToken = _tokenService.GenerateRefreshToken();
        var refreshTokenExpiry = _tokenService.GetRefreshTokenExpiration();

        var token = _tokenService.GenerateAccessToken(usuario);

        var userId = usuario.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    "UPDATE \"Usuarios\" SET \"RefreshToken\" = @rt, \"RefreshTokenExpiry\" = @rte WHERE \"Id\" = @id", conn);
                cmd.Parameters.AddWithValue("@rt", refreshToken);
                cmd.Parameters.AddWithValue("@rte", refreshTokenExpiry);
                cmd.Parameters.AddWithValue("@id", userId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        });

        string? barberoNombre = null;
        if (usuario.BarberoId.HasValue)
        {
            var barbero = await _context.Barberos.FindAsync(usuario.BarberoId.Value);
            barberoNombre = barbero?.Nombre;
        }

        return Ok(new AuthResponseDto
        {
            Token = token,
            RefreshToken = refreshToken,
            ExpiresAt = _tokenService.GetAccessTokenExpiration(),
            Usuario = new UsuarioResponseDto
            {
                Id = usuario.Id,
                Nombre = usuario.Nombre,
                Email = usuario.Email,
                Rol = usuario.Rol,
                BarberoId = usuario.BarberoId,
                BarberoNombre = barberoNombre,
                Estado = usuario.Estado
            }
        });
    }

    [HttpPost("refresh")]
    public async Task<ActionResult> Refresh([FromBody] RefreshDto dto)
    {
        if (string.IsNullOrEmpty(dto.RefreshToken))
            return BadRequest(new { mensaje = "Refresh token requerido" });

        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Estado == "Activo" && u.RefreshToken == dto.RefreshToken);

        if (usuario == null || !_tokenService.ValidateRefreshToken(usuario, dto.RefreshToken))
            return Unauthorized(new { mensaje = "Refresh token inválido o expirado" });

        var newRefreshToken = _tokenService.GenerateRefreshToken();
        var newRefreshTokenExpiry = _tokenService.GetRefreshTokenExpiration();

        var token = _tokenService.GenerateAccessToken(usuario);

        var userId = usuario.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    "UPDATE \"Usuarios\" SET \"RefreshToken\" = @rt, \"RefreshTokenExpiry\" = @rte WHERE \"Id\" = @id", conn);
                cmd.Parameters.AddWithValue("@rt", newRefreshToken);
                cmd.Parameters.AddWithValue("@rte", newRefreshTokenExpiry);
                cmd.Parameters.AddWithValue("@id", userId);
                await cmd.ExecuteNonQueryAsync();
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
                Id = usuario.Id,
                Nombre = usuario.Nombre,
                Email = usuario.Email,
                Rol = usuario.Rol,
                BarberoId = usuario.BarberoId,
                Estado = usuario.Estado
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

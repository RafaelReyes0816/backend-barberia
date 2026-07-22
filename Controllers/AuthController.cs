using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BarberPro.Data;
using BarberPro.Dominio;
using BarberPro.DTOs.Auth;
using BarberPro.DTOs.Barberos;
using BarberPro.DTOs.Clientes;
using BarberPro.DTOs.Servicios;
using BarberPro.DTOs.Citas;
using BarberPro.DTOs.Dashboard;
using BarberPro.DTOs.CierreCaja;
using BarberPro.Services;
using Npgsql;

namespace BarberPro.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly TokenService _tokenService;
    private readonly string _connectionString;
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AuthController(AppDbContext context, TokenService tokenService, IConfiguration configuration)
    {
        _context = context;
        _tokenService = tokenService;
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }

    private async Task<InitDataDto?> FetchInitDataAsync(string rol, int? barberoId)
    {
        var hoy = DateTime.UtcNow.Date;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        if (rol == "Encargado")
        {
            var sql = @"
                SELECT 'stats', row_to_json(t) FROM (
                    SELECT
                        (SELECT COUNT(*) FROM ""Citas"" WHERE ""Estado"" = 'Pendiente') AS ""citasPendientes"",
                        (SELECT COUNT(*) FROM ""Citas"" WHERE ""Estado"" = 'Confirmada') AS ""citasConfirmadas"",
                        (SELECT COUNT(*) FROM ""Citas"" WHERE ""Estado"" IN ('Completada','Terminada') AND ""Fecha""::date = @hoy) AS ""citasCompletadas"",
                        (SELECT COUNT(*) FROM ""Citas"" WHERE ""Fecha""::date = @hoy AND ""Estado"" != 'Inactivo') AS ""citasHoy"",
                        COALESCE((SELECT SUM(s.""Precio"") FROM ""Citas"" c JOIN ""Servicios"" s ON c.""ServicioId"" = s.""Id"" WHERE c.""Fecha""::date = @hoy AND c.""Estado"" IN ('Completada','Terminada')), 0) AS ""totalRecaudadoHoy""
                ) t
                UNION ALL
                SELECT 'barberos', COALESCE((SELECT json_agg(row_to_json(b)) FROM (SELECT ""Codigo"" as ""codigo"", ""Nombre"" as ""nombre"", ""Estado"" as ""estado"", ""FechaCreacion"" as ""fechaCreacion"" FROM ""Barberos"" WHERE ""Estado"" != 'Inactivo' ORDER BY ""Nombre"") b), '[]'::json)
                UNION ALL
                SELECT 'servicios', COALESCE((SELECT json_agg(row_to_json(s)) FROM (SELECT ""Codigo"" as ""codigo"", ""Nombre"" as ""nombre"", ""Precio"" as ""precio"", ""Estado"" as ""estado"", ""FechaCreacion"" as ""fechaCreacion"" FROM ""Servicios"" WHERE ""Estado"" != 'Inactivo' ORDER BY ""Nombre"") s), '[]'::json)
                UNION ALL
                SELECT 'clientes', COALESCE((SELECT json_agg(row_to_json(c)) FROM (SELECT ""Codigo"" as ""codigo"", ""Nombre"" as ""nombre"", ""Telefono"" as ""telefono"", ""Estado"" as ""estado"", ""FechaCreacion"" as ""fechaCreacion"" FROM ""Clientes"" WHERE ""Estado"" != 'Inactivo' ORDER BY ""Nombre"") c), '[]'::json)
                UNION ALL
                SELECT 'citas', COALESCE((SELECT json_agg(row_to_json(c)) FROM (
                    SELECT c2.""Codigo"" as ""codigo"", c2.""CodigoGenerado"" as ""codigoGenerado"",
                        cl.""Nombre"" as ""clienteNombre"", cl.""Telefono"" as ""clienteTelefono"",
                        b.""Nombre"" as ""barberoNombre"",
                        s.""Nombre"" as ""servicioNombre"", s.""Precio"" as ""servicioPrecio"",
                        c2.""Fecha"" as ""fecha"", to_char(c2.""Hora"", 'HH24:MI') as ""hora"",
                        c2.""Estado"" as ""estado"", c2.""FechaCreacion"" as ""fechaCreacion""
                    FROM ""Citas"" c2
                    JOIN ""Clientes"" cl ON c2.""ClienteId"" = cl.""Id""
                    JOIN ""Barberos"" b ON c2.""BarberoId"" = b.""Id""
                    JOIN ""Servicios"" s ON c2.""ServicioId"" = s.""Id""
                    WHERE c2.""Estado"" IN ('Pendiente','Confirmada','Completada','Terminada')
                    ORDER BY c2.""Fecha"" DESC, c2.""Hora""
                ) c), '[]'::json)
                UNION ALL
                SELECT 'cierres', COALESCE((SELECT json_agg(row_to_json(cc)) FROM (
                    SELECT ""Id"" as ""id"", ""Fecha"" as ""fecha"", ""TotalRecaudado"" as ""totalRecaudado"",
                        ""TotalCitas"" as ""totalCitas"", CASE WHEN ""DetallesJson"" IS NOT NULL THEN ""DetallesJson""::json ELSE null END as ""detalles"", ""FechaCreacion"" as ""fechaCreacion""
                    FROM ""CierreCaja"" ORDER BY ""Fecha"" DESC
                ) cc), '[]'::json)
            ";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@hoy", hoy);
            await using var reader = await cmd.ExecuteReaderAsync();

            var result = new InitDataDto { Stats = new DashboardStatsDto() };
            while (await reader.ReadAsync())
            {
                var tipo = reader.GetString(0);
                var data = reader.GetString(1);
                switch (tipo)
                {
                    case "stats": result.Stats = JsonSerializer.Deserialize<DashboardStatsDto>(data, _jsonOpts) ?? new(); break;
                    case "barberos": result.Barberos = JsonSerializer.Deserialize<List<BarberoResponseDto>>(data, _jsonOpts) ?? new(); break;
                    case "servicios": result.Servicios = JsonSerializer.Deserialize<List<ServicioResponseDto>>(data, _jsonOpts) ?? new(); break;
                    case "clientes": result.Clientes = JsonSerializer.Deserialize<List<ClienteResponseDto>>(data, _jsonOpts) ?? new(); break;
                    case "citas": result.Citas = JsonSerializer.Deserialize<List<CitaResponseDto>>(data, _jsonOpts) ?? new(); break;
                    case "cierres": result.CierresCaja = JsonSerializer.Deserialize<List<CierreCajaResponseDto>>(data, _jsonOpts) ?? new(); break;
                }
            }
            return result;
        }
        else if (rol == "Barbero" && barberoId.HasValue)
        {
            var sql = @"
                SELECT 'stats', row_to_json(t) FROM (
                    SELECT
                        (SELECT COUNT(*) FROM ""Citas"" WHERE ""BarberoId"" = @barberoId AND ""Fecha""::date = @hoy AND ""Estado"" != 'Inactivo') AS ""citasHoy"",
                        (SELECT COUNT(*) FROM ""Citas"" WHERE ""BarberoId"" = @barberoId AND ""Fecha""::date = @hoy AND ""Estado"" IN ('Completada','Terminada')) AS ""citasCompletadasHoy"",
                        (SELECT COUNT(*) FROM ""Citas"" WHERE ""BarberoId"" = @barberoId AND ""Estado"" != 'Inactivo') AS ""totalCitas""
                ) t
                UNION ALL
                SELECT 'citas', COALESCE((SELECT json_agg(row_to_json(c)) FROM (
                    SELECT c2.""Codigo"" as ""codigo"", c2.""CodigoGenerado"" as ""codigoGenerado"",
                        cl.""Nombre"" as ""clienteNombre"", cl.""Telefono"" as ""clienteTelefono"",
                        b.""Nombre"" as ""barberoNombre"",
                        s.""Nombre"" as ""servicioNombre"", s.""Precio"" as ""servicioPrecio"",
                        c2.""Fecha"" as ""fecha"", to_char(c2.""Hora"", 'HH24:MI') as ""hora"",
                        c2.""Estado"" as ""estado"", c2.""FechaCreacion"" as ""fechaCreacion""
                    FROM ""Citas"" c2
                    JOIN ""Clientes"" cl ON c2.""ClienteId"" = cl.""Id""
                    JOIN ""Barberos"" b ON c2.""BarberoId"" = b.""Id""
                    JOIN ""Servicios"" s ON c2.""ServicioId"" = s.""Id""
                    WHERE c2.""BarberoId"" = @barberoId AND c2.""Estado"" IN ('Pendiente','Confirmada','Completada','Terminada')
                    ORDER BY c2.""Fecha"" DESC, c2.""Hora""
                ) c), '[]'::json)
            ";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@barberoId", barberoId.Value);
            cmd.Parameters.AddWithValue("@hoy", hoy);
            await using var reader = await cmd.ExecuteReaderAsync();

            var result = new InitDataDto { BarberoStats = new DashboardStatsPersonalesDto() };
            while (await reader.ReadAsync())
            {
                var tipo = reader.GetString(0);
                var data = reader.GetString(1);
                switch (tipo)
                {
                    case "stats": result.BarberoStats = JsonSerializer.Deserialize<DashboardStatsPersonalesDto>(data, _jsonOpts) ?? new(); break;
                    case "citas": result.Citas = JsonSerializer.Deserialize<List<CitaResponseDto>>(data, _jsonOpts) ?? new(); break;
                }
            }
            return result;
        }

        return null;
    }

    [HttpPost("setup")]
    public async Task<ActionResult> Setup([FromBody] SetupDto dto)
    {
        var existenUsuarios = await _context.Usuarios.AnyAsync();
        if (existenUsuarios)
            return BadRequest(new { mensaje = "El setup ya fue completado. No se pueden crear más usuarios administradores desde aquí." });

        var refreshToken = _tokenService.GenerateRefreshToken();
        var refreshTokenExpiry = _tokenService.GetRefreshTokenExpiration();

        var usuario = new Usuario
        {
            Nombre = dto.Nombre,
            Email = dto.Email.ToLower(),
            PasswordHash = PasswordService.HashPassword(dto.Password),
            Rol = "Encargado",
            Estado = "Activo",
            FechaCreacion = DateTime.UtcNow,
            RefreshToken = refreshToken,
            RefreshTokenExpiry = refreshTokenExpiry
        };

        _context.Usuarios.Add(usuario);
        await _context.SaveChangesAsync();

        var token = _tokenService.GenerateAccessToken(usuario);

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
                Estado = usuario.Estado
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

        usuario.RefreshToken = refreshToken;
        usuario.RefreshTokenExpiry = refreshTokenExpiry;
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();

        var token = _tokenService.GenerateAccessToken(usuario);

        string? barberoNombre = null;
        if (usuario.BarberoId.HasValue)
        {
            var barbero = await _context.Barberos.FindAsync(usuario.BarberoId.Value);
            barberoNombre = barbero?.Nombre;
        }

        var initData = await FetchInitDataAsync(usuario.Rol, usuario.BarberoId);

        return Ok(new AuthWithInitResponseDto
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
            },
            InitData = initData
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

        usuario.RefreshToken = newRefreshToken;
        usuario.RefreshTokenExpiry = newRefreshTokenExpiry;
        await _context.SaveChangesAsync();

        var token = _tokenService.GenerateAccessToken(usuario);

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

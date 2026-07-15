using System.ComponentModel.DataAnnotations;

namespace BarberPro.DTOs.Auth;

public class SetupDto
{
    [Required(ErrorMessage = "El nombre es requerido")]
    [StringLength(100, MinimumLength = 2)]
    public string Nombre { get; set; } = null!;

    [Required(ErrorMessage = "El email es requerido")]
    [EmailAddress(ErrorMessage = "El email no es válido")]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "La contraseña es requerida")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres")]
    public string Password { get; set; } = null!;
}

public class LoginDto
{
    [Required(ErrorMessage = "El email es requerido")]
    [EmailAddress]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "La contraseña es requerida")]
    public string Password { get; set; } = null!;
}

public class UsuarioRequestDto
{
    [Required(ErrorMessage = "El nombre es requerido")]
    [StringLength(100, MinimumLength = 2)]
    public string Nombre { get; set; } = null!;

    [Required(ErrorMessage = "El email es requerido")]
    [EmailAddress]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "La contraseña es requerida")]
    [StringLength(100, MinimumLength = 6)]
    public string Password { get; set; } = null!;

    [Required(ErrorMessage = "El rol es requerido")]
    [StringLength(20)]
    public string Rol { get; set; } = "Barbero";

    public int? BarberoId { get; set; }
}

public class UsuarioUpdateDto
{
    [StringLength(100, MinimumLength = 2)]
    public string? NuevoNombre { get; set; }

    [EmailAddress]
    public string? NuevoEmail { get; set; }

    [StringLength(100, MinimumLength = 6)]
    public string? NuevaPassword { get; set; }

    [StringLength(20)]
    public string? NuevoRol { get; set; }

    public int? NuevoBarberoId { get; set; }
}

public class AuthResponseDto
{
    public string Token { get; set; } = null!;
    public string RefreshToken { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public UsuarioResponseDto Usuario { get; set; } = null!;
}

public class UsuarioResponseDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Rol { get; set; } = null!;
    public int? BarberoId { get; set; }
    public string? BarberoNombre { get; set; }
    public string Estado { get; set; } = null!;
}

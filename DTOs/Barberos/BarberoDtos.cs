using System.ComponentModel.DataAnnotations;

namespace BarberPro.DTOs.Barberos;

public class BarberoRequestDto
{
    [Required(ErrorMessage = "El nombre es requerido")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 100 caracteres")]
    public string Nombre { get; set; } = null!;

    [EmailAddress(ErrorMessage = "El email no es válido")]
    public string? Email { get; set; }

    [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres")]
    public string? Password { get; set; }
}

public class BarberoUpdateDto
{
    [Required(ErrorMessage = "El nombre es requerido")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 100 caracteres")]
    public string NuevoNombre { get; set; } = null!;
}

public class BarberoResponseDto
{
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string? Email { get; set; }
    public string Estado { get; set; } = null!;
    public DateTime FechaCreacion { get; set; }
}

public class BarberoCredencialesDto
{
    [Required(ErrorMessage = "El email es requerido")]
    [EmailAddress(ErrorMessage = "El email no es válido")]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "La contraseña es requerida")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres")]
    public string Password { get; set; } = null!;
}

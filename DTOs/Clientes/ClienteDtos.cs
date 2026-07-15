using System.ComponentModel.DataAnnotations;

namespace BarberPro.DTOs.Clientes;

public class ClienteRequestDto
{
    [Required(ErrorMessage = "El nombre es requerido")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 100 caracteres")]
    public string Nombre { get; set; } = null!;

    [Required(ErrorMessage = "El teléfono es requerido")]
    [StringLength(20, MinimumLength = 7, ErrorMessage = "El teléfono debe tener entre 7 y 20 caracteres")]
    public string Telefono { get; set; } = null!;
}

public class ClienteUpdateDto
{
    [Required(ErrorMessage = "El nombre es requerido")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 100 caracteres")]
    public string NuevoNombre { get; set; } = null!;

    [Required(ErrorMessage = "El teléfono es requerido")]
    [StringLength(20, MinimumLength = 7, ErrorMessage = "El teléfono debe tener entre 7 y 20 caracteres")]
    public string NuevoTelefono { get; set; } = null!;
}

public class ClienteResponseDto
{
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string Telefono { get; set; } = null!;
    public string Estado { get; set; } = null!;
    public DateTime FechaCreacion { get; set; }
}

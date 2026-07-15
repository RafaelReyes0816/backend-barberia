using System.ComponentModel.DataAnnotations;

namespace BarberPro.DTOs.Servicios;

public class ServicioRequestDto
{
    [Required(ErrorMessage = "El nombre es requerido")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 100 caracteres")]
    public string Nombre { get; set; } = null!;

    [Required(ErrorMessage = "El precio es requerido")]
    [Range(0.01, 99999.99, ErrorMessage = "El precio debe ser mayor a 0")]
    public decimal Precio { get; set; }
}

public class ServicioUpdateDto
{
    [Required(ErrorMessage = "El nombre es requerido")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 100 caracteres")]
    public string NuevoNombre { get; set; } = null!;

    [Required(ErrorMessage = "El precio es requerido")]
    [Range(0.01, 99999.99, ErrorMessage = "El precio debe ser mayor a 0")]
    public decimal NuevoPrecio { get; set; }
}

public class ServicioResponseDto
{
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public decimal Precio { get; set; }
    public string Estado { get; set; } = null!;
    public DateTime FechaCreacion { get; set; }
}

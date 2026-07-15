using System.ComponentModel.DataAnnotations;

namespace BarberPro.DTOs.Citas;

public class CitaRequestDto
{
    [Required(ErrorMessage = "El código del cliente es requerido")]
    public string ClienteCodigo { get; set; } = null!;

    [Required(ErrorMessage = "El código del barbero es requerido")]
    public string BarberoCodigo { get; set; } = null!;

    [Required(ErrorMessage = "El código del servicio es requerido")]
    public string ServicioCodigo { get; set; } = null!;

    [Required(ErrorMessage = "La fecha es requerida")]
    public DateTime Fecha { get; set; }

    [Required(ErrorMessage = "La hora es requerida")]
    public string Hora { get; set; } = null!;
}

public class CitaUpdateDto
{
    [Required(ErrorMessage = "La fecha es requerida")]
    public DateTime NuevaFecha { get; set; }

    [Required(ErrorMessage = "La hora es requerida")]
    public string NuevaHora { get; set; } = null!;
}

public class CitaStatusDto
{
    [Required(ErrorMessage = "El estado es requerido")]
    [StringLength(20)]
    public string Estado { get; set; } = null!;
}

public class CitaResponseDto
{
    public string Codigo { get; set; } = null!;
    public string CodigoGenerado { get; set; } = null!;
    public string ClienteNombre { get; set; } = null!;
    public string ClienteTelefono { get; set; } = null!;
    public string BarberoNombre { get; set; } = null!;
    public string ServicioNombre { get; set; } = null!;
    public decimal ServicioPrecio { get; set; }
    public DateTime Fecha { get; set; }
    public string Hora { get; set; } = null!;
    public string Estado { get; set; } = null!;
    public DateTime FechaCreacion { get; set; }
}

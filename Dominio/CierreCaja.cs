using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BarberPro.Dominio;

public class CierreCaja
{
    [Key]
    public int Id { get; set; }

    [Required]
    public DateTime Fecha { get; set; }

    [Required]
    public decimal TotalRecaudado { get; set; }

    [Required]
    public int TotalCitas { get; set; }

    [JsonIgnore]
    public string? DetallesJson { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}

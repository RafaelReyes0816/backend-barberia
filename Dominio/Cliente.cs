using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BarberPro.Dominio;

public class Cliente
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(20)]
    public string Codigo { get; set; } = null!;

    [Required]
    [StringLength(100)]
    public string Nombre { get; set; } = null!;

    [Required]
    [StringLength(20)]
    public string Telefono { get; set; } = null!;

    [Required]
    [StringLength(20)]
    public string Estado { get; set; } = "Activo";

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public List<Cita>? Citas { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace BarberPro.Dominio;

public class Cita
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(20)]
    public string Codigo { get; set; } = null!;

    [Required]
    public int ClienteId { get; set; }

    [ForeignKey("ClienteId")]
    [JsonIgnore]
    public Cliente? Cliente { get; set; }

    [Required]
    public int BarberoId { get; set; }

    [ForeignKey("BarberoId")]
    [JsonIgnore]
    public Barbero? Barbero { get; set; }

    [Required]
    public int ServicioId { get; set; }

    [ForeignKey("ServicioId")]
    [JsonIgnore]
    public Servicio? Servicio { get; set; }

    [Required]
    public DateTime Fecha { get; set; }

    [Required]
    public TimeSpan Hora { get; set; }

    [Required]
    [StringLength(20)]
    public string Estado { get; set; } = "Pendiente";

    [Required]
    [StringLength(30)]
    public string CodigoGenerado { get; set; } = null!;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}

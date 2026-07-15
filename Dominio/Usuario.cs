using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace BarberPro.Dominio;

public class Usuario
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Nombre { get; set; } = null!;

    [Required]
    [StringLength(100)]
    public string Email { get; set; } = null!;

    [Required]
    [JsonIgnore]
    public string PasswordHash { get; set; } = null!;

    [Required]
    [StringLength(20)]
    public string Rol { get; set; } = "Barbero";

    public int? BarberoId { get; set; }

    [ForeignKey("BarberoId")]
    [JsonIgnore]
    public Barbero? Barbero { get; set; }

    [Required]
    [StringLength(20)]
    public string Estado { get; set; } = "Activo";

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}

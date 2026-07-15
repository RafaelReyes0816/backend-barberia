namespace BarberPro.DTOs.CierreCaja;

public class CierreCajaResponseDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public decimal TotalRecaudado { get; set; }
    public int TotalCitas { get; set; }
    public List<DetalleServicioDto>? Detalles { get; set; }
    public DateTime FechaCreacion { get; set; }
}

public class DetalleServicioDto
{
    public string Servicio { get; set; } = null!;
    public int Cantidad { get; set; }
    public decimal Total { get; set; }
}

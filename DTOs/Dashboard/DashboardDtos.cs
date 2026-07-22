namespace BarberPro.DTOs.Dashboard;

public class DashboardStatsDto
{
    public int CitasPendientes { get; set; }
    public int CitasConfirmadas { get; set; }
    public int CitasCompletadas { get; set; }
    public int CitasHoy { get; set; }
    public decimal TotalRecaudadoHoy { get; set; }
}

public class DashboardStatsPersonalesDto
{
    public int CitasHoy { get; set; }
    public int CitasCompletadasHoy { get; set; }
}

public class BuscarCitaDto
{
    public string CodigoGenerado { get; set; } = null!;
    public string ClienteNombre { get; set; } = null!;
    public string ClienteTelefono { get; set; } = null!;
    public string BarberoNombre { get; set; } = null!;
    public string ServicioNombre { get; set; } = null!;
    public decimal ServicioPrecio { get; set; }
    public DateTime Fecha { get; set; }
    public string Hora { get; set; } = null!;
    public string Estado { get; set; } = null!;
}

using BarberPro.DTOs.Barberos;
using BarberPro.DTOs.Clientes;
using BarberPro.DTOs.Servicios;
using BarberPro.DTOs.Citas;
using BarberPro.DTOs.Dashboard;
using BarberPro.DTOs.CierreCaja;

namespace BarberPro.DTOs.Init;

public class InitEncargadoDto
{
    public DashboardStatsDto Stats { get; set; } = new();
    public List<BarberoResponseDto> Barberos { get; set; } = new();
    public List<ServicioResponseDto> Servicios { get; set; } = new();
    public List<ClienteResponseDto> Clientes { get; set; } = new();
    public List<CitaResponseDto> Citas { get; set; } = new();
    public List<CierreCajaResponseDto> CierresCaja { get; set; } = new();
}

public class InitBarberoDto
{
    public DashboardStatsPersonalesDto Stats { get; set; } = new();
    public List<CitaResponseDto> Citas { get; set; } = new();
}

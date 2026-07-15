using Microsoft.EntityFrameworkCore;
using BarberPro.Data;

namespace BarberPro.Services;

public class CodigoService
{
    private readonly AppDbContext _context;

    public CodigoService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<string> GenerarCodigoCliente()
    {
        var ultimo = await _context.Clientes
            .OrderByDescending(c => c.Id)
            .Select(c => c.Codigo)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(ultimo))
            return "CLI-001";

        var numero = int.Parse(ultimo.Split('-')[1]) + 1;
        return $"CLI-{numero:D3}";
    }

    public async Task<string> GenerarCodigoBarbero()
    {
        var ultimo = await _context.Barberos
            .OrderByDescending(b => b.Id)
            .Select(b => b.Codigo)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(ultimo))
            return "BARB-001";

        var numero = int.Parse(ultimo.Split('-')[1]) + 1;
        return $"BARB-{numero:D3}";
    }

    public async Task<string> GenerarCodigoServicio()
    {
        var ultimo = await _context.Servicios
            .OrderByDescending(s => s.Id)
            .Select(s => s.Codigo)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(ultimo))
            return "SERV-001";

        var numero = int.Parse(ultimo.Split('-')[1]) + 1;
        return $"SERV-{numero:D3}";
    }

    public async Task<string> GenerarCodigoCita()
    {
        var ultimo = await _context.Citas
            .OrderByDescending(c => c.Id)
            .Select(c => c.Codigo)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(ultimo))
            return "CITA-001";

        var numero = int.Parse(ultimo.Split('-')[1]) + 1;
        return $"CITA-{numero:D3}";
    }

    public async Task<string> GenerarCodigoGenerado(DateTime fecha)
    {
        var fechaStr = fecha.ToString("yyyyMMdd");
        var count = await _context.Citas
            .CountAsync(c => c.Fecha.Date == fecha.Date);

        return $"BARB-{fechaStr}-{(count + 1):D3}";
    }
}

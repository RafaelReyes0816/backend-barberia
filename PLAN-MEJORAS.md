# Plan de Mejoras — BarberPro Backend

> **Fecha:** 23 de julio de 2026
> **Stack:** .NET 8 + PostgreSQL + EF Core 8 + JWT Bearer + BCrypt
> **Estado actual:** API funcional — EF Core LINQ en todos los controllers, auth JWT con refresh tokens, rate limiting (login: 5/min, API: 100/min), security headers, health checks, ExceptionMiddleware global.
> **Cobertura de tests:** 0%

---

## Guía de configuración

### Requisitos previos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [PostgreSQL 16+](https://www.postgresql.org/download/) (o Docker)
- Git

### 1. Clonar el repositorio

```bash
git clone https://github.com/RafaelReyes0816/backend-barberia.git
cd backend-barberia
```

### 2. Base de datos

Opción A — PostgreSQL local directo:
```bash
# Crear la base de datos (ajustar usuario y contraseña según tu PostgreSQL)
psql -U postgres -c "CREATE DATABASE barberia;"
```

Opción B — Docker (recomendado para no interferir con otras BD):
```bash
docker run -d \
  --name barberia-pg \
  -e POSTGRES_USER=barberia \
  -E POSTGRES_PASSWORD=barberia123 \
  -e POSTGRES_DB=barberia \
  -p 5432:5432 \
  postgres:16-alpine
```

### 3. Configurar conexión a la BD

Crear/editar `appsettings.Development.json` (este archivo NO se sube a Git):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=barberia;Username=barberia;Password=barberia123"
  },
  "Jwt": {
    "Key": "TuClaveSecretaDeAlMenos32CaracteresAquí",
    "Issuer": "BarberPro",
    "Audience": "BarberProApp",
    "ExpiresInMinutes": 60,
    "RefreshExpiresInDays": 7
  }
}
```

**Nota:** Si usas Docker, el puerto es 5432 y usuario `barberia`. Si usas PostgreSQL local, ajusta puerto (generalmente 5433 o 5432), usuario (`postgres`) y contraseña.

### 4. Aplicar migraciones

```bash
dotnet ef database update
```

Esto crea todas las tablas (Barberos, Clientes, Servicios, Citas, Usuarios, RefreshTokens, CierreCaja) y la data inicial.

### 5. Sembrar datos de prueba (opcional)

Si la BD está vacía, insertar datos de prueba:
```bash
psql -U barberia -d barberia -c "
INSERT INTO \"Usuarios\" (\"Nombre\", \"Email\", \"PasswordHash\", \"Rol\", \"Estado\", \"FechaCreacion\")
VALUES
  ('Admin', 'admin@barberpro.com', '\$2a\$12\$8SqsImSQKu6AuMkSXn7dr.edDEtpLd1tL70u7qSOvru1RDcyxrdhe', 'Encargado', 'Activo', NOW()),
  ('Ricardo Gutierrez', 'barb001@barberpro.com', '\$2a\$12\$eCYzIRuKFIGbC8K0PyOC5O7I3l/08H9qBd1q2aloj5gFhubqwwmni', 'Barbero', 'Activo', NOW());
"
```

Logins de prueba:
| Email | Contraseña | Rol |
|-------|-----------|-----|
| `admin@barberpro.com` | `admin123` | Encargado |
| `barb001@barberpro.com` | `barbero123` | Barbero |

### 6. Ejecutar el backend

```bash
dotnet run
```

El servidor arranca en:
- **HTTP:** `http://localhost:5000`
- **HTTPS:** `https://localhost:5001`
- **Swagger:** `http://localhost:5000/swagger`
- **Health check:** `http://localhost:5000/health`

### 7. Probar los endpoints

```bash
# Login
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@barberpro.com","password":"admin123"}'

# Listar barberos (usar el token del login)
curl http://localhost:5000/api/barberos \
  -H "Authorization: Bearer <TU_TOKEN>"
```

### Variables de entorno (producción)

| Variable | Ejemplo | Descripción |
|----------|---------|-------------|
| `ConnectionStrings__DefaultConnection` | `Host=...;Port=...;Database=...` | Cadena de conexión PostgreSQL |
| `Jwt__Key` | `ClaveSecretaMinimo32Chars` | Clave HMAC-SHA256 para JWT |
| `Jwt__Issuer` | `BarberPro` | Emisor del token |
| `Jwt__Audience` | `BarberProApp` | Audiencia del token |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Entorno de ejecución |

---

## FASE A — Seguridad y correcciones críticas

Prioridad: **ALTA**. Deben resolverse antes de producción.

### A1. Limpieza automática de Refresh Tokens expirados

**Problema:** Los registros en `RefreshTokens` se acumulan infinitamente. Un usuario que usa la app diariamente genera ~1 refresh token/día. En 6 meses = miles de registros sin uso.

**Solución:** Crear un `IHostedService` (background job) que cada 24 horas ejecute:
```sql
DELETE FROM "RefreshTokens" WHERE "ExpiresAt" < NOW() OR "IsRevoked" = true;
```

**Archivos a crear/modificar:**
- `Services/TokenCleanupService.cs` (nuevo)
- `Program.cs` (registrar el hosted service)

---

### A2. Máquina de estados para Citas

**Problema:** No hay validación de transiciones de estado. Actualmente se puede ir de "Cerrada" a "Pendiente", lo cual no tiene sentido de negocio.

**Solución:** Definir transiciones válidas y validar en `CitasController.UpdateStatus`:

```
Pendiente → Confirmada, Cancelada
Confirmada → Completada, Cancelada
Completada → (estado final)
Terminada → (estado final)
Cancelada → Pendiente (reapertura por encargado)
```

**Archivos a modificar:**
- `Controllers/CitasController.cs` (agregar validación en `UpdateStatus`)
- Crear una constante/clase con las transiciones permitidas

---

### A3. Fix race condition en CodigoService

**Problema:** `CodigoService` genera códigos secuenciales (CLI-001, BARB-001) leyendo `Max(Id)`. Si dos requests llegan simultáneamente, ambos leen el mismo Max y generan el mismo código. El índice único de BD lo rechaza pero la app devuelve un error 500.

**Solución:** Envolver en try-catch con `DbUpdateException` y reintentar con el siguiente código, o usar secuencias de PostgreSQL:
```sql
CREATE SEQUENCE barbero_codigo_seq START 1;
```

**Archivos a modificar:**
- `Services/CodigoService.cs`

---

### A4. Logging estructurado con Serilog

**Problema:** Solo hay console logging básico de ASP.NET Core. No hay logs persistentes, no hay rolling de archivos, no hay forma de diagnosticar problemas en producción.

**Solución:** Instalar y configurar Serilog:
```bash
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Sinks.File
```

Configurar en `Program.cs`:
```csharp
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/api-.log", rollingInterval: RollingInterval.Day)
    .Enrich.FromLogContext()
    .MinimumLevel.Information()
    .CreateLogger();

builder.Host.UseSerilog();
```

**Archivos a modificar:**
- `BarberPro.csproj` (agregar paquetes)
- `Program.cs` (configurar Serilog)
- `appsettings.template.json` (agregar sección Serilog)

---

### A5. Request logging middleware

**Problema:** No hay registro de qué endpoints se consumen, cuánto tardan, ni qué usuario hace qué. Imposible diagnosticar problemas de rendimiento o uso.

**Solución:** Crear middleware que loguee cada request:
```
[INFO] 2026-07-23 15:30:12 POST /api/auth/login → 200 (45ms)
[INFO] 2026-07-23 15:30:15 GET /api/barberos → 200 (12ms) user=admin@barberpro.com
[WARN] 2026-07-23 15:31:00 POST /api/auth/login → 401 (320ms) ip=10.10.5.107
```

**Archivos a crear:**
- `Middleware/RequestLoggingMiddleware.cs` (nuevo)

**Nota:** Esta tarea depende de A4 (Serilog debe estar configurado primero).

---

### A6. Output Caching en endpoints GET de listas

**Problema:** Endpoints como `/barberos`, `/servicios`, `/clientes` consultan la BD en cada request, pero estos datos cambian muy raramente.

**Solución:** Agregar output caching con TTL de 30 segundos para endpoints GET de listas:
```csharp
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(policy => policy.Expire(TimeSpan.FromSeconds(30)));
});
```

**Archivos a modificar:**
- `Program.cs`
- `Controllers/` (agregar tags de cache por entidad para invalidar al mutar)

---

## FASE B — Funcionalidad faltante

Prioridad: **MEDIA**. Mejoran significativamente la API pero no son bloqueantes.

### B1. Paginación genérica

**Problema:** Todos los endpoints GET de listas devuelven todos los registros sin paginación. Con datos reales esto será un problema de rendimiento.

**Solución:** Crear DTO genérico de paginación:
```csharp
public class PaginatedResponse<T>
{
    public List<T> Data { get; set; } = new();
    public int Total { get; set; }
    public int Pagina { get; set; }
    public int TotalPaginas { get; set; }
}
```

Aplicar parámetros `?pagina=1&porPagina=20` en todos los GET de listas:
- `GET /api/barberos`
- `GET /api/clientes`
- `GET /api/servicios`
- `GET /api/citas`
- `GET /api/cierre-caja`

**Archivos a crear/modificar:**
- `DTOs/PaginatedResponse.cs` (nuevo)
- Todos los controllers con GET de listas

---

### B2. Cierre de caja automático programado

**Problema:** Si el encargado olvida cerrar la caja, no hay datos del día. No hay mecanismo de respaldo.

**Solución:** `IHostedService` que a las 23:59 verifique si el cierre del día fue hecho. Si no, ejecutarlo automáticamente.

**Archivos a crear:**
- `Services/CierreCajaAutoService.cs` (nuevo)

---

### B3. Audit trail

**Problema:** No hay registro de quién hizo qué cambio. Si un encargado modifica un precio o cancela una cita, no queda evidencia.

**Solución:** Crear entidad `AuditLog`:
```csharp
public class AuditLog
{
    public int Id { get; set; }
    public int? UsuarioId { get; set; }
    public string Accion { get; set; } = null!;       // "CREAR", "ACTUALIZAR", "ELIMINAR"
    public string Entidad { get; set; } = null!;      // "Cita", "Servicio"
    public string EntidadCodigo { get; set; } = null!; // "CITA-001"
    public string? DetallesJson { get; set; }          // {"campo": "precio", "antes": 80, "despues": 100}
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}
```

Registrar automáticamente en cada POST/PUT/DELETE de los controllers.

**Archivos a crear/modificar:**
- `Dominio/AuditLog.cs` (nuevo)
- `Data/AppDbContext.cs` (agregar DbSet)
- `Migrations/` (nueva migración)
- Todos los controllers (registrar auditoría)

---

### B4. FluentValidation

**Problema:** La validación actual usa DataAnnotations que son básicas. No se pueden hacer reglas como "la fecha de la cita no puede ser en el pasado" o "el barbero debe estar activo al crear una cita".

**Solución:** Instalar FluentValidation y crear validadores:
```bash
dotnet add package FluentValidation.AspNetCore
```

Ejemplo:
```csharp
public class CitaRequestValidator : AbstractValidator<CitaRequestDto>
{
    public CitaRequestValidator()
    {
        RuleFor(x => x.Fecha)
            .GreaterThan(DateTime.Today)
            .WithMessage("La fecha de la cita no puede ser en el pasado.");
        RuleFor(x => x.BarberoCodigo)
            .MustAsync(async (codigo, ct) => /* verificar que el barbero existe y está activo */)
            .WithMessage("El barbero especificado no está activo.");
    }
}
```

**Archivos a crear/modificar:**
- `BarberPro.csproj` (agregar paquete)
- `Validators/` (nueva carpeta con validadores por entidad)
- `Program.cs` (registrar FluentValidation)

---

### B5. Swagger con documentación completa

**Problema:** El Swagger no tiene descripciones de endpoints, ejemplos de request/response, ni códigos de error documentados.

**Solución:** Agregar `[ProducesResponseType]` y `[SwaggerOperation]` a todos los actions de los controllers:
```csharp
[HttpGet]
[ProducesResponseType(typeof(List<BarberoResponseDto>), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
[SwaggerOperation("Lista todos los barberos activos")]
public async Task<ActionResult<IEnumerable<BarberoResponseDto>>> GetAll() { }
```

**Archivos a modificar:**
- Todos los controllers

---

## Resumen de esfuerzo

| Fase | Tareas | Complejidad | Impacto |
|------|--------|-------------|---------|
| **A — Seguridad** | 6 | Media-Alta | Crítico para producción |
| **B — Funcionalidad** | 5 | Media | API completa y profesional |
| **Total** | **11** | | |

### Dependencias entre tareas

```
A4 (Serilog) → A5 (Request logging)   [A5 necesita logging configurado]
A3 (CodigoService) → independiente
A1 (Token cleanup) → independiente
A2 (State machine) → independiente
A6 (Output caching) → independiente

B1 (Paginación) → independiente
B2 (Cierre automático) → independiente
B3 (Audit trail) → independiente
B4 (FluentValidation) → independiente
B5 (Swagger docs) → independiente
```

### Orden de implementación sugerido

1. **Primero:** A4 + A5 (logging base para diagnosticar todo lo demás)
2. **Segundo:** A1 + A2 + A3 (correcciones de seguridad)
3. **Tercero:** A6 (output caching)
4. **Cuarto:** B1 + B3 (paginación + audit trail)
5. **Quinto:** B2 + B4 + B5 (lo demás según prioridad)

### Equipo sugerido

Si el equipo tiene **2 personas backend**, distribución:

| Persona | Tareas |
|---------|--------|
| **Backend 1** | A1, A4, A5, A6, B1, B3, B5 |
| **Backend 2** | A2, A3, B2, B4 |

---

## Endpoints actuales (referencia rápida)

| Método | Ruta | Auth | Descripción |
|--------|------|------|-------------|
| POST | `/api/Auth/setup` | No | Configuración inicial (crear admin) |
| POST | `/api/Auth/login` | No | Login (devuelve JWT + refresh token) |
| POST | `/api/Auth/refresh` | No | Renovar access token |
| POST | `/api/Auth/logout` | `[Authorize]` | Revocar refresh token |
| POST | `/api/Auth/cambiar-password` | `[Authorize]` | Cambiar contraseña |
| GET | `/api/Auth/usuarios` | `[Authorize(Roles="Encargado")]` | Listar usuarios |
| POST | `/api/Auth/usuarios` | `[Authorize(Roles="Encargado")]` | Crear usuario |
| PUT | `/api/Auth/usuarios/{id}` | `[Authorize(Roles="Encargado")]` | Actualizar usuario |
| DELETE | `/api/Auth/usuarios/{id}` | `[Authorize(Roles="Encargado")]` | Eliminar usuario |
| GET | `/api/Barberos` | `[Authorize]` | Listar barberos |
| GET | `/api/Barberos/{codigo}` | `[Authorize]` | Barbero por código |
| POST | `/api/Barberos` | `[Authorize(Roles="Encargado")]` | Crear barbero |
| PUT | `/api/Barberos/{codigo}` | `[Authorize(Roles="Encargado")]` | Actualizar barbero |
| POST | `/api/Barberos/{codigo}/credenciales` | `[Authorize(Roles="Encargado")]` | Asignar credenciales |
| DELETE | `/api/Barberos/{codigo}` | `[Authorize(Roles="Encargado")]` | Soft delete barbero |
| GET | `/api/Clientes` | `[Authorize]` | Listar clientes |
| GET | `/api/Clientes/{codigo}` | `[Authorize]` | Cliente por código |
| POST | `/api/Clientes` | `[Authorize(Roles="Encargado")]` | Crear cliente |
| PUT | `/api/Clientes/{codigo}` | `[Authorize(Roles="Encargado")]` | Actualizar cliente |
| DELETE | `/api/Clientes/{codigo}` | `[Authorize(Roles="Encargado")]` | Soft delete cliente |
| GET | `/api/Servicios` | `[Authorize]` | Listar servicios |
| GET | `/api/Servicios/{codigo}` | `[Authorize]` | Servicio por código |
| POST | `/api/Servicios` | `[Authorize(Roles="Encargado")]` | Crear servicio |
| PUT | `/api/Servicios/{codigo}` | `[Authorize(Roles="Encargado")]` | Actualizar servicio |
| DELETE | `/api/Servicios/{codigo}` | `[Authorize(Roles="Encargado")]` | Soft delete servicio |
| GET | `/api/Citas` | `[Authorize]` | Listar citas |
| GET | `/api/Citas/mis-citas` | `[Authorize(Roles="Barbero")]` | Citas del barbero |
| GET | `/api/Citas/{codigo}` | `[Authorize]` | Cita por código |
| POST | `/api/Citas` | `[Authorize(Roles="Encargado")]` | Crear cita |
| PUT | `/api/Citas/{codigo}` | `[Authorize(Roles="Encargado")]` | Actualizar cita |
| PUT | `/api/Citas/{codigo}/status` | `[Authorize]` | Cambiar estado |
| GET | `/api/Dashboard/stats` | `[Authorize(Roles="Encargado")]` | Estadísticas globales |
| GET | `/api/Dashboard/stats-personales` | `[Authorize(Roles="Barbero")]` | Estadísticas del barbero |
| GET | `/api/Dashboard/buscar` | `[Authorize(Roles="Encargado")]` | Buscar citas por nombre |
| GET | `/api/Init/encargado` | `[Authorize(Roles="Encargado")]` | Datos iniciales encargado |
| GET | `/api/Init/barbero` | `[Authorize(Roles="Barbero")]` | Datos iniciales barbero |
| POST | `/api/cierre-caja` | `[Authorize(Roles="Encargado")]` | Ejecutar cierre diario |
| GET | `/api/cierre-caja` | `[Authorize(Roles="Encargado")]` | Listar cierres |
| GET | `/api/cierre-caja/{id}` | `[Authorize(Roles="Encargado")]` | Cierre por ID |
| GET | `/health` | No | Health check |

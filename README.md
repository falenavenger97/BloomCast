# 🌸 BloomCast

**Sistema web de gestión de inventario, ventas y predicción de demanda para orquídeas.**

BloomCast está desarrollado con **.NET 8**, ASP.NET Core MVC, ASP.NET Core Web API, Entity Framework Core y PostgreSQL. El sistema incorpora gestión de lotes bajo criterio **FEFO**, control de ventas, administración de usuarios, autenticación con **MFA/TOTP** y autorización mediante **JWT**.

---

## Arquitectura

BloomCast está separado en tres proyectos principales:

```text
BloomCast.Web
    │
    │ HTTP / JSON + JWT
    ▼
BloomCast.Api
    │
    │ Entity Framework Core
    ▼
PostgreSQL
```

Y un proyecto compartido para los contratos:

```text
BloomCast.Contracts
```

### Responsabilidades

#### `BloomCast`
Frontend MVC/Razor.

Se encarga de:

- Renderizar las vistas.
- Manejar la navegación.
- Mantener la sesión del usuario.
- Mantener la cookie de autenticación MVC.
- Consumir `BloomCast.Api` mediante `HttpClient`.
- Enviar automáticamente el JWT a los endpoints protegidos.

El frontend **no accede directamente a PostgreSQL**.

#### `BloomCast.Api`
Backend REST desarrollado con ASP.NET Core Web API.

Se encarga de:

- Acceso a PostgreSQL mediante Entity Framework Core.
- Reglas de negocio.
- Gestión de lotes.
- Gestión de ventas.
- Gestión de usuarios.
- Dashboard.
- Autenticación.
- MFA.
- Emisión y validación de JWT.
- Autorización por roles.

#### `BloomCast.Contracts`
Biblioteca compartida que contiene los DTO utilizados para la comunicación entre Web y API.

---

## Tecnologías utilizadas

- .NET 8
- ASP.NET Core MVC
- ASP.NET Core Web API
- Razor Views
- Entity Framework Core 8
- PostgreSQL
- Npgsql
- JWT Bearer Authentication
- Cookie Authentication
- BCrypt
- Otp.NET
- QRCoder
- Chart.js
- HTML
- CSS
- JavaScript

El proyecto también cuenta con componentes de predicción desarrollados en Python utilizando **Prophet** y **XGBoost**, cuya integración como servicio independiente forma parte de la arquitectura predictiva de BloomCast.

---

## Base de datos

Base de datos utilizada:

```text
bloomcast
```

### Tablas principales

| Tabla | Campos principales |
|---|---|
| `roles` | id, nombre |
| `usuarios` | id, nombrecompleto, correo, passwordhash, rolid, activo, mfa_secret, mfa_habilitado |
| `orquideas` | id, variedad, tipo, preciobase, diasvidacomercial |
| `insumos` | id, nombre, unidadmedida, stockactual |
| `recetas_armado` | id, orquideaid, insumoid, cantidadrequerida |
| `lotes` | id, orquideaid, codigolote, cantidadinicial, cantidadactual, fechaingreso, fechafloracion, fechacaducidad |
| `predicciones_demanda` | id, fechageneracion, fechaobjetivo, orquideaid, cantidadestimada |
| `ventas` | id, usuarioid, fechaventa, total |
| `detalle_ventas` | id, ventaid, loteid, cantidad, preciounitario, subtotal |

---

# Autenticación y seguridad

BloomCast utiliza dos mecanismos complementarios de autenticación.

### Frontend

ASP.NET Core Cookie Authentication mantiene la sesión autenticada dentro de las páginas MVC.

### Backend

`BloomCast.Api` utiliza **JWT Bearer Authentication** para proteger sus endpoints.

El flujo general es:

```text
Usuario
  │
  ▼
BloomCast.Web
  │
  │ correo + contraseña
  ▼
POST /api/auth/login
  │
  ▼
BloomCast.Api
  │
  ├── valida usuario
  ├── valida BCrypt
  └── genera challenge MFA
           │
           ▼
      Código TOTP
           │
           ▼
POST /api/auth/mfa
           │
           ├── valida MFA
           └── genera JWT
                    │
                    ▼
             BloomCast.Web
                    │
                    │ Authorization:
                    │ Bearer <JWT>
                    ▼
              BloomCast.Api
```

---

## MFA / TOTP

BloomCast implementa autenticación multifactor mediante **TOTP**.

Compatible con aplicaciones como:

- Google Authenticator
- Microsoft Authenticator
- Authy
- Otros clientes compatibles con TOTP

La configuración utiliza:

```text
Algoritmo: SHA1
Dígitos:   6
Periodo:   30 segundos
Issuer:    BloomCast
```

Cuando un usuario todavía no tiene MFA configurado:

```text
Login
  ↓
Generación de secreto
  ↓
Código QR
  ↓
Escaneo con Authenticator
  ↓
Código TOTP
  ↓
Verificación
  ↓
MFA habilitado
```

---

# Roles

BloomCast actualmente maneja principalmente:

```text
Administrador
Vendedor
```

La autorización se realiza tanto en la interfaz como en el backend.

Por ejemplo:

```csharp
[Authorize]
```

permite acceso a cualquier usuario autenticado.

Mientras:

```csharp
[Authorize(Roles = "Administrador")]
```

restringe operaciones administrativas.

Por lo tanto, ocultar una opción en la interfaz **no es la única medida de seguridad**: la API también verifica los permisos.

---

# Gestión de lotes

BloomCast administra los lotes de orquídeas utilizando el criterio:

## FEFO — First Expired, First Out

Los lotes con vencimiento más próximo tienen prioridad de salida.

Los estados utilizados son:

```text
Agotado
Vencido
Crítico
Próximo
Estable
Sin fecha
```

### Clasificación

```text
Agotado
stock <= 0

Vencido
fecha < hoy

Crítico
0 a 7 días restantes

Próximo
8 a 30 días restantes

Estable
31 días o más
```

La vida comercial utilizada para calcular la caducidad depende de:

```text
orquideas.diasvidacomercial
```

Al registrar un lote:

```text
FechaIngreso = fecha actual
FechaFloracion = fecha actual

FechaCaducidad =
    FechaIngreso + DiasVidaComercial
```

---

## Código de lote

BloomCast genera automáticamente códigos con la estructura:

```text
LOT-YYYY-MM-ORQUIDEA-CORRELATIVO
```

Ejemplo:

```text
LOT-2026-09-5-2
```

---

# Gestión de ventas

El módulo de ventas permite:

- Listar ventas.
- Buscar ventas.
- Filtrar por fecha.
- Consultar detalles.
- Registrar nuevas ventas.
- Seleccionar lotes disponibles.
- Descontar inventario.
- Eliminar ventas.
- Restaurar automáticamente el stock al eliminar una venta.

---

## Venta transaccional

La creación de una venta se ejecuta dentro de una transacción.

Proceso:

```text
Validar usuario
      ↓
Bloquear lotes
SELECT ... FOR UPDATE
      ↓
Validar vencimiento
      ↓
Validar stock
      ↓
Calcular precios
      ↓
Crear venta
      ↓
Crear detalles
      ↓
Descontar inventario
      ↓
COMMIT
```

Si ocurre algún error:

```text
ROLLBACK
```

Esto ayuda a evitar inconsistencias de inventario cuando existen operaciones concurrentes.

---

## Lotes disponibles para venta

Solo pueden venderse lotes que:

```text
CantidadActual > 0
FechaCaducidad != null
FechaCaducidad >= hoy
```

Los lotes son ordenados por prioridad FEFO:

```text
FechaCaducidad
      ↓
FechaIngreso
```

---

## Eliminación de ventas

Eliminar una venta requiere:

```text
Usuario autenticado
        +
Rol Administrador
        +
Confirmación de contraseña
```

La API obtiene la identidad del administrador directamente desde el JWT.

No se utiliza un ID fijo de administrador.

Al eliminar:

```text
Venta
  ↓
Restaurar stock
  ↓
Eliminar detalle_ventas
  ↓
Eliminar ventas
  ↓
COMMIT
```

---

# Gestión de usuarios

El módulo administrativo permite:

- Listar usuarios.
- Buscar por nombre o correo.
- Filtrar por rol.
- Filtrar por estado.
- Crear usuarios.
- Editar usuarios.
- Cambiar contraseña.
- Resetear MFA.
- Activar/desactivar usuarios.

Los usuarios se muestran prioritariamente como:

```text
1. Activos
2. Administradores
3. Orden alfabético
```

La eliminación lógica se realiza mediante:

```text
activo = false
```

No se elimina físicamente el registro.

Además, un administrador no puede desactivar su propia cuenta.

---

# Dashboard

El Dashboard consume:

```http
GET /api/dashboard
```

y presenta información consolidada del sistema.

### Indicadores

- Total de variedades de orquídeas.
- Total de lotes activos.
- Ventas del día.
- Lotes críticos.

### Próximos vencimientos

Muestra lotes con:

```text
stock > 0
fecha de caducidad >= hoy
fecha de caducidad <= próximos 30 días
```

Los lotes vencidos no se consideran lotes próximos a caducar.

### Predicciones

Las predicciones se obtienen desde:

```text
predicciones_demanda
```

relacionadas con:

```text
orquideas
```

y se presentan mediante gráficos de demanda por variedad y fecha objetivo.

---

# Principales endpoints

## Auth

```http
POST /api/auth/login
POST /api/auth/mfa
```

## Dashboard

```http
GET /api/dashboard
```

Requiere autenticación.

## Lotes

```http
GET  /api/lots
GET  /api/lots/orchids
POST /api/lots
```

## Ventas

```http
GET  /api/sales
GET  /api/sales/{id}
GET  /api/sales/users
GET  /api/sales/available-lots
POST /api/sales
POST /api/sales/{id}/delete-with-password
```

La eliminación requiere rol:

```text
Administrador
```

## Usuarios

```http
GET    /api/users
GET    /api/users/{id}
GET    /api/users/roles
POST   /api/users
PUT    /api/users/{id}
DELETE /api/users/{id}
```

Requieren rol:

```text
Administrador
```

---

# Estructura del proyecto

```text
BloomCast/
│
├── BloomCast.Api/
│   ├── Controllers/
│   │   ├── AuthController.cs
│   │   ├── DashboardController.cs
│   │   ├── LotsController.cs
│   │   ├── SalesController.cs
│   │   └── UsersController.cs
│   │
│   ├── Data/
│   │   └── BloomCastDbContext.cs
│   │
│   ├── Models/
│   │
│   ├── Services/
│   │   ├── MfaService.cs
│   │   └── JwtTokenService.cs
│   │
│   ├── Program.cs
│   └── BloomCast.Api.csproj
│
├── BloomCast.Contracts/
│   ├── Auth/
│   ├── Dashboard/
│   ├── Lots/
│   ├── Sales/
│   ├── Users/
│   └── BloomCast.Contracts.csproj
│
├── Controllers/
│   ├── AccountController.cs
│   ├── HomeController.cs
│   ├── LotsController.cs
│   ├── SalesController.cs
│   └── UsersController.cs
│
├── Models/
│   └── Models.cs
│
├── Services/
│   └── Api/
│       ├── ApiAuthorizationHandler.cs
│       ├── AuthApiService.cs
│       ├── DashboardApiService.cs
│       ├── LotsApiService.cs
│       ├── SalesApiService.cs
│       └── UsersApiService.cs
│
├── Views/
│   ├── Account/
│   ├── Home/
│   ├── Lots/
│   ├── Sales/
│   ├── Users/
│   └── Shared/
│
├── wwwroot/
│
├── BloomCast.csproj
├── BloomCast.sln
├── Program.cs
└── appsettings.json
```

---

# Requisitos

Para ejecutar BloomCast localmente necesitas:

- .NET SDK 8
- PostgreSQL
- Base de datos BloomCast configurada
- Git

Opcionalmente:

- Visual Studio 2022
- Visual Studio Code
- pgAdmin

---

# Instalación

## 1. Clonar el repositorio

```bash
git clone <URL_DEL_REPOSITORIO>
cd BloomCast
```

---

## 2. Restaurar dependencias

```bash
dotnet restore BloomCast.sln
```

---

# Configuración de secretos

BloomCast utiliza **.NET User Secrets** para evitar almacenar credenciales y claves JWT dentro del repositorio.

Los secretos principales pertenecen a:

```text
BloomCast.Api
```

---

## PostgreSQL

Configura la cadena de conexión:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "TU_CONNECTION_STRING" --project BloomCast.Api/BloomCast.Api.csproj
```

Ejemplo de estructura:

```text
Host=localhost;
Port=5432;
Database=bloomcast;
Username=postgres;
Password=TU_PASSWORD
```

No almacenes credenciales reales dentro de `appsettings.json`.

---

## JWT

Genera una clave segura.

En PowerShell:

```powershell
[Convert]::ToBase64String(
    [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(64)
)
```

Luego configura:

```bash
dotnet user-secrets set "Jwt:Key" "TU_CLAVE_GENERADA" --project BloomCast.Api/BloomCast.Api.csproj
```

Puedes verificar los secretos configurados con:

```bash
dotnet user-secrets list --project BloomCast.Api/BloomCast.Api.csproj
```

Deberían existir al menos:

```text
ConnectionStrings:DefaultConnection
Jwt:Key
```

---

# Configuración JWT

`BloomCast.Api/appsettings.json` contiene únicamente la configuración no sensible:

```json
"Jwt": {
  "Issuer": "BloomCast.Api",
  "Audience": "BloomCast.Web",
  "Key": "",
  "ExpirationMinutes": 30
}
```

La clave real se obtiene mediante User Secrets.

---

# Configuración de la API en el frontend

El proyecto Web utiliza:

```json
"ApiSettings": {
  "BaseUrl": "http://localhost:5209/"
}
```

Ajusta el puerto si `BloomCast.Api` utiliza otro puerto en tu entorno.

---

# Ejecutar BloomCast

BloomCast Web y BloomCast API deben levantarse **como procesos separados**.

## Terminal 1 — API

```bash
dotnet run --project BloomCast.Api
```

En el entorno actual de desarrollo la API puede estar disponible en:

```text
http://localhost:5209
```

Consulta la salida de `dotnet run` o `launchSettings.json` para confirmar el puerto.

---

## Terminal 2 — Web

```bash
dotnet run --project BloomCast.csproj
```

Abre en el navegador la URL indicada por ASP.NET Core.

---

## Importante

No basta con ejecutar únicamente el frontend.

BloomCast utiliza:

```text
Web
 ↓
API
 ↓
PostgreSQL
```

Por lo tanto, si `BloomCast.Api` no está ejecutándose, las vistas no podrán cargar los datos.

---

# Compilar solución

```bash
dotnet clean BloomCast.sln
dotnet restore BloomCast.sln
dotnet build BloomCast.sln -t:Rebuild
```

La compilación correcta debe incluir:

```text
BloomCast.Contracts  ✅
BloomCast.Api        ✅
BloomCast            ✅
```

---

# Buenas prácticas de seguridad

No subir al repositorio:

```text
Contraseñas
Connection strings reales
Jwt:Key
Secrets de producción
```

Los secretos deben configurarse mediante:

```text
dotnet user-secrets
```

durante desarrollo, o mediante variables de entorno / gestores de secretos en producción.

---

# Estado actual de la arquitectura

```text
                 ┌──────────────────────┐
                 │   BloomCast.Contracts│
                 └──────────┬───────────┘
                            │
            ┌───────────────┴───────────────┐
            │                               │
            ▼                               ▼
┌─────────────────────┐          ┌─────────────────────┐
│    BloomCast.Web    │          │    BloomCast.Api    │
│                     │ HTTP/JWT │                     │
│ MVC / Razor         ├─────────►│ REST API            │
│ Cookie Auth         │          │ JWT Authorization   │
│ HttpClient          │          │ EF Core             │
└─────────────────────┘          └──────────┬──────────┘
                                           │
                                           ▼
                                  ┌─────────────────────┐
                                  │     PostgreSQL      │
                                  └─────────────────────┘
```

El frontend ya no accede directamente a PostgreSQL.

Toda operación de datos pasa por `BloomCast.Api`.

---

# 🌸 BloomCast

BloomCast busca apoyar la gestión operativa de inventario perecedero de orquídeas mediante la combinación de:

- gestión de lotes;
- estrategia FEFO;
- control de ventas;
- trazabilidad de inventario;
- predicción de demanda;
- autenticación segura;
- MFA;
- autorización basada en roles.

El objetivo es proporcionar una plataforma centralizada que facilite la toma de decisiones sobre inventario y demanda dentro de la operación.

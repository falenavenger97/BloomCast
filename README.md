# 🌸 BloomCast — .NET 8 MVC

Sistema de predicción de demanda de orquídeas con login seguro + MFA (TOTP).

---

## Estructura real de la BD (leída del diagrama)

| Tabla | Campos clave |
|-------|-------------|
| `roles` | id, nombre |
| `usuarios` | id, nombrecompleto, correo, passwordhash, rolid, activo, mfa_secret, mfa_habilitado |
| `orquideas` | id, variedad, tipo, preciobase |
| `insumos` | id, nombre, unidadmedida, stockactual |
| `recetas_armado` | id, orquideaid, insumoid, cantidadrequerida |
| `lotes` | id, orquideaid, codigolote, cantidadinicial, cantidadactual, fechaingreso, fechafloracion, **fechacaducidad** |
| `predicciones_demanda` | id, fechageneracion, **fechaobjetivo**, **orquideaid**, cantidadestimada |
| `ventas` | id, usuarioid, fechaventa, total |
| `detalle_ventas` | id, ventaid, loteid, cantidad, preciounitario, subtotal |

---

## Requisitos

- .NET SDK 8.0
- PostgreSQL 14 (base de datos `bloomcast` ya existente)

---

## Setup en 2 pasos

### 1 · Agregar columnas MFA (única modificación a tu BD)

```sql
ALTER TABLE usuarios
    ADD COLUMN IF NOT EXISTS mfa_secret     TEXT,
    ADD COLUMN IF NOT EXISTS mfa_habilitado BOOLEAN NOT NULL DEFAULT FALSE;
```

> La app también lo hace automáticamente al iniciar.

### 2 · Ejecutar

```bash
cd BloomCast
dotnet restore
dotnet run
```

La app arranca en `https://localhost:5001`.

---

## Flujo de login + MFA

```
POST /Account/Login  (correo + password)
       │
       ├─ Primera vez ──► MfaSetup (QR para escanear) ──► Verificar TOTP ──► Dashboard
       └─ Ya configurado ──► Mfa (código 6 dígitos)   ──► Dashboard
```

---

## Dashboard

- **Gráfico de líneas** — `predicciones_demanda` JOIN `orquideas`, agrupado por `fechaobjetivo` (mes) y `variedad`
- **Tarjetas de lotes** — `lotes` donde `fechacaducidad ≤ hoy + 30 días`, ordenados por urgencia:
  - 🔴 `< 7 días` (crítico)
  - 🟡 `8–15 días` (aviso)
  - 🟢 `16–30 días` (ok)
- **Stats**: variedades totales, lotes activos, ventas del día (Bs), lotes críticos

---

## Estructura del proyecto

```
BloomCast/
├── Controllers/
│   ├── AccountController.cs   # Login · MfaSetup · Logout
│   ├── UserController.cs
│   ├── SalesController.cs
│   └── HomeController.cs      # Dashboard [Authorize]
├── Data/
│   └── BloomCastDbContext.cs  # EF Core — todas las tablas
├── Models/
│   └── Models.cs              # Entidades + ViewModels
├── Services/
│   └── MfaService.cs          # TOTP (OtpNet) + QR (QRCoder)
├── Views/
│   ├── Account/Login · Mfa · MfaSetup
│   ├── Home/Index             # Dashboard
│   ├── Shared/_Layout.cshtml
│   ├── Users/Index · Edit · Create
│   └── Sales/Index · Create · _DeletePasswordModal · _VentaDetailsPartial
├── bloomcast_schema.sql       # Solo el ALTER TABLE para MFA
├── appsettings.json
└── Program.cs
```

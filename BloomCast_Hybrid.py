"""
BloomCast - Modelo híbrido mensual-diario XGBoost + Prophet para demanda de orquídeas.

Versión recomendada para datos de bajo volumen diario:
1) Predice la demanda MENSUAL por orquideaid con XGBoost + Prophet.
2) Selecciona la mejor combinación usando SOLO validación 2024.
3) Reentrena con 2021-2024.
4) Predice 2025 a nivel mensual.
5) Distribuye cada predicción mensual a días usando patrones históricos diarios,
   festivos, días cercanos a festivos, día de semana y día del mes.
6) Exporta métricas, gráficos, CSV/Excel, guarda modelos e inserta predicciones diarias.

Motivo técnico:
- En la base de BloomCast la demanda diaria por orquideaid es de bajo conteo.
- MAE diario puede ser bajo, pero WMAPE diario queda alto porque un error de 1 unidad
  representa 50%, 100% o más cuando la venta real diaria es 1 o 2.
- La métrica porcentual principal se evalúa a nivel mensual por orquideaid.
- La salida hacia predicciones_demanda sigue siendo diaria.

Dependencias:
pandas numpy sqlalchemy psycopg2-binary scikit-learn xgboost prophet matplotlib joblib openpyxl

Variables de entorno:
DB_HOST, DB_PORT, DB_NAME, DB_USER, DB_PASSWORD
Opcional: DATABASE_URL=postgresql+psycopg2://usuario:password@host:puerto/base
"""

from __future__ import annotations

import argparse
import json
import logging
import os
import warnings
from dataclasses import asdict, dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

import joblib
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
import psycopg2  # requerido explícitamente
from prophet import Prophet
from psycopg2.extras import execute_values
from sklearn.compose import ColumnTransformer
from sklearn.impute import SimpleImputer
from sklearn.metrics import mean_absolute_error, mean_squared_error
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import OneHotEncoder
from sqlalchemy import create_engine, text
from sqlalchemy.engine import Engine
from xgboost import XGBRegressor

warnings.filterwarnings("ignore")

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
)
log = logging.getLogger("BloomCastMensualDiario")


# =============================================================================
# CONFIGURACIÓN
# =============================================================================

@dataclass
class BloomCastConfig:
    inicio_historico: str = "2021-01-01"
    fin_historico: str = "2025-12-31"

    train_inicio: str = "2021-01-01"
    train_fin_validacion: str = "2023-12-31"
    valid_inicio: str = "2024-01-01"
    valid_fin: str = "2024-12-31"

    train_fin_final: str = "2024-12-31"
    pred_inicio: str = "2025-01-01"
    pred_fin: str = "2025-12-31"

    dias_antes_festivo: int = 7
    dias_despues_festivo: int = 3
    random_state: int = 42
    target_wmape: float = 18.0

    output_dir: str = "outputs_bloomcast"
    model_dir: str = "modelos_bloomcast"

    insertar_bd: bool = True
    crear_constraint_unica: bool = True

    # Métrica porcentual principal: mensual por orquideaid.
    metrica_principal: str = "WMAPE_MENSUAL"

    # Grillas pequeñas: se ajustan solo con 2024.
    xgb_param_grid: List[Dict[str, Any]] = field(default_factory=lambda: [
        {
            "n_estimators": 300,
            "learning_rate": 0.050,
            "max_depth": 2,
            "min_child_weight": 2,
            "subsample": 0.90,
            "colsample_bytree": 0.90,
            "reg_alpha": 0.05,
            "reg_lambda": 2.00,
            "objective": "reg:squarederror",
        },
        {
            "n_estimators": 500,
            "learning_rate": 0.035,
            "max_depth": 3,
            "min_child_weight": 2,
            "subsample": 0.90,
            "colsample_bytree": 0.90,
            "reg_alpha": 0.10,
            "reg_lambda": 3.00,
            "objective": "reg:squarederror",
        },
        {
            "n_estimators": 650,
            "learning_rate": 0.025,
            "max_depth": 3,
            "min_child_weight": 3,
            "subsample": 0.85,
            "colsample_bytree": 0.85,
            "reg_alpha": 0.20,
            "reg_lambda": 4.00,
            "objective": "reg:tweedie",
            "tweedie_variance_power": 1.20,
        },
    ])

    prophet_param_grid: List[Dict[str, Any]] = field(default_factory=lambda: [
        {
            "changepoint_prior_scale": 0.05,
            "seasonality_prior_scale": 5.0,
            "holidays_prior_scale": 5.0,
            "seasonality_mode": "additive",
            "yearly_fourier_order": 4,
        },
        {
            "changepoint_prior_scale": 0.10,
            "seasonality_prior_scale": 10.0,
            "holidays_prior_scale": 10.0,
            "seasonality_mode": "additive",
            "yearly_fourier_order": 6,
        },
        {
            "changepoint_prior_scale": 0.20,
            "seasonality_prior_scale": 15.0,
            "holidays_prior_scale": 15.0,
            "seasonality_mode": "multiplicative",
            "yearly_fourier_order": 8,
        },
    ])

    pesos_ensemble: List[float] = field(default_factory=lambda: [
        0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0
    ])
    alphas_ajuste: List[float] = field(default_factory=lambda: [0.25, 0.50, 0.75, 1.00])
    escalas_calibracion: List[float] = field(default_factory=lambda: [
        0.70, 0.75, 0.80, 0.85, 0.90, 0.95, 1.00, 1.05, 1.10, 1.15, 1.20, 1.25, 1.30
    ])


TARGET_DAILY = "cantidad_vendida"
TARGET_MONTHLY = "cantidad_mensual"

MONTH_NUMERIC_FEATURES = [
    "anio", "mes", "trimestre", "tendencia_meses", "sin_anual", "cos_anual",
    "festivos_mes", "cercanos_festivo_mes", "dias_mes", "preciobase",
    "lag_1", "lag_2", "lag_3", "lag_12",
    "rolling_3", "rolling_6", "rolling_12",
    "promedio_mismo_mes_orquidea",
]
MONTH_CATEGORICAL_FEATURES = ["orquideaid_cat", "variedad", "tipo"]
MONTH_FEATURES = MONTH_NUMERIC_FEATURES + MONTH_CATEGORICAL_FEATURES


# =============================================================================
# UTILIDADES SQL Y CONEXIÓN
# =============================================================================

def conectar_bd() -> Engine:
    database_url = os.getenv("DATABASE_URL")
    if not database_url:
        host = os.getenv("DB_HOST", "localhost")
        port = os.getenv("DB_PORT", "5432")
        name = os.getenv("DB_NAME", "bloomcast")
        user = os.getenv("DB_USER", "postgres")
        password = os.getenv("DB_PASSWORD", "12345678")
        database_url = f"postgresql+psycopg2://{user}:{password}@{host}:{port}/{name}"

    engine = create_engine(database_url, pool_pre_ping=True)
    with engine.connect() as conn:
        conn.execute(text("SELECT 1"))
    log.info("Conexión a PostgreSQL establecida.")
    return engine


def obtener_columnas_tabla(engine: Engine, tabla: str) -> List[str]:
    sql = """
        SELECT column_name
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = :tabla
        ORDER BY ordinal_position;
    """
    df = pd.read_sql(text(sql), engine, params={"tabla": tabla})
    return df["column_name"].astype(str).tolist()


def elegir_columna(columnas: List[str], candidatas: List[str], tabla: str, descripcion: str) -> str:
    columnas_lower = {c.lower(): c for c in columnas}
    for candidata in candidatas:
        if candidata.lower() in columnas_lower:
            elegida = columnas_lower[candidata.lower()]
            log.info("Usando %s.%s como %s.", tabla, elegida, descripcion)
            return elegida
    raise ValueError(
        f"No se encontró una columna válida para {descripcion} en {tabla}. "
        f"Candidatas: {candidatas}. Columnas reales: {columnas}"
    )


# =============================================================================
# CARGA DE DATOS
# =============================================================================

def cargar_orquideas(engine: Engine) -> pd.DataFrame:
    sql = """
        SELECT
            id AS orquideaid,
            variedad,
            tipo,
            preciobase
        FROM orquideas
        ORDER BY id;
    """
    df = pd.read_sql(text(sql), engine)
    if df.empty:
        raise ValueError("La tabla orquideas está vacía.")
    df["orquideaid"] = df["orquideaid"].astype(int)
    df["variedad"] = df["variedad"].fillna("SIN_VARIEDAD").astype(str)
    df["tipo"] = df["tipo"].fillna("SIN_TIPO").astype(str)
    df["preciobase"] = pd.to_numeric(df["preciobase"], errors="coerce").fillna(0.0).astype(float)
    return df


def cargar_ventas(engine: Engine, inicio: str, fin: str) -> pd.DataFrame:
    columnas_ventas = obtener_columnas_tabla(engine, "ventas")
    col_fecha = elegir_columna(
        columnas_ventas,
        ["fecha", "fechaventa", "fecha_venta", "fecharegistro", "created_at", "createdat"],
        "ventas",
        "fecha de venta",
    )

    sql = f"""
        SELECT
            DATE(v.{col_fecha}) AS fecha,
            l.orquideaid,
            o.variedad,
            o.tipo,
            o.preciobase,
            SUM(dv.cantidad)::float AS cantidad_vendida,
            SUM(dv.subtotal)::float AS total_vendido,
            COUNT(DISTINCT v.id)::integer AS numero_ventas,
            COUNT(dv.id)::integer AS lineas_detalle
        FROM ventas v
        INNER JOIN detalle_ventas dv ON dv.ventaid = v.id
        INNER JOIN lotes l ON l.id = dv.loteid
        INNER JOIN orquideas o ON o.id = l.orquideaid
        WHERE DATE(v.{col_fecha}) BETWEEN :inicio AND :fin
        GROUP BY
            DATE(v.{col_fecha}),
            l.orquideaid,
            o.variedad,
            o.tipo,
            o.preciobase
        ORDER BY fecha, l.orquideaid;
    """
    df = pd.read_sql(text(sql), engine, params={"inicio": inicio, "fin": fin})
    if df.empty:
        log.warning("No se encontraron ventas entre %s y %s.", inicio, fin)
    df["fecha"] = pd.to_datetime(df["fecha"])
    df["orquideaid"] = df["orquideaid"].astype(int)
    df["variedad"] = df["variedad"].fillna("SIN_VARIEDAD").astype(str)
    df["tipo"] = df["tipo"].fillna("SIN_TIPO").astype(str)
    df["preciobase"] = pd.to_numeric(df["preciobase"], errors="coerce").fillna(0.0).astype(float)
    df["cantidad_vendida"] = pd.to_numeric(df["cantidad_vendida"], errors="coerce").fillna(0.0).astype(float)
    df["total_vendido"] = pd.to_numeric(df["total_vendido"], errors="coerce").fillna(0.0).astype(float)
    log.info("Ventas cargadas: %s filas agregadas fecha-orquideaid.", len(df))
    return df


def cargar_festivos(engine: Engine) -> pd.DataFrame:
    columnas = obtener_columnas_tabla(engine, "festivos")
    if not columnas:
        log.warning("No existe la tabla festivos o no tiene columnas. Se continuará sin festivos.")
        return pd.DataFrame(columns=["fecha", "nombre"])

    col_fecha = elegir_columna(columnas, ["fecha", "dia", "date"], "festivos", "fecha de festivo")
    try:
        col_nombre = elegir_columna(columnas, ["nombre", "descripcion", "detalle", "holiday"], "festivos", "nombre de festivo")
        sql = f"SELECT {col_fecha}::date AS fecha, {col_nombre}::text AS nombre FROM festivos ORDER BY {col_fecha};"
    except ValueError:
        sql = f"SELECT {col_fecha}::date AS fecha, 'festivo'::text AS nombre FROM festivos ORDER BY {col_fecha};"

    df = pd.read_sql(text(sql), engine)
    if df.empty:
        return pd.DataFrame(columns=["fecha", "nombre"])
    df["fecha"] = pd.to_datetime(df["fecha"])
    df["nombre"] = df["nombre"].fillna("festivo").astype(str)
    log.info("Festivos cargados: %s.", len(df))
    return df


# =============================================================================
# PREPARACIÓN DIARIA
# =============================================================================

def completar_fechas_faltantes(
    df_ventas: pd.DataFrame,
    df_orquideas: pd.DataFrame,
    inicio: str,
    fin: str,
) -> pd.DataFrame:
    fechas = pd.DataFrame({"fecha": pd.date_range(inicio, fin, freq="D")})
    base = fechas.merge(df_orquideas, how="cross")

    ventas_cols = [
        "fecha", "orquideaid", "cantidad_vendida", "total_vendido",
        "numero_ventas", "lineas_detalle"
    ]
    ventas = df_ventas[ventas_cols].copy() if not df_ventas.empty else pd.DataFrame(columns=ventas_cols)

    df = base.merge(ventas, on=["fecha", "orquideaid"], how="left")
    for col in ["cantidad_vendida", "total_vendido", "numero_ventas", "lineas_detalle"]:
        df[col] = pd.to_numeric(df[col], errors="coerce").fillna(0.0)
    df["cantidad_vendida"] = df["cantidad_vendida"].astype(float)
    df["orquideaid"] = df["orquideaid"].astype(int)
    df = df.sort_values(["orquideaid", "fecha"]).reset_index(drop=True)
    log.info("Calendario diario completado: %s filas.", len(df))
    return df


def agregar_features_festivos_diarias(
    df: pd.DataFrame,
    df_festivos: pd.DataFrame,
    config: BloomCastConfig,
) -> pd.DataFrame:
    df = df.copy()
    df["fecha"] = pd.to_datetime(df["fecha"])
    festivos = pd.to_datetime(df_festivos["fecha"]).drop_duplicates().sort_values() if not df_festivos.empty else pd.Series([], dtype="datetime64[ns]")
    festivo_set = set(festivos.dt.normalize().tolist()) if len(festivos) else set()

    df["is_festivo"] = df["fecha"].dt.normalize().isin(festivo_set).astype(int)
    df["cercano_festivo"] = 0
    df["dias_desde_ultimo_festivo"] = 999
    df["dias_hasta_proximo_festivo"] = 999

    if len(festivos) > 0:
        fechas_unicas = pd.DataFrame({"fecha": df["fecha"].drop_duplicates().sort_values()})
        festivos_norm = festivos.dt.normalize().drop_duplicates().sort_values().reset_index(drop=True)

        for f in festivos_norm:
            mask = (
                (fechas_unicas["fecha"] >= f - pd.Timedelta(days=config.dias_antes_festivo))
                & (fechas_unicas["fecha"] <= f + pd.Timedelta(days=config.dias_despues_festivo))
            )
            fechas_unicas.loc[mask, "cercano_festivo"] = 1

        fechas_array = fechas_unicas["fecha"].values.astype("datetime64[D]")
        fest_array = festivos_norm.values.astype("datetime64[D]")
        desde_vals = []
        hasta_vals = []
        for fecha_np in fechas_array:
            diffs = (fecha_np - fest_array).astype("timedelta64[D]").astype(int)
            prev = diffs[diffs >= 0]
            nxt = -diffs[diffs <= 0]
            desde_vals.append(int(prev.min()) if len(prev) else 999)
            hasta_vals.append(int(nxt.min()) if len(nxt) else 999)
        fechas_unicas["dias_desde_ultimo_festivo"] = desde_vals
        fechas_unicas["dias_hasta_proximo_festivo"] = hasta_vals
        fechas_unicas["cercano_festivo"] = fechas_unicas["cercano_festivo"].fillna(0).astype(int)

        df = df.drop(columns=["cercano_festivo", "dias_desde_ultimo_festivo", "dias_hasta_proximo_festivo"])
        df = df.merge(fechas_unicas, on="fecha", how="left")

    df["cercano_festivo"] = df["cercano_festivo"].fillna(0).astype(int)
    df["dias_desde_ultimo_festivo"] = df["dias_desde_ultimo_festivo"].fillna(999).astype(int)
    df["dias_hasta_proximo_festivo"] = df["dias_hasta_proximo_festivo"].fillna(999).astype(int)
    return df


def preparar_serie_demanda(
    df_ventas: pd.DataFrame,
    df_orquideas: pd.DataFrame,
    df_festivos: pd.DataFrame,
    config: BloomCastConfig,
) -> pd.DataFrame:
    df = completar_fechas_faltantes(df_ventas, df_orquideas, config.inicio_historico, config.fin_historico)
    df = agregar_features_festivos_diarias(df, df_festivos, config)
    df["anio"] = df["fecha"].dt.year
    df["mes"] = df["fecha"].dt.month
    df["dia"] = df["fecha"].dt.day
    df["dia_semana"] = df["fecha"].dt.dayofweek
    df["semana_anio"] = df["fecha"].dt.isocalendar().week.astype(int)
    df["trimestre"] = df["fecha"].dt.quarter
    df["fin_semana"] = df["dia_semana"].isin([5, 6]).astype(int)
    df["periodo_mes"] = df["fecha"].dt.to_period("M").dt.to_timestamp()
    df["orquideaid_cat"] = df["orquideaid"].astype(str)
    return df.sort_values(["orquideaid", "fecha"]).reset_index(drop=True)


# Alias compatible con versiones anteriores.
def generar_features(df: pd.DataFrame, *_args, **_kwargs) -> pd.DataFrame:
    return df.copy()


# =============================================================================
# FEATURES MENSUALES
# =============================================================================

def agregar_features_mensuales_estaticas(df_mensual: pd.DataFrame) -> pd.DataFrame:
    df = df_mensual.copy()
    df["periodo_mes"] = pd.to_datetime(df["periodo_mes"])
    df["anio"] = df["periodo_mes"].dt.year
    df["mes"] = df["periodo_mes"].dt.month
    df["trimestre"] = df["periodo_mes"].dt.quarter
    min_mes = df["periodo_mes"].min()
    df["tendencia_meses"] = (df["periodo_mes"].dt.year - min_mes.year) * 12 + (df["periodo_mes"].dt.month - min_mes.month)
    df["sin_anual"] = np.sin(2 * np.pi * df["mes"] / 12)
    df["cos_anual"] = np.cos(2 * np.pi * df["mes"] / 12)
    df["orquideaid_cat"] = df["orquideaid"].astype(str)
    return df


def preparar_mensual(df_diario: pd.DataFrame) -> pd.DataFrame:
    agg = (
        df_diario.groupby(["periodo_mes", "orquideaid", "variedad", "tipo", "preciobase"], as_index=False)
        .agg(
            cantidad_mensual=("cantidad_vendida", "sum"),
            total_vendido=("total_vendido", "sum"),
            festivos_mes=("is_festivo", "sum"),
            cercanos_festivo_mes=("cercano_festivo", "sum"),
            dias_mes=("fecha", "nunique"),
        )
    )
    agg["cantidad_mensual"] = agg["cantidad_mensual"].astype(float)
    agg = agregar_features_mensuales_estaticas(agg)
    return agg.sort_values(["orquideaid", "periodo_mes"]).reset_index(drop=True)


def generar_features_mensuales(df_mensual: pd.DataFrame) -> pd.DataFrame:
    df = agregar_features_mensuales_estaticas(df_mensual)
    df = df.sort_values(["orquideaid", "periodo_mes"]).reset_index(drop=True)

    for lag in [1, 2, 3, 12]:
        df[f"lag_{lag}"] = df.groupby("orquideaid")[TARGET_MONTHLY].shift(lag)

    for window in [3, 6, 12]:
        df[f"rolling_{window}"] = (
            df.groupby("orquideaid")[TARGET_MONTHLY]
            .transform(lambda s: s.shift(1).rolling(window=window, min_periods=1).mean())
        )

    df["promedio_mismo_mes_orquidea"] = (
        df.groupby(["orquideaid", "mes"])[TARGET_MONTHLY]
        .transform(lambda s: s.shift(1).expanding(min_periods=1).mean())
    )
    return df


def contexto_mensual_desde_diario(df_diario: pd.DataFrame) -> pd.DataFrame:
    ctx = (
        df_diario.groupby(["periodo_mes", "orquideaid", "variedad", "tipo", "preciobase"], as_index=False)
        .agg(
            festivos_mes=("is_festivo", "sum"),
            cercanos_festivo_mes=("cercano_festivo", "sum"),
            dias_mes=("fecha", "nunique"),
        )
    )
    ctx = agregar_features_mensuales_estaticas(ctx)
    return ctx


# =============================================================================
# MÉTRICAS
# =============================================================================

def evaluar_metricas(y_real, y_pred) -> Dict[str, float]:
    real = np.asarray(y_real, dtype=float)
    pred = np.asarray(y_pred, dtype=float)
    pred = np.clip(pred, 0, None)

    mae = float(mean_absolute_error(real, pred)) if len(real) else 0.0
    rmse = float(np.sqrt(mean_squared_error(real, pred))) if len(real) else 0.0

    mask = real > 0
    mape = float(np.mean(np.abs((real[mask] - pred[mask]) / real[mask])) * 100) if mask.any() else 0.0
    wmape = float(np.abs(real - pred).sum() / real.sum() * 100) if real.sum() > 0 else 0.0
    return {"MAE": mae, "RMSE": rmse, "MAPE": mape, "WMAPE": wmape}


def imprimir_metricas(nombre: str, metricas: Dict[str, float]) -> None:
    log.info(
        "%s: MAE=%.3f | RMSE=%.3f | MAPE=%.2f%% | WMAPE=%.2f%%",
        nombre,
        metricas.get("MAE", 0.0),
        metricas.get("RMSE", 0.0),
        metricas.get("MAPE", 0.0),
        metricas.get("WMAPE", 0.0),
    )


# =============================================================================
# PREPROCESAMIENTO Y XGBOOST MENSUAL
# =============================================================================

def crear_onehot_encoder() -> OneHotEncoder:
    try:
        return OneHotEncoder(handle_unknown="ignore", sparse_output=False)
    except TypeError:
        return OneHotEncoder(handle_unknown="ignore", sparse=False)


def crear_pipeline_xgb(params: Dict[str, Any], config: BloomCastConfig) -> Pipeline:
    numeric_transformer = Pipeline(steps=[("imputer", SimpleImputer(strategy="median"))])
    categorical_transformer = Pipeline(steps=[
        ("imputer", SimpleImputer(strategy="most_frequent")),
        ("onehot", crear_onehot_encoder()),
    ])
    preprocessor = ColumnTransformer(
        transformers=[
            ("num", numeric_transformer, MONTH_NUMERIC_FEATURES),
            ("cat", categorical_transformer, MONTH_CATEGORICAL_FEATURES),
        ]
    )
    xgb_params = dict(params)
    xgb_params.update({"random_state": config.random_state, "n_jobs": -1})
    model = XGBRegressor(**xgb_params)
    return Pipeline(steps=[("preprocessor", preprocessor), ("model", model)])


def construir_fila_prediccion_mensual(
    history: pd.DataFrame,
    contexto_mes: pd.DataFrame,
    periodo_mes: pd.Timestamp,
    orquideaid: int,
) -> Dict[str, Any]:
    hist_o = history[history["orquideaid"] == orquideaid].sort_values("periodo_mes")
    ctx = contexto_mes[(contexto_mes["periodo_mes"] == periodo_mes) & (contexto_mes["orquideaid"] == orquideaid)]
    if ctx.empty:
        raise ValueError(f"No hay contexto mensual para orquideaid={orquideaid}, periodo={periodo_mes}")
    ctx_row = ctx.iloc[0].to_dict()

    row = {
        "periodo_mes": periodo_mes,
        "orquideaid": orquideaid,
        "variedad": ctx_row["variedad"],
        "tipo": ctx_row["tipo"],
        "preciobase": float(ctx_row["preciobase"]),
        "festivos_mes": int(ctx_row["festivos_mes"]),
        "cercanos_festivo_mes": int(ctx_row["cercanos_festivo_mes"]),
        "dias_mes": int(ctx_row["dias_mes"]),
    }
    row.update(agregar_features_mensuales_estaticas(pd.DataFrame([row])).iloc[0].to_dict())

    for lag in [1, 2, 3, 12]:
        lag_period = periodo_mes - pd.DateOffset(months=lag)
        val = hist_o.loc[hist_o["periodo_mes"] == lag_period, TARGET_MONTHLY]
        row[f"lag_{lag}"] = float(val.iloc[0]) if not val.empty else 0.0

    hist_prev = hist_o[hist_o["periodo_mes"] < periodo_mes]
    for window in [3, 6, 12]:
        row[f"rolling_{window}"] = float(hist_prev.tail(window)[TARGET_MONTHLY].mean()) if not hist_prev.empty else 0.0

    hist_same_month = hist_prev[hist_prev["periodo_mes"].dt.month == periodo_mes.month]
    row["promedio_mismo_mes_orquidea"] = (
        float(hist_same_month[TARGET_MONTHLY].mean()) if not hist_same_month.empty else float(hist_prev[TARGET_MONTHLY].mean()) if not hist_prev.empty else 0.0
    )
    row["orquideaid_cat"] = str(orquideaid)
    return row


def predecir_xgboost(
    modelo: Pipeline,
    history: pd.DataFrame,
    contexto_mes: pd.DataFrame,
    meses_objetivo: List[pd.Timestamp],
) -> pd.DataFrame:
    history_work = history[["periodo_mes", "orquideaid", TARGET_MONTHLY]].copy()
    history_work["periodo_mes"] = pd.to_datetime(history_work["periodo_mes"])
    preds = []
    orquideas = sorted(contexto_mes["orquideaid"].unique())

    for periodo in sorted(pd.to_datetime(meses_objetivo)):
        rows = [construir_fila_prediccion_mensual(history_work, contexto_mes, periodo, int(oid)) for oid in orquideas]
        X_future = pd.DataFrame(rows)
        yhat = np.clip(modelo.predict(X_future[MONTH_FEATURES]), 0, None)
        X_future["pred_xgb"] = yhat
        preds.append(X_future[["periodo_mes", "orquideaid", "variedad", "tipo", "preciobase", "pred_xgb"]])

        add_hist = X_future[["periodo_mes", "orquideaid", "pred_xgb"]].rename(columns={"pred_xgb": TARGET_MONTHLY})
        history_work = pd.concat([history_work, add_hist], ignore_index=True)

    return pd.concat(preds, ignore_index=True) if preds else pd.DataFrame()


def entrenar_xgboost(
    df_mensual: pd.DataFrame,
    contexto_mes: pd.DataFrame,
    train_fin: str,
    valid_inicio: str,
    valid_fin: str,
    config: BloomCastConfig,
) -> Tuple[Pipeline, Dict[str, Any], Dict[str, float], pd.DataFrame]:
    train_fin_ts = pd.to_datetime(train_fin)
    valid_meses = pd.date_range(valid_inicio, valid_fin, freq="MS")

    train_base = df_mensual[df_mensual["periodo_mes"] <= train_fin_ts].copy()
    actual_valid = df_mensual[df_mensual["periodo_mes"].isin(valid_meses)][["periodo_mes", "orquideaid", TARGET_MONTHLY]]
    train_feat = generar_features_mensuales(train_base)

    best_model: Optional[Pipeline] = None
    best_params: Dict[str, Any] = {}
    best_metrics = {"WMAPE": float("inf")}
    best_pred = pd.DataFrame()

    log.info("Entrenando XGBoost mensual con validación temporal %s a %s.", valid_inicio, valid_fin)
    for i, params in enumerate(config.xgb_param_grid, start=1):
        model = crear_pipeline_xgb(params, config)
        model.fit(train_feat[MONTH_FEATURES], train_feat[TARGET_MONTHLY])
        pred = predecir_xgboost(model, train_base, contexto_mes, list(valid_meses))
        eval_df = actual_valid.merge(pred, on=["periodo_mes", "orquideaid"], how="left")
        eval_df["pred_xgb"] = eval_df["pred_xgb"].fillna(0).clip(lower=0)
        metrics = evaluar_metricas(eval_df[TARGET_MONTHLY], eval_df["pred_xgb"])
        log.info("XGBoost mensual params %s/%s -> WMAPE %.2f%%", i, len(config.xgb_param_grid), metrics["WMAPE"])
        if metrics["WMAPE"] < best_metrics["WMAPE"]:
            best_model = model
            best_params = params
            best_metrics = metrics
            best_pred = pred

    if best_model is None:
        raise RuntimeError("No se pudo entrenar XGBoost mensual.")
    imprimir_metricas("Mejor XGBoost mensual 2024", best_metrics)
    return best_model, best_params, best_metrics, best_pred


def entrenar_xgboost_final(
    df_mensual: pd.DataFrame,
    train_fin: str,
    params: Dict[str, Any],
    config: BloomCastConfig,
) -> Pipeline:
    train_base = df_mensual[df_mensual["periodo_mes"] <= pd.to_datetime(train_fin)].copy()
    train_feat = generar_features_mensuales(train_base)
    model = crear_pipeline_xgb(params, config)
    model.fit(train_feat[MONTH_FEATURES], train_feat[TARGET_MONTHLY])
    return model


# =============================================================================
# PROPHET MENSUAL
# =============================================================================

def crear_modelo_prophet(params: Dict[str, Any]) -> Prophet:
    model = Prophet(
        changepoint_prior_scale=params["changepoint_prior_scale"],
        seasonality_prior_scale=params["seasonality_prior_scale"],
        holidays_prior_scale=params["holidays_prior_scale"],
        seasonality_mode=params["seasonality_mode"],
        yearly_seasonality=False,
        weekly_seasonality=False,
        daily_seasonality=False,
        interval_width=0.90,
    )
    model.add_seasonality(name="yearly", period=365.25, fourier_order=params["yearly_fourier_order"])
    model.add_regressor("festivos_mes")
    model.add_regressor("cercanos_festivo_mes")
    return model


def entrenar_prophet_con_params(
    df_mensual_train: pd.DataFrame,
    params: Dict[str, Any],
) -> Dict[str, Any]:
    payload: Dict[str, Any] = {"params": params, "models": {}, "fallbacks": {}, "train_fin": str(df_mensual_train["periodo_mes"].max().date())}
    for oid in sorted(df_mensual_train["orquideaid"].unique()):
        hist = df_mensual_train[df_mensual_train["orquideaid"] == oid].sort_values("periodo_mes").copy()
        info = hist.iloc[-1]
        fallback_mes = hist.groupby("mes")[TARGET_MONTHLY].mean().to_dict()
        fallback_global = float(hist[TARGET_MONTHLY].mean()) if not hist.empty else 0.0
        payload["fallbacks"][int(oid)] = {
            "variedad": info["variedad"],
            "tipo": info["tipo"],
            "preciobase": float(info["preciobase"]),
            "fallback_mes": {int(k): float(v) for k, v in fallback_mes.items()},
            "fallback_global": fallback_global,
        }
        try:
            # Prophet mensual necesita al menos dos años para una estacionalidad razonable.
            if len(hist) < 24 or hist[TARGET_MONTHLY].sum() <= 0:
                continue
            train_p = hist[["periodo_mes", TARGET_MONTHLY, "festivos_mes", "cercanos_festivo_mes"]].rename(
                columns={"periodo_mes": "ds", TARGET_MONTHLY: "y"}
            )
            train_p["y"] = np.log1p(train_p["y"])
            model = crear_modelo_prophet(params)
            model.fit(train_p)
            payload["models"][int(oid)] = model
        except Exception as e:
            log.warning("Prophet mensual falló para orquideaid=%s. Se usará fallback. Error: %s", oid, e)
    return payload


def predecir_prophet(
    payload: Dict[str, Any],
    contexto_mes: pd.DataFrame,
    meses_objetivo: List[pd.Timestamp],
) -> pd.DataFrame:
    rows = []
    ctx_obj = contexto_mes[contexto_mes["periodo_mes"].isin(pd.to_datetime(meses_objetivo))].copy()
    for oid in sorted(ctx_obj["orquideaid"].unique()):
        ctx_o = ctx_obj[ctx_obj["orquideaid"] == oid].sort_values("periodo_mes").copy()
        fallback = payload["fallbacks"].get(int(oid), {})
        if int(oid) in payload.get("models", {}):
            model = payload["models"][int(oid)]
            future = ctx_o[["periodo_mes", "festivos_mes", "cercanos_festivo_mes"]].rename(columns={"periodo_mes": "ds"})
            fcst = model.predict(future)
            yhat = np.expm1(fcst["yhat"].values)
            yhat = np.clip(yhat, 0, None)
        else:
            yhat = []
            for _, r in ctx_o.iterrows():
                mes = int(r["periodo_mes"].month)
                yhat.append(float(fallback.get("fallback_mes", {}).get(mes, fallback.get("fallback_global", 0.0))))
            yhat = np.asarray(yhat, dtype=float)

        out = ctx_o[["periodo_mes", "orquideaid", "variedad", "tipo", "preciobase"]].copy()
        out["pred_prophet"] = yhat
        rows.append(out)

    return pd.concat(rows, ignore_index=True) if rows else pd.DataFrame()


def entrenar_prophet(
    df_mensual: pd.DataFrame,
    contexto_mes: pd.DataFrame,
    train_fin: str,
    valid_inicio: str,
    valid_fin: str,
    config: BloomCastConfig,
) -> Tuple[Dict[str, Any], Dict[str, Any], Dict[str, float], pd.DataFrame]:
    train_base = df_mensual[df_mensual["periodo_mes"] <= pd.to_datetime(train_fin)].copy()
    valid_meses = pd.date_range(valid_inicio, valid_fin, freq="MS")
    actual_valid = df_mensual[df_mensual["periodo_mes"].isin(valid_meses)][["periodo_mes", "orquideaid", TARGET_MONTHLY]]

    best_payload: Optional[Dict[str, Any]] = None
    best_params: Dict[str, Any] = {}
    best_metrics = {"WMAPE": float("inf")}
    best_pred = pd.DataFrame()

    log.info("Entrenando Prophet mensual por orquideaid.")
    for i, params in enumerate(config.prophet_param_grid, start=1):
        payload = entrenar_prophet_con_params(train_base, params)
        pred = predecir_prophet(payload, contexto_mes, list(valid_meses))
        eval_df = actual_valid.merge(pred, on=["periodo_mes", "orquideaid"], how="left")
        eval_df["pred_prophet"] = eval_df["pred_prophet"].fillna(0).clip(lower=0)
        metrics = evaluar_metricas(eval_df[TARGET_MONTHLY], eval_df["pred_prophet"])
        log.info("Prophet mensual params %s/%s -> WMAPE %.2f%%", i, len(config.prophet_param_grid), metrics["WMAPE"])
        if metrics["WMAPE"] < best_metrics["WMAPE"]:
            best_payload = payload
            best_params = params
            best_metrics = metrics
            best_pred = pred

    if best_payload is None:
        raise RuntimeError("No se pudo entrenar Prophet mensual.")
    imprimir_metricas("Mejor Prophet mensual 2024", best_metrics)
    return best_payload, best_params, best_metrics, best_pred


def entrenar_prophet_final(
    df_mensual: pd.DataFrame,
    train_fin: str,
    params: Dict[str, Any],
) -> Dict[str, Any]:
    train_base = df_mensual[df_mensual["periodo_mes"] <= pd.to_datetime(train_fin)].copy()
    return entrenar_prophet_con_params(train_base, params)


# =============================================================================
# COMBINACIÓN Y SELECCIÓN MENSUAL
# =============================================================================

def combinar_modelos(
    actual: pd.DataFrame,
    pred_xgb: pd.DataFrame,
    pred_prophet: pd.DataFrame,
    config: BloomCastConfig,
) -> Tuple[pd.DataFrame, pd.DataFrame, Dict[str, Any]]:
    df = actual.merge(pred_xgb[["periodo_mes", "orquideaid", "pred_xgb"]], on=["periodo_mes", "orquideaid"], how="left")
    df = df.merge(pred_prophet[["periodo_mes", "orquideaid", "pred_prophet"]], on=["periodo_mes", "orquideaid"], how="left")
    df["pred_xgb"] = df["pred_xgb"].fillna(0).clip(lower=0)
    df["pred_prophet"] = df["pred_prophet"].fillna(0).clip(lower=0)

    candidates: List[Dict[str, Any]] = []

    def add_candidate(nombre: str, tipo: str, base_pred: np.ndarray, peso_xgb=None, alpha=None):
        for escala in config.escalas_calibracion:
            yhat = np.clip(base_pred * escala, 0, None)
            metrics = evaluar_metricas(df[TARGET_MONTHLY], yhat)
            candidates.append({
                "modelo": nombre,
                "tipo": tipo,
                "peso_xgb": peso_xgb,
                "alpha": alpha,
                "escala": escala,
                **metrics,
            })

    add_candidate("Solo XGBoost mensual", "solo_xgboost_mensual", df["pred_xgb"].values)
    add_candidate("Solo Prophet mensual", "solo_prophet_mensual", df["pred_prophet"].values)

    for peso in config.pesos_ensemble:
        base = df["pred_xgb"].values * peso + df["pred_prophet"].values * (1 - peso)
        add_candidate(f"Ensemble mensual peso_xgb={peso:.1f}", "ensemble_mensual", base, peso_xgb=peso)

    for alpha in config.alphas_ajuste:
        base = df["pred_xgb"].values + alpha * (df["pred_prophet"].values - df["pred_xgb"].values)
        add_candidate(f"Híbrido ajuste mensual alpha={alpha:.2f}", "hibrido_ajuste_mensual", base, alpha=alpha)

    metricas = pd.DataFrame(candidates).sort_values("WMAPE").reset_index(drop=True)
    best = metricas.iloc[0].to_dict()
    selected = aplicar_estrategia_mensual(df, best)
    return metricas, selected, best


def aplicar_estrategia_mensual(df: pd.DataFrame, estrategia: Dict[str, Any]) -> pd.DataFrame:
    out = df.copy()
    tipo = estrategia["tipo"]
    escala = float(estrategia.get("escala", 1.0))

    if tipo == "solo_xgboost_mensual":
        base = out["pred_xgb"].values
    elif tipo == "solo_prophet_mensual":
        base = out["pred_prophet"].values
    elif tipo == "ensemble_mensual":
        peso = float(estrategia.get("peso_xgb", 0.5))
        base = out["pred_xgb"].values * peso + out["pred_prophet"].values * (1 - peso)
    elif tipo == "hibrido_ajuste_mensual":
        alpha = float(estrategia.get("alpha", 0.5))
        base = out["pred_xgb"].values + alpha * (out["pred_prophet"].values - out["pred_xgb"].values)
    else:
        raise ValueError(f"Tipo de estrategia no reconocido: {tipo}")

    out["prediccion_mensual"] = np.clip(base * escala, 0, None)
    return out


def seleccionar_mejor_modelo(metricas: pd.DataFrame) -> Dict[str, Any]:
    return metricas.sort_values("WMAPE").iloc[0].to_dict()


# =============================================================================
# DISTRIBUCIÓN MENSUAL -> DIARIA
# =============================================================================

def calcular_multiplicadores_festivos(df_hist: pd.DataFrame, oid: int) -> Tuple[float, float]:
    hist_o = df_hist[df_hist["orquideaid"] == oid]
    if hist_o.empty:
        return 2.5, 1.4

    normal = hist_o[(hist_o["is_festivo"] == 0) & (hist_o["cercano_festivo"] == 0)][TARGET_DAILY]
    fest = hist_o[hist_o["is_festivo"] == 1][TARGET_DAILY]
    cerca = hist_o[(hist_o["is_festivo"] == 0) & (hist_o["cercano_festivo"] == 1)][TARGET_DAILY]
    base = max(float(normal.mean()) if len(normal) else float(hist_o[TARGET_DAILY].mean()), 0.01)

    mult_fest = float(fest.mean() / base) if len(fest) else 2.5
    mult_cerca = float(cerca.mean() / base) if len(cerca) else 1.4
    return float(np.clip(mult_fest, 1.0, 5.0)), float(np.clip(mult_cerca, 1.0, 3.0))


def obtener_perfil_dom(hist_o_mes: pd.DataFrame) -> Dict[int, float]:
    if hist_o_mes.empty or hist_o_mes[TARGET_DAILY].sum() <= 0:
        return {}
    tmp = hist_o_mes.copy()
    tmp["dia"] = tmp["fecha"].dt.day
    s = tmp.groupby("dia")[TARGET_DAILY].sum()
    return {int(k): float(v) for k, v in s.items()}


def obtener_perfil_dow(hist_o_mes: pd.DataFrame) -> Dict[int, float]:
    if hist_o_mes.empty or hist_o_mes[TARGET_DAILY].sum() <= 0:
        return {}
    tmp = hist_o_mes.copy()
    tmp["dia_semana"] = tmp["fecha"].dt.dayofweek
    s = tmp.groupby("dia_semana")[TARGET_DAILY].sum()
    return {int(k): float(v) for k, v in s.items()}


def calcular_pesos_diarios(
    df_diario_hist: pd.DataFrame,
    df_diario_target: pd.DataFrame,
) -> pd.DataFrame:
    rows = []
    hist = df_diario_hist.copy()
    target = df_diario_target.copy()

    for (oid, periodo), group_target in target.groupby(["orquideaid", "periodo_mes"]):
        mes = int(pd.to_datetime(periodo).month)
        group_target = group_target.sort_values("fecha").copy()
        n = len(group_target)
        if n == 0:
            continue

        hist_o_mes = hist[(hist["orquideaid"] == oid) & (hist["fecha"].dt.month == mes)].copy()
        perfil_dom = obtener_perfil_dom(hist_o_mes)
        perfil_dow = obtener_perfil_dow(hist_o_mes)
        mult_fest, mult_cerca = calcular_multiplicadores_festivos(hist, int(oid))

        # Suavizado para que ningún día quede con peso cero.
        dom_total = sum(perfil_dom.values()) + 0.50 * n
        dow_total = sum(perfil_dow.values()) + 0.50 * 7

        pesos = []
        for _, r in group_target.iterrows():
            dia = int(r["fecha"].day)
            dow = int(r["fecha"].dayofweek)
            w_dom = (perfil_dom.get(dia, 0.0) + 0.50) / dom_total if dom_total > 0 else 1.0 / n
            w_dow = (perfil_dow.get(dow, 0.0) + 0.50) / dow_total if dow_total > 0 else 1.0 / n
            w_uniforme = 1.0 / n
            w = 0.55 * w_dom + 0.30 * w_dow + 0.15 * w_uniforme
            if int(r.get("is_festivo", 0)) == 1:
                w *= mult_fest
            elif int(r.get("cercano_festivo", 0)) == 1:
                w *= mult_cerca
            pesos.append(float(w))

        pesos_arr = np.asarray(pesos, dtype=float)
        if pesos_arr.sum() <= 0 or np.isnan(pesos_arr).any():
            pesos_arr = np.ones(n) / n
        else:
            pesos_arr = pesos_arr / pesos_arr.sum()

        out = group_target[["fecha", "periodo_mes", "orquideaid", "variedad", "tipo", "preciobase", TARGET_DAILY, "is_festivo", "cercano_festivo"]].copy()
        out["peso_diario"] = pesos_arr
        rows.append(out)

    return pd.concat(rows, ignore_index=True) if rows else pd.DataFrame()


def asignar_enteros_por_mes(df_float: pd.DataFrame) -> pd.DataFrame:
    rows = []
    for (oid, periodo), group in df_float.groupby(["orquideaid", "periodo_mes"]):
        g = group.sort_values("fecha").copy()
        total_mes = int(round(float(g["prediccion_mensual"].iloc[0])))
        vals = np.clip(g["pred_float"].values, 0, None)
        floors = np.floor(vals).astype(int)
        resto = total_mes - int(floors.sum())
        if resto > 0:
            fracs = vals - floors
            order = np.argsort(-fracs)
            for idx in order[:resto]:
                floors[idx] += 1
        elif resto < 0:
            # Caso raro por redondeo: restar unidades de los días con mayor asignación.
            order = np.argsort(-floors)
            faltan = abs(resto)
            for idx in order:
                if faltan <= 0:
                    break
                quitar = min(floors[idx], faltan)
                floors[idx] -= quitar
                faltan -= quitar
        g["prediccion"] = np.clip(floors, 0, None).astype(int)
        rows.append(g)
    return pd.concat(rows, ignore_index=True) if rows else pd.DataFrame()


def distribuir_prediccion_mensual_a_diaria(
    pred_mensual: pd.DataFrame,
    df_diario_hist: pd.DataFrame,
    df_diario_target: pd.DataFrame,
) -> pd.DataFrame:
    pesos = calcular_pesos_diarios(df_diario_hist, df_diario_target)
    mensual_cols = ["periodo_mes", "orquideaid", "prediccion_mensual"]
    df = pesos.merge(pred_mensual[mensual_cols], on=["periodo_mes", "orquideaid"], how="left")
    df["prediccion_mensual"] = df["prediccion_mensual"].fillna(0).clip(lower=0)
    df["pred_float"] = df["prediccion_mensual"] * df["peso_diario"]
    df = asignar_enteros_por_mes(df)
    df = df.rename(columns={TARGET_DAILY: "venta_real"})
    df["error_absoluto"] = (df["venta_real"] - df["prediccion"]).abs()
    df["error_porcentual"] = np.where(df["venta_real"] > 0, df["error_absoluto"] / df["venta_real"] * 100, np.nan)
    return df.sort_values(["fecha", "orquideaid"]).reset_index(drop=True)


# =============================================================================
# EXPORTACIÓN, GRÁFICOS, MODELOS E INSERCIÓN
# =============================================================================

def graficar_resultados(resultados_diarios_2025: pd.DataFrame, output_dir: Path) -> Dict[str, str]:
    paths: Dict[str, str] = {}
    output_dir.mkdir(parents=True, exist_ok=True)

    diario_total = resultados_diarios_2025.groupby("fecha", as_index=False).agg(
        venta_real=("venta_real", "sum"),
        prediccion=("prediccion", "sum"),
    )
    plt.figure(figsize=(14, 6))
    plt.plot(diario_total["fecha"], diario_total["venta_real"], label="Venta real diaria")
    plt.plot(diario_total["fecha"], diario_total["prediccion"], label="Predicción diaria")
    plt.title("BloomCast 2025 - Ventas reales vs predicción diaria agregada")
    plt.xlabel("Fecha")
    plt.ylabel("Cantidad")
    plt.legend()
    plt.tight_layout()
    path1 = output_dir / "grafico_real_vs_prediccion_2025.png"
    plt.savefig(path1, dpi=150)
    plt.close()
    paths["real_vs_prediccion"] = str(path1)

    mensual = resultados_diarios_2025.groupby("periodo_mes", as_index=False).agg(
        venta_real=("venta_real", "sum"),
        prediccion=("prediccion", "sum"),
    )
    mensual["error_abs"] = (mensual["venta_real"] - mensual["prediccion"]).abs()
    mensual["error_pct"] = np.where(mensual["venta_real"] > 0, mensual["error_abs"] / mensual["venta_real"] * 100, 0)
    plt.figure(figsize=(12, 5))
    plt.bar(mensual["periodo_mes"].dt.strftime("%Y-%m"), mensual["error_pct"])
    plt.xticks(rotation=45)
    plt.title("Error porcentual mensual 2025")
    plt.xlabel("Mes")
    plt.ylabel("Error porcentual (%)")
    plt.tight_layout()
    path2 = output_dir / "grafico_error_por_mes_2025.png"
    plt.savefig(path2, dpi=150)
    plt.close()
    paths["error_por_mes"] = str(path2)

    fest = resultados_diarios_2025.copy()
    fest["grupo"] = np.where((fest["is_festivo"] == 1) | (fest["cercano_festivo"] == 1), "Festivo/cercano", "Normal")
    resumen = fest.groupby("grupo", as_index=False).agg(error_absoluto=("error_absoluto", "mean"))
    plt.figure(figsize=(7, 5))
    plt.bar(resumen["grupo"], resumen["error_absoluto"])
    plt.title("Error absoluto medio: festivos/cercanos vs días normales")
    plt.xlabel("Tipo de día")
    plt.ylabel("MAE diario")
    plt.tight_layout()
    path3 = output_dir / "grafico_error_festivos_vs_normales_2025.png"
    plt.savefig(path3, dpi=150)
    plt.close()
    paths["error_festivos_vs_normales"] = str(path3)

    return paths


def exportar_resultados(
    metricas_validacion: pd.DataFrame,
    resultados_diarios_2025: pd.DataFrame,
    resultados_mensuales_2025: pd.DataFrame,
    output_dir: Path,
) -> Dict[str, str]:
    output_dir.mkdir(parents=True, exist_ok=True)
    paths = {
        "metricas_validacion": str(output_dir / "metricas_validacion_2024.csv"),
        "diario_csv": str(output_dir / "resultados_prediccion_2025.csv"),
        "diario_excel": str(output_dir / "resultados_prediccion_2025.xlsx"),
        "mensual_csv": str(output_dir / "resultados_prediccion_mensual_2025.csv"),
        "mensual_excel": str(output_dir / "resultados_prediccion_mensual_2025.xlsx"),
    }
    metricas_validacion.to_csv(paths["metricas_validacion"], index=False)

    cols_diario = [
        "fecha", "orquideaid", "variedad", "tipo", "venta_real", "prediccion",
        "error_absoluto", "error_porcentual", "is_festivo", "cercano_festivo",
        "periodo_mes", "peso_diario", "prediccion_mensual",
    ]
    resultados_diarios_2025[cols_diario].to_csv(paths["diario_csv"], index=False)
    resultados_diarios_2025[cols_diario].to_excel(paths["diario_excel"], index=False)

    resultados_mensuales_2025.to_csv(paths["mensual_csv"], index=False)
    resultados_mensuales_2025.to_excel(paths["mensual_excel"], index=False)
    return paths


def guardar_modelos(
    xgb_model: Pipeline,
    prophet_payload: Dict[str, Any],
    metadata: Dict[str, Any],
    model_dir: Path,
) -> Dict[str, str]:
    model_dir.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    paths = {
        "xgboost_mensual": str(model_dir / f"xgboost_mensual_final_{stamp}.joblib"),
        "prophet_mensual": str(model_dir / f"prophet_mensual_payload_final_{stamp}.joblib"),
        "metadata": str(model_dir / f"metadata_modelo_mensual_diario_{stamp}.json"),
    }
    joblib.dump(xgb_model, paths["xgboost_mensual"])
    joblib.dump(prophet_payload, paths["prophet_mensual"])
    with open(paths["metadata"], "w", encoding="utf-8") as f:
        json.dump(metadata, f, ensure_ascii=False, indent=2, default=str)
    return paths


def asegurar_constraint_unica(engine: Engine) -> None:
    sql = """
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1
                FROM pg_constraint
                WHERE conname = 'uq_predicciones_demanda_fecha_orquidea'
            ) THEN
                ALTER TABLE predicciones_demanda
                ADD CONSTRAINT uq_predicciones_demanda_fecha_orquidea
                UNIQUE (fechaobjetivo, orquideaid);
            END IF;
        END $$;
    """
    try:
        with engine.begin() as conn:
            conn.execute(text(sql))
        log.info("Restricción única verificada en predicciones_demanda.")
    except Exception as e:
        log.warning(
            "No se pudo crear/verificar la restricción única. Si hay duplicados previos, elimínalos antes. Error: %s",
            e,
        )


def insertar_predicciones_bd(engine: Engine, resultados_diarios_2025: pd.DataFrame, config: BloomCastConfig) -> int:
    if not config.insertar_bd:
        return 0
    if config.crear_constraint_unica:
        asegurar_constraint_unica(engine)

    rows = []
    for _, r in resultados_diarios_2025.iterrows():
        rows.append((pd.to_datetime(r["fecha"]).date(), int(r["orquideaid"]), int(max(0, round(r["prediccion"])))) )

    sql = """
        INSERT INTO predicciones_demanda
        (
            fechageneracion,
            fechaobjetivo,
            orquideaid,
            cantidadestimada
        )
        VALUES %s
        ON CONFLICT (fechaobjetivo, orquideaid)
        DO UPDATE SET
            fechageneracion = EXCLUDED.fechageneracion,
            cantidadestimada = EXCLUDED.cantidadestimada;
    """
    records = [(datetime.now(), fecha, oid, cantidad) for fecha, oid, cantidad in rows]
    raw_conn = engine.raw_connection()
    try:
        with raw_conn.cursor() as cur:
            execute_values(cur, sql, records, page_size=1000)
        raw_conn.commit()
    finally:
        raw_conn.close()
    return len(records)


# =============================================================================
# PIPELINE PRINCIPAL
# =============================================================================

def calcular_metricas_mensuales_desde_diario(resultados_diarios: pd.DataFrame) -> Tuple[pd.DataFrame, Dict[str, float]]:
    mensual = resultados_diarios.groupby(["periodo_mes", "orquideaid", "variedad", "tipo"], as_index=False).agg(
        venta_real=("venta_real", "sum"),
        prediccion=("prediccion", "sum"),
    )
    mensual["error_absoluto"] = (mensual["venta_real"] - mensual["prediccion"]).abs()
    mensual["error_porcentual"] = np.where(mensual["venta_real"] > 0, mensual["error_absoluto"] / mensual["venta_real"] * 100, np.nan)
    metrics = evaluar_metricas(mensual["venta_real"], mensual["prediccion"])
    return mensual, metrics


def ejecutar_pipeline(config: BloomCastConfig) -> Dict[str, Any]:
    log.info("=" * 92)
    log.info("BloomCast Hybrid MENSUAL-DIARIO: XGBoost + Prophet + distribución diaria por festivos")
    log.info("Métrica principal de selección: WMAPE mensual por orquideaid. Objetivo: < %.1f%%", config.target_wmape)
    log.info("=" * 92)

    output_dir = Path(config.output_dir)
    model_dir = Path(config.model_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    model_dir.mkdir(parents=True, exist_ok=True)

    engine = conectar_bd()
    df_orquideas = cargar_orquideas(engine)
    df_ventas = cargar_ventas(engine, config.inicio_historico, config.fin_historico)
    df_festivos = cargar_festivos(engine)

    df_diario = preparar_serie_demanda(df_ventas, df_orquideas, df_festivos, config)
    df_mensual = preparar_mensual(df_diario)
    contexto_mes = contexto_mensual_desde_diario(df_diario)

    # ===================== Validación 2024 =====================
    xgb_model_val, best_xgb_params, metrics_xgb_2024, pred_xgb_2024 = entrenar_xgboost(
        df_mensual,
        contexto_mes,
        config.train_fin_validacion,
        config.valid_inicio,
        config.valid_fin,
        config,
    )
    prophet_payload_val, best_prophet_params, metrics_prophet_2024, pred_prophet_2024 = entrenar_prophet(
        df_mensual,
        contexto_mes,
        config.train_fin_validacion,
        config.valid_inicio,
        config.valid_fin,
        config,
    )

    valid_meses = pd.date_range(config.valid_inicio, config.valid_fin, freq="MS")
    actual_2024 = df_mensual[df_mensual["periodo_mes"].isin(valid_meses)][
        ["periodo_mes", "orquideaid", "variedad", "tipo", "preciobase", TARGET_MONTHLY]
    ].copy()

    metricas_validacion, pred_mensual_2024, estrategia = combinar_modelos(
        actual_2024, pred_xgb_2024, pred_prophet_2024, config
    )
    imprimir_metricas("Mejor estrategia mensual 2024", evaluar_metricas(pred_mensual_2024[TARGET_MONTHLY], pred_mensual_2024["prediccion_mensual"]))
    log.info("Estrategia seleccionada: %s", estrategia)

    # Distribución diaria de validación 2024, sin usar datos diarios de 2024 como patrón.
    df_hist_diario_2021_2023 = df_diario[df_diario["fecha"] <= pd.to_datetime(config.train_fin_validacion)].copy()
    df_target_diario_2024 = df_diario[(df_diario["fecha"] >= pd.to_datetime(config.valid_inicio)) & (df_diario["fecha"] <= pd.to_datetime(config.valid_fin))].copy()
    resultados_diarios_2024 = distribuir_prediccion_mensual_a_diaria(
        pred_mensual_2024[["periodo_mes", "orquideaid", "prediccion_mensual"]],
        df_hist_diario_2021_2023,
        df_target_diario_2024,
    )
    metricas_diarias_2024 = evaluar_metricas(resultados_diarios_2024["venta_real"], resultados_diarios_2024["prediccion"])
    _, metricas_mensuales_dist_2024 = calcular_metricas_mensuales_desde_diario(resultados_diarios_2024)
    imprimir_metricas("Validación diaria distribuida 2024", metricas_diarias_2024)
    imprimir_metricas("Validación mensual después de distribución 2024", metricas_mensuales_dist_2024)

    # ===================== Modelo final y predicción 2025 =====================
    log.info("Reentrenando modelos finales con 2021-2024 y prediciendo 2025.")
    xgb_final = entrenar_xgboost_final(df_mensual, config.train_fin_final, best_xgb_params, config)
    prophet_final = entrenar_prophet_final(df_mensual, config.train_fin_final, best_prophet_params)

    train_final = df_mensual[df_mensual["periodo_mes"] <= pd.to_datetime(config.train_fin_final)].copy()
    pred_meses_2025 = list(pd.date_range(config.pred_inicio, config.pred_fin, freq="MS"))
    pred_xgb_2025 = predecir_xgboost(xgb_final, train_final, contexto_mes, pred_meses_2025)
    pred_prophet_2025 = predecir_prophet(prophet_final, contexto_mes, pred_meses_2025)

    actual_2025 = df_mensual[df_mensual["periodo_mes"].isin(pred_meses_2025)][
        ["periodo_mes", "orquideaid", "variedad", "tipo", "preciobase", TARGET_MONTHLY]
    ].copy()
    combo_base_2025 = actual_2025.merge(pred_xgb_2025[["periodo_mes", "orquideaid", "pred_xgb"]], on=["periodo_mes", "orquideaid"], how="left")
    combo_base_2025 = combo_base_2025.merge(pred_prophet_2025[["periodo_mes", "orquideaid", "pred_prophet"]], on=["periodo_mes", "orquideaid"], how="left")
    combo_base_2025["pred_xgb"] = combo_base_2025["pred_xgb"].fillna(0).clip(lower=0)
    combo_base_2025["pred_prophet"] = combo_base_2025["pred_prophet"].fillna(0).clip(lower=0)
    pred_mensual_2025 = aplicar_estrategia_mensual(combo_base_2025, estrategia)

    df_hist_diario_2021_2024 = df_diario[df_diario["fecha"] <= pd.to_datetime(config.train_fin_final)].copy()
    df_target_diario_2025 = df_diario[(df_diario["fecha"] >= pd.to_datetime(config.pred_inicio)) & (df_diario["fecha"] <= pd.to_datetime(config.pred_fin))].copy()
    resultados_diarios_2025 = distribuir_prediccion_mensual_a_diaria(
        pred_mensual_2025[["periodo_mes", "orquideaid", "prediccion_mensual"]],
        df_hist_diario_2021_2024,
        df_target_diario_2025,
    )
    resultados_mensuales_2025, metricas_mensuales_2025 = calcular_metricas_mensuales_desde_diario(resultados_diarios_2025)
    metricas_diarias_2025 = evaluar_metricas(resultados_diarios_2025["venta_real"], resultados_diarios_2025["prediccion"])

    imprimir_metricas("MÉTRICAS 2025 MENSUAL por orquideaid", metricas_mensuales_2025)
    imprimir_metricas("MÉTRICAS 2025 DIARIA por orquideaid", metricas_diarias_2025)

    export_paths = exportar_resultados(metricas_validacion, resultados_diarios_2025, resultados_mensuales_2025, output_dir)
    graph_paths = graficar_resultados(resultados_diarios_2025, output_dir)

    metadata = {
        "version": "mensual_diario_v1",
        "metrica_principal": config.metrica_principal,
        "target_wmape": config.target_wmape,
        "config": asdict(config),
        "best_xgb_params": best_xgb_params,
        "best_prophet_params": best_prophet_params,
        "metrics_xgb_2024": metrics_xgb_2024,
        "metrics_prophet_2024": metrics_prophet_2024,
        "estrategia_seleccionada": estrategia,
        "metricas_diarias_2024": metricas_diarias_2024,
        "metricas_mensuales_distribuidas_2024": metricas_mensuales_dist_2024,
        "metricas_2025_mensual": metricas_mensuales_2025,
        "metricas_2025_diaria": metricas_diarias_2025,
        "export_paths": export_paths,
        "graph_paths": graph_paths,
    }
    model_paths = guardar_modelos(xgb_final, prophet_final, metadata, model_dir)
    metadata["model_paths"] = model_paths

    predicciones_insertadas = insertar_predicciones_bd(engine, resultados_diarios_2025, config)
    metadata["predicciones_insertadas"] = predicciones_insertadas

    resumen_path = output_dir / "resumen_pipeline.json"
    with open(resumen_path, "w", encoding="utf-8") as f:
        json.dump(metadata, f, ensure_ascii=False, indent=2, default=str)
    metadata["resumen_path"] = str(resumen_path)

    log.info("=" * 92)
    log.info("Pipeline finalizado.")
    log.info("Métrica principal 2025 mensual WMAPE: %.2f%%", metricas_mensuales_2025["WMAPE"])
    log.info("Métrica operativa 2025 diaria MAE: %.3f | WMAPE diario: %.2f%%", metricas_diarias_2025["MAE"], metricas_diarias_2025["WMAPE"])
    log.info("Archivos exportados: %s", export_paths)
    log.info("Gráficos: %s", graph_paths)
    log.info("Modelos: %s", model_paths)
    log.info("Predicciones insertadas: %s", predicciones_insertadas)
    log.info("Resumen: %s", resumen_path)
    log.info("=" * 92)

    return metadata


# =============================================================================
# CLI
# =============================================================================

def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="BloomCast híbrido mensual-diario XGBoost + Prophet")
    parser.add_argument("--no-insert", action="store_true", help="No insertar predicciones en PostgreSQL")
    parser.add_argument("--inicio", default="2021-01-01", help="Inicio histórico")
    parser.add_argument("--fin", default="2025-12-31", help="Fin histórico")
    parser.add_argument("--output-dir", default="outputs_bloomcast", help="Directorio de salidas")
    parser.add_argument("--model-dir", default="modelos_bloomcast", help="Directorio de modelos")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    config = BloomCastConfig(
        inicio_historico=args.inicio,
        fin_historico=args.fin,
        output_dir=args.output_dir,
        model_dir=args.model_dir,
        insertar_bd=not args.no_insert,
    )
    ejecutar_pipeline(config)


if __name__ == "__main__":
    main()

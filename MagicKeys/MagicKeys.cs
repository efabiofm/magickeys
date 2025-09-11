using cAlgo.API;
using System;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OrdenConRiesgo : Robot
    {
        private Button botonEntrada;
        private Button botonCerrarMitad;
        private Button botonSLBE;
        private Button botonLimit; // NUEVO

        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Margin (%)", DefaultValue = 0)]
        public double Margin { get; set; }

        [Parameter("SL Line Comment", DefaultValue = "SL")]
        public string SLLineComment { get; set; }

        [Parameter("Entry Line Comment", DefaultValue = "ENTRY")]
        public string EntryLineComment { get; set; }

        protected override void OnStart()
        {
            botonEntrada = new Button
            {
                Text = "ENTER",
                Width = 60,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 10, 10, 10),
                BackgroundColor = Color.Green
            };
            botonEntrada.Click += args => { EjecutarEntrada(); };

            botonLimit = new Button
            {
                Text = "LIMIT",
                Width = 60,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 10, 80, 10),
                BackgroundColor = Color.Blue
            };
            botonLimit.Click += args => { EjecutarLimit(); };

            botonCerrarMitad = new Button
            {
                Text = "HALF",
                Width = 60,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 10, 150, 10),
                BackgroundColor = Color.Orange
            };
            botonCerrarMitad.Click += args => { CerrarMitad(); };

            botonSLBE = new Button
            {
                Text = "BE",
                Width = 60,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 10, 220, 10),
                BackgroundColor = Color.Red
            };
            botonSLBE.Click += args => { SLtoBE(); };

            Chart.AddControl(botonEntrada);
            Chart.AddControl(botonLimit);
            Chart.AddControl(botonCerrarMitad);
            Chart.AddControl(botonSLBE);
        }

        private ChartHorizontalLine BuscarLinea(string comment)
        {
            foreach (var obj in Chart.Objects)
            {
                var line = obj as ChartHorizontalLine;
                if (line != null && line.Comment == comment)
                    return line;
            }
            return null;
        }

        private void EjecutarEntrada()
        {
            var slLine = BuscarLinea(SLLineComment);
            if (slLine == null)
            {
                Print("No se encontró una línea de SL con el comentario: " + SLLineComment);
                return;
            }

            double stopLossPrice = slLine.Y;
            double bid = Symbol.Bid;
            double ask = Symbol.Ask;

            TradeType tradeType;
            double entryPrice;

            if (ask > stopLossPrice)
            {
                tradeType = TradeType.Buy;
                entryPrice = ask;
            }
            else
            {
                tradeType = TradeType.Sell;
                entryPrice = bid;
            }

            double adjustedRiskPercent = RiskPercent - Margin;
            if (adjustedRiskPercent <= 0)
            {
                Print("El margen es demasiado grande en relación al riesgo.");
                return;
            }

            double riskAmount = Account.Balance * (adjustedRiskPercent / 100.0);
            double pipSize = Symbol.PipSize;
            double pipValue = Symbol.PipValue;

            double distanceToSL = Math.Abs(entryPrice - stopLossPrice);
            double pips = distanceToSL / pipSize;

            if (pips < 0.01)
            {
                Print("El SL está demasiado cerca del precio actual.");
                return;
            }

            double volumeInUnits = riskAmount / (pips * pipValue);

            double normalizedVolume = Math.Max(
                Symbol.VolumeInUnitsMin,
                Symbol.NormalizeVolumeInUnits(volumeInUnits, RoundingMode.ToNearest)
            );

            double stopLossPips = pips;

            Print($"[MARKET] Tipo: {tradeType}, Vol: {normalizedVolume}, SL(pips): {stopLossPips}, SL: {stopLossPrice}, riesgo: {adjustedRiskPercent}%");

            var result = ExecuteMarketOrder(tradeType, SymbolName, normalizedVolume, "EntradaRiesgo", stopLossPips, null);

            if (!result.IsSuccessful || result.Position == null)
                Print("No se pudo abrir la posición.");
        }

        // NUEVO: orden LIMIT con entrada desde línea ENTRY y SL desde línea SL
        private void EjecutarLimit()
        {
            var slLine = BuscarLinea(SLLineComment);
            if (slLine == null)
            {
                Print("No se encontró una línea de SL con el comentario: " + SLLineComment);
                return;
            }

            var entryLine = BuscarLinea(EntryLineComment);
            if (entryLine == null)
            {
                Print("No se encontró una línea de ENTRY con el comentario: " + EntryLineComment);
                return;
            }

            double entryPrice = entryLine.Y;
            double stopLossPrice = slLine.Y;

            double bid = Symbol.Bid;
            double ask = Symbol.Ask;
            double mid = (bid + ask) / 2.0;

            // Tipo de orden limit según relación con el precio actual
            TradeType tradeType = entryPrice < mid ? TradeType.Buy : TradeType.Sell;

            // Validaciones de lado correcto del SL
            if (tradeType == TradeType.Buy && !(stopLossPrice < entryPrice))
            {
                Print("Para BUY LIMIT, el SL debe estar por debajo del ENTRY.");
                return;
            }
            if (tradeType == TradeType.Sell && !(stopLossPrice > entryPrice))
            {
                Print("Para SELL LIMIT, el SL debe estar por encima del ENTRY.");
                return;
            }

            double adjustedRiskPercent = RiskPercent - Margin;
            if (adjustedRiskPercent <= 0)
            {
                Print("El margen es demasiado grande en relación al riesgo.");
                return;
            }

            double riskAmount = Account.Balance * (adjustedRiskPercent / 100.0);
            double pipSize = Symbol.PipSize;
            double pipValue = Symbol.PipValue;

            double distanceToSL = Math.Abs(entryPrice - stopLossPrice);
            double pips = distanceToSL / pipSize;

            if (pips < 0.01)
            {
                Print("El SL está demasiado cerca del ENTRY.");
                return;
            }

            // Tamaño por riesgo medido desde ENTRY hasta SL
            double volumeInUnits = riskAmount / (pips * pipValue);
            double normalizedVolume = Math.Max(
                Symbol.VolumeInUnitsMin,
                Symbol.NormalizeVolumeInUnits(volumeInUnits, RoundingMode.ToNearest)
            );

            double stopLossPips = pips;

            Print($"[LIMIT] Tipo: {tradeType}, Vol: {normalizedVolume}, ENTRY: {entryPrice}, SL(pips): {stopLossPips}, SL: {stopLossPrice}, riesgo: {adjustedRiskPercent}%");

            var result = PlaceLimitOrder(tradeType, SymbolName, normalizedVolume, entryPrice, "EntradaRiesgo_LIMIT", stopLossPips, null);

            if (!result.IsSuccessful || result.PendingOrder == null)
                Print("No se pudo colocar la orden LIMIT.");
        }

        private void CerrarMitad()
        {
            var position = Positions.Find("EntradaRiesgo", SymbolName);
            if (position == null)
            {
                Print("No hay posición activa para cerrar la mitad.");
                return;
            }

            double currentVolume = position.VolumeInUnits;
            double halfVolume = Symbol.NormalizeVolumeInUnits(currentVolume / 2.0, RoundingMode.ToNearest);

            if (halfVolume < Symbol.VolumeInUnitsMin)
            {
                Print("El volumen para cerrar es menor que el mínimo permitido.");
                return;
            }

            Print($"Cerrando {halfVolume} unidades (la mitad) de la posición.");
            ClosePosition(position, halfVolume);
        }

        private void SLtoBE()
        {
            var position = Positions.Find("EntradaRiesgo", SymbolName);

            if (position == null)
            {
                Print("No hay posición activa para mover el SL a BE.");
                return;
            }

            double breakEven = position.EntryPrice;
            double currentPrice = position.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask;

            if ((position.TradeType == TradeType.Buy && currentPrice < breakEven) ||
                (position.TradeType == TradeType.Sell && currentPrice > breakEven))
            {
                ModifyPosition(position, position.StopLoss, breakEven);
                Print($"El precio está en contra, SL queda igual y TP movido a BE: {breakEven}");
            }
            else
            {
                ModifyPosition(position, breakEven, position.TakeProfit);
                Print($"SL movido a Break Even: {breakEven}");
            }
        }
    }
}

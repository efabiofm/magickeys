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

        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Margin (%)", DefaultValue = 0)]
        public double Margin { get; set; }

        [Parameter("SL Line Comment", DefaultValue = "SL")]
        public string SLLineComment { get; set; }

        protected override void OnStart()
        {
            botonEntrada = new Button
            {
                Text = "ENTER",
                Width = 60,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 10, 10, 10), // Derecha, primer botón arriba
                BackgroundColor = Color.Green
            };

            botonEntrada.Click += args =>
            {
                EjecutarEntrada();
            };

            botonCerrarMitad = new Button
            {
                Text = "HALF",
                Width = 60,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 10, 80, 10), // Derecha, más abajo
                BackgroundColor = Color.Orange
            };

            botonCerrarMitad.Click += args =>
            {
                CerrarMitad();
            };

            botonSLBE = new Button
            {
                Text = "BE",
                Width = 60,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 10, 150, 10), // Derecha, más abajo aún
                BackgroundColor = Color.Red
            };

            botonSLBE.Click += args =>
            {
                SLtoBE();
            };

            Chart.AddControl(botonEntrada);
            Chart.AddControl(botonCerrarMitad);
            Chart.AddControl(botonSLBE);
        }

        private void EjecutarEntrada()
        {
            ChartHorizontalLine slLine = null;

            foreach (var obj in Chart.Objects)
            {
                var line = obj as ChartHorizontalLine;
                if (line != null && line.Comment == SLLineComment)
                {
                    slLine = line;
                    break;
                }
            }

            if (slLine == null)
            {
                Print("No se encontró una línea de SL con el comentario: " + SLLineComment);
                return;
            }

            double stopLossPrice = slLine.Y;
            double entryPrice = Symbol.Bid;
            TradeType tradeType;
            double adjustedRiskPercent = RiskPercent - Margin;
            if (adjustedRiskPercent <= 0)
            {
                Print("El margen es demasiado grande en relación al riesgo.");
                return;
            }
            double riskAmount = Account.Balance * (adjustedRiskPercent / 100.0);
            double distanceToSL;

            if (entryPrice > stopLossPrice)
            {
                tradeType = TradeType.Buy;
                distanceToSL = entryPrice - stopLossPrice;
            }
            else
            {
                tradeType = TradeType.Sell;
                entryPrice = Symbol.Ask;
                distanceToSL = stopLossPrice - entryPrice;
            }

            if (distanceToSL <= 0)
            {
                Print("El SL está del lado incorrecto respecto al precio actual.");
                return;
            }

            double pipValue = Symbol.PipValue;
            double pipSize = Symbol.PipSize;
            double pips = distanceToSL / pipSize;

            if (pips < 0.01)
            {
                Print("El SL está demasiado cerca del precio actual.");
                return;
            }

            // Cálculo del volumen ideal (en unidades)
            double volumeInUnits = riskAmount / (pips * pipValue);

            // Redondeo hacia abajo al múltiplo permitido
            double minVolume = Symbol.VolumeInUnitsMin;
            double volumeStep = Symbol.VolumeInUnitsStep;
            double normalizedVolume = Math.Floor(volumeInUnits / volumeStep) * volumeStep;

            if (normalizedVolume < minVolume)
                normalizedVolume = minVolume;

            double stopLossPips = pips;

            Print($"Tipo: {tradeType}, Volumen: {normalizedVolume}, SL en pips: {stopLossPips}, nivel SL: {stopLossPrice}, riesgo usado: {adjustedRiskPercent}%");

            var result = ExecuteMarketOrder(tradeType, SymbolName, normalizedVolume, "EntradaRiesgo", stopLossPips, null);

            if (!result.IsSuccessful || result.Position == null)
            {
                Print("No se pudo abrir la posición.");
            }
        }

        private void CerrarMitad()
        {
            // Busca la posición activa con el label que usó el bot al abrir
            var position = Positions.Find("EntradaRiesgo", SymbolName);

            if (position == null)
            {
                Print("No hay posición activa para cerrar la mitad.");
                return;
            }

            double currentVolume = position.VolumeInUnits;
            double minVolume = Symbol.VolumeInUnitsMin;
            double volumeStep = Symbol.VolumeInUnitsStep;

            double halfVolume = Math.Floor((currentVolume / 2) / volumeStep) * volumeStep;

            if (halfVolume < minVolume)
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
                // Precio está en contra: deja el SL, mueve el TP a BE
                ModifyPosition(position, position.StopLoss, breakEven);
                Print($"El precio está en contra, SL queda igual y TP movido a BE: {breakEven}");
            }
            else
            {
                // Precio a favor: mueve el SL a BE (TP queda igual)
                ModifyPosition(position, breakEven, position.TakeProfit);
                Print($"SL movido a Break Even: {breakEven}");
            }
        }
    }
}

using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.CommonObjects;
using CryptoExchange.Net.Interfaces;
using CryptoExchange.Net.Objects.Sockets;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Intrinsics.X86;

namespace IndicatorBot
{
    internal class Program
    {
        public static readonly string API_KEY = "your api key here";
        public static readonly string API_SECRET = "your api secret here";
        public static readonly BybitEnvironment env = BybitEnvironment.DemoTrading; // Demo Account on Mainnet

        public static BybitRestClient restClient;

        // setup
        public static string symbol = "SOLUSDT";
        public static KlineInterval timeframe = KlineInterval.OneMinute;
        public static int RsiInterval = 14; // 14 is the default in a lot of cases. For 14, the bot will be looking for RSI 14 SMA 14.
        public static int CCIInterval = 20; // 20 is the default in a lot of cases. For 20, the bot will be looking for CCI 20 SMA 20.
        public static decimal qty = 1m; // 1 SOL in this case

        // this bot will only open a position when both RSI and CCI indicate a certain sentiment (BULLISH or BEARISH)
        // which marks...
        // Long Open = RSI >= 70 && CCI >= 100
        // Short Open = RSI <= 30 && CCI <= -100
        // opposing signals close one position and open the opposite one.
        public static bool openOrders = false;
        public static bool positionOpened = false;
        public static bool positionOpenRequest = false;

        public static BybitPosition? posOfInt;

        public static List<Kline> candles = new List<Kline>();
        public static bool firstUpdateFlag = true;

        static async Task Main(string[] args)
        {
            // initialize client for REST API interactions
            restClient = new BybitRestClient((client) =>
            {
                client.ApiCredentials = new ApiCredentials(API_KEY, API_SECRET);
                client.Environment = env;
            });

            // initialize WebSocket for public information gathering
            var publicWs = new BybitSocketClient((client) =>
            {
                client.Environment = env == BybitEnvironment.DemoTrading ? BybitEnvironment.Live : env; // Demo Trading should use Live data
            });

            // initialize WebSocket client for PRIVATE data
            var privateWs = new BybitSocketClient((client) =>
            {
                client.ApiCredentials = new ApiCredentials(API_KEY, API_SECRET);
                client.Environment = env;
            });

            // this block only necessary if bot is placing orders...
            if (openOrders)
            {
                // subscribe to order updates - we'll need that for when our limit orders are executed
                await privateWs.V5PrivateApi.SubscribeToOrderUpdatesAsync(HandleOrderUpdates);

                // check if position is opened
                var positions = await restClient.V5Api.Trading.GetPositionsAsync(
                    category: Category.Linear,
                    symbol: symbol
                    );
                posOfInt = positions.Data.List.FirstOrDefault((z) => z.Quantity != 0m);

                positionOpened = posOfInt is not null;
            }

            // request some initial candles to work with
            var klineReq = await restClient.V5Api.ExchangeData.GetKlinesAsync(
                category: Category.Linear,
                symbol: symbol,
                interval: timeframe,
                limit: RsiInterval * 8
                );
            var klineList = klineReq.Data.List.OrderBy((z) => z.StartTime).ToList();
            klineList.ForEach((z) =>
            {
                candles.Add(new Kline()
                {
                    ClosePrice = z.ClosePrice,
                    OpenPrice = z.OpenPrice,
                    OpenTime = z.StartTime,
                    HighPrice = z.HighPrice,
                    LowPrice = z.LowPrice,
                    Volume = z.Volume
                });
            });

            await publicWs.V5LinearApi.SubscribeToKlineUpdatesAsync(
                symbol: symbol,
                interval: timeframe,
                HandleKlineUpdatesAsync
                );

            while (true) Thread.Sleep(1000);
        }

        private static async void HandleOrderUpdates(DataEvent<IEnumerable<BybitOrderUpdate>> ev)
        {
            var updates = ev.Data;
            foreach (var update in updates)
            {
                if (update.Symbol != symbol) continue;

                if (update.Status == Bybit.Net.Enums.V5.OrderStatus.Filled &&
                    !positionOpened)
                {
                    positionOpened = true;
                    posOfInt = new BybitPosition() { Side = update.Side == OrderSide.Buy ? PositionSide.Buy : PositionSide.Sell }; // stub
                    positionOpenRequest = false;
                }
            }
        }

        private static async void HandleKlineUpdatesAsync(DataEvent<IEnumerable<BybitKlineUpdate>> ev)
        {
            var updates = ev.Data.ToList();
            foreach (var upd in updates)
            {
                if (firstUpdateFlag)
                {
                    // on our first update, we should check if candle we received in last step is still open
                    // it is GUARANTEED to be open at the time of request, however during the small time between GetKlinesAsync
                    // and HandleKlineUpdates, it could've been closed

                    // since HandleKlineUpdates keeps track of currently open candle, it's critical that we remove the last candle if it's still open
                    // or keep it if it isn't
                    var last = candles.Last();
                    if (upd.StartTime == last.OpenTime)
                        // it's the same candle, remove it
                        candles.Remove(last);

                    firstUpdateFlag = false;
                }

                var latestKline = new Kline()
                {
                    ClosePrice = upd.ClosePrice,
                    OpenPrice = upd.OpenPrice,
                    HighPrice = upd.HighPrice,
                    LowPrice = upd.LowPrice,
                    OpenTime = upd.StartTime,
                    Volume = upd.Volume,
                };

                while (candles.Count > RsiInterval * 8) candles.RemoveAt(0); // get rid of useless candles

                var candlesIncludingLast = new List<Kline>(candles)
                {
                    latestKline
                };

                // build SMMA N for all N of our candles
                var smmaN = GetSMMAClose(candlesIncludingLast, RsiInterval);

                // calculate N*2 RSI N values
                var rsiValues = GetRSIClose(candlesIncludingLast, RsiInterval).TakeLast(RsiInterval * 2 - 1).ToList();

                // apply Simple Moving Average (SMA N) to RSI values
                var rsiSmaValues = new List<decimal>();
                for (int i = 0; i < RsiInterval; i++)
                {
                    var applicableRSI = rsiValues.Skip(i).Take(RsiInterval).ToList();
                    var sum = applicableRSI.Sum();
                    var sma = sum / RsiInterval;
                    rsiSmaValues.Add(sma);
                }

                // calculate N*2 CCI N values
                var cciValues = GetCciHLC3(candlesIncludingLast, CCIInterval).TakeLast(CCIInterval * 2 - 1).ToList();

                // apply SMA to CCI values to get CCI N SMA N
                var CCISmaValues = new List<decimal?>();
                for (int i = 0; i < CCIInterval; i++)
                {
                    var applicableCCI = cciValues.Skip(i).Take(CCIInterval).ToList();
                    var sum = applicableCCI.Sum();
                    var sma = sum / CCIInterval;
                    CCISmaValues.Add(sma);
                }

                var latestRsi = rsiSmaValues.Last();
                await OutputValues(latestRsi, rsiValues.Last(), latestKline.ClosePrice.Value, smmaN.Last(), CCISmaValues.Last());

                if (upd.Confirm)
                {
                    candles.Add(latestKline);
                }
            }
        }

        public static async Task OutputValues(decimal rsiSMA, decimal rsi, decimal ltp, decimal smmaN, decimal? cciSMA)
        {
            bool rsiOverbought = false, rsiOversold = false;
            bool cciOverbought = false, cciOversold = false;

            string isPositionOpened = (positionOpened ? "YES" : "NO");
            if (!openOrders) isPositionOpened = "DISABLED";

            Console.Clear();
            Console.WriteLine($"Current price: ${ltp}");
            Console.WriteLine($"SMMA {RsiInterval}: ${Math.Round(smmaN, 2)}");
            Console.WriteLine($"Position opened: {isPositionOpened}");
            Console.WriteLine($"---");
            Console.WriteLine($"RSI {RsiInterval} SMA {RsiInterval} = {Math.Round(rsiSMA, 2)}");
            Console.WriteLine($"CCI {CCIInterval} SMA {CCIInterval} = {(cciSMA is null ? "UNDEFINED" : Math.Round(cciSMA.Value, 2))}");
            Console.WriteLine($"RSI {RsiInterval} (without SMA) = {Math.Round(rsi, 2)}");
            Console.WriteLine($"---");

            // output colorful indicator
            Console.Write($"RSI {RsiInterval} SMA {RsiInterval} shows asset is ");
            if (rsiSMA > 70)
            {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.Write("OVERBOUGHT! ");
                rsiOverbought = true;
            } else if (rsiSMA < 30)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("OVERSOLD! ");
                rsiOversold = true;
            }
            else
            {
                Console.Write("NEUTRAL! ");
            }
            int stage = (int)Math.Round(40 * (rsiSMA / 100));
            string bar = new string('#', stage).PadRight(40, '-');
            Console.WriteLine($"[{bar}]");
            Console.ResetColor();

            // output another colorful indicator
            Console.Write($"CCI {CCIInterval} SMA {CCIInterval} shows asset is ");
            if (cciSMA > 100)
            {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.Write("OVERBOUGHT! ");
                cciOverbought = true;
            }
            else if (cciSMA < -100)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("OVERSOLD! ");
                cciOversold = true;
            }
            else
            {
                Console.Write("NEUTRAL! ");
            }
            stage = (int)Math.Round(40 * (Math.Clamp(cciSMA is null ? 200 : cciSMA.Value + 200, -200, +200m) / 400));
            bar = new string('#', stage).PadRight(40, '-');
            Console.WriteLine($"[{bar}]");
            Console.ResetColor();

            if ((rsiOverbought && cciOverbought) ||
                (rsiOversold && cciOversold) && !positionOpenRequest && openOrders)
            {
                OrderSide side = (rsiOverbought && cciOverbought) ? OrderSide.Sell : OrderSide.Buy;

                if (positionOpened)
                {
                    if (posOfInt is null)
                    {
                        positionOpened = false;
                        return;
                    }

                    if ((posOfInt.Side == PositionSide.Buy && side == OrderSide.Buy) ||
                        (posOfInt.Side == PositionSide.Sell && side == OrderSide.Sell))
                    {
                        return;
                    }

                    // met all conditions to re-open!
                    positionOpenRequest = true;

                    // close it!
                    await CloseCurrentPosition(side);
                }

                // need to set this flag for the first order.
                positionOpenRequest = true;

                // open a new position
                var openingOrder = await restClient.V5Api.Trading.PlaceOrderAsync(
                    category: Category.Linear,
                    symbol: symbol,
                    side: side,
                    type: NewOrderType.Market,
                    quantity: qty
                    );

                if (!openingOrder.Success)
                {
                    Console.WriteLine($"Non-fatal error. {openingOrder.Error.Message} - {openingOrder.Error.Code}");
                    return;
                }
                Console.WriteLine($"Order successfully placed! OrderID#{openingOrder.Data.OrderId}");
            } 
        }

        public static async Task CloseCurrentPosition(OrderSide oppositePositionSide)
        {
            var closingOrder = await restClient.V5Api.Trading.PlaceOrderAsync(
                        category: Category.Linear,
                        symbol: symbol,
                        side: oppositePositionSide,
                        type: NewOrderType.Market,
                        // this combination closes the order
                        quantity: 0m,
                        reduceOnly: true,
                        closeOnTrigger: true
                        );

            if (!closingOrder.Success)
            {
                Console.WriteLine($"Non-fatal error. {closingOrder.Error.Message} - {closingOrder.Error.Code}");
                return;
            }

            if (posOfInt is not null)
            {
                posOfInt = null;
                positionOpened = false;
            }
        }

        #region Indicator Math
        public static List<decimal> GetRSIClose(List<Kline> candles, int period)
        {
            int length = candles.Count;
            decimal avgGain = 0;
            decimal avgLoss = 0;

            List<decimal> results = new(length);
            decimal[] gain = new decimal[length];
            decimal[] loss = new decimal[length];
            decimal lastValue;

            if (length == 0)
            {
                return results;
            }
            else
            {
                lastValue = candles[0].ClosePrice.Value;
            }

            for (int i = 0; i < length; i++)
            {
                decimal value = candles[i].ClosePrice.Value;

                decimal rsi = 0;

                gain[i] = (value > lastValue) ? value - lastValue : 0;
                loss[i] = (value < lastValue) ? lastValue - value : 0;
                lastValue = value;

                // calculate RSI
                if (i > period)
                {
                    avgGain = ((avgGain * (period - 1)) + gain[i]) / period;
                    avgLoss = ((avgLoss * (period - 1)) + loss[i]) / period;

                    if (avgLoss > 0)
                    {
                        decimal rs = avgGain / avgLoss;
                        rsi = 100 - (100 / (1 + rs));
                    }
                    else
                    {
                        rsi = 100;
                    }
                }
                else if (i == period) // initialize average gain
                {
                    decimal sumGain = 0;
                    decimal sumLoss = 0;

                    for (int p = 1; p <= period; p++)
                    {
                        sumGain += gain[p];
                        sumLoss += loss[p];
                    }

                    avgGain = sumGain / period;
                    avgLoss = sumLoss / period;

                    rsi = (avgLoss > 0) ? 100 - (100 / (1 + (avgGain / avgLoss))) : 100;
                }

                results.Add(rsi);
            }

            return results;
        }

        internal static List<decimal?> GetCciHLC3(List<Kline> candles, int period)
        {
            int length = candles.Count;
            List<decimal?> results = new(length);
            decimal[] tp = new decimal[length];

            // roll through quotes
            for (int i = 0; i < length; i++)
            {
                var q = candles[i];
                tp[i] = (q.HighPrice.Value + q.LowPrice.Value + q.ClosePrice.Value) / 3m;

                decimal? r = null;
                if (i + 1 >= period)
                {
                    // average TP over lookback
                    decimal avgTp = 0;
                    for (int p = i + 1 - period; p <= i; p++)
                    {
                        avgTp += tp[p];
                    }

                    avgTp /= period;

                    // average Deviation over lookback
                    decimal avgDv = 0;
                    for (int p = i + 1 - period; p <= i; p++)
                    {
                        avgDv += Math.Abs(avgTp - tp[p]);
                    }

                    avgDv /= period;

                    r = (avgDv == 0) ? null : ((tp[i] - avgTp) / (0.015m * avgDv));
                }
                results.Add(r);
            }

            return results;
        }

        public static List<decimal> GetSMMAClose(List<Kline> candles, int interval)
        {
            // calculate the initial SMA (first SMMA value) based on the first N candles
            decimal initialSum = candles.Take(interval).Sum(z => z.ClosePrice).Value;
            decimal previousSmma = initialSum / interval;

            // calculate subsequent SMMA values, but only store the last N which we'll need for RSI
            var smmaNClose = new List<decimal>();
            for (int i = 1; i < candles.Count; i++)
            {
                decimal currentPrice = candles[i].ClosePrice.Value;
                decimal currentSmma = (previousSmma * (interval - 1) + currentPrice) / interval;

                if (i >= candles.Count - interval)
                {
                    smmaNClose.Add(currentSmma); // Store only the last N SMMA values
                }

                previousSmma = currentSmma; // Update for the next iteration
            }
            return smmaNClose;
        }
        #endregion
    }
}

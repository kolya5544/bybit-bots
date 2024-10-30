using System;
using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Authentication;
using Bybit.Net.Enums;
using CryptoExchange.Net.Objects.Sockets;
using System.Drawing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace GridBot
{
    internal class Program
    {
        public static readonly string API_KEY = "your api key here";
        public static readonly string API_SECRET = "your api secret here";
        public static readonly BybitEnvironment env = BybitEnvironment.DemoTrading; // Demo Account on Mainnet

        // Grid options setup
        public static OrderSide side = OrderSide.Sell; // Buy = longs only, Sell = shorts only
        public static GridType gridType = GridType.Arithmetic;
        public static string symbol = "HMSTRUSDT";
        public static decimal priceLowerEnd = 0.002822m;
        public static decimal priceUpperEnd = 0.002906m;
        public static int gridCount = 20;
        public static decimal initialOrderSize = 10000m; // in base coin
        public static int leverage = 10;

        // vars
        public static BybitRestClient restClient;
        public static decimal currentPrice = 0m;
        public static bool positionOpened = false;
        public static bool positionOpenRequest = false;
        public static BybitLinearInverseSymbol idData;
        public static List<decimal> gridLines = new List<decimal>();

        static async Task Main(string[] args)
        {
            Console.WriteLine($"Starting the bot.");

            // initialize client for REST API interactions
            restClient = new BybitRestClient((client) =>
            {
                client.ApiCredentials = new ApiCredentials(API_KEY, API_SECRET);
                client.Environment = env;
            });

            // set leverage requested
            var setLeverage = await restClient.V5Api.Account.SetLeverageAsync(
                category: Category.Linear,
                symbol: symbol,
                buyLeverage: leverage,
                sellLeverage: leverage
                );

            if (!setLeverage.Success)
            {
                Console.WriteLine($"Error! {setLeverage.Error.Message} - {setLeverage.Error.Code}");
            }

            // initialize WebSocket client for PUBLIC data
            var publicWs = new BybitSocketClient();

            // initialize WebSocket client for PRIVATE data
            var privateWs = new BybitSocketClient((client) =>
            {
                client.ApiCredentials = new ApiCredentials(API_KEY, API_SECRET);
                client.Environment = env;
            });

            // subscribe to price updates for symbol of interest
            await publicWs.V5LinearApi.SubscribeToTickerUpdatesAsync(symbol: symbol, HandleTickerUpdates);

            while (currentPrice == 0m) Thread.Sleep(1000); // wait for price data to update

            // acquire instrument data on the instrument specified
            var id = await restClient.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(
                category: Category.Linear,
                symbol: symbol
                );
            idData = id.Data.List.First();

            // === actual bot routine ===

            // check if position is opened
            var positions = await restClient.V5Api.Trading.GetPositionsAsync(
                category: Category.Linear,
                symbol: symbol
                );
            var posOfInt = positions.Data.List.FirstOrDefault((z) => (z.Side == PositionSide.Buy && side == OrderSide.Buy) ||
                                                                     (z.Side == PositionSide.Sell && side == OrderSide.Sell));

            positionOpened = posOfInt is not null;
            Console.WriteLine($"Position opened: {(positionOpened ? "YES" : "NO")}");

            // subscribe to order updates - we'll need that for when our limit orders are executed
            await privateWs.V5PrivateApi.SubscribeToOrderUpdatesAsync(HandleOrderUpdates);

            while (true)
            {
                positionOpenRequest = true;

                // create a list of limit orders in accordance with grid bot logic
                UpdateGridLines();

                // count the amount of lines above (for long) or below (for short) current market grid line
                // LONG BOT
                // --- SELL 1 BTC
                // --- SELL 1 BTC
                // --- current market grid line
                // <- market price
                // --- BUY 1 BTC
                // yields: 2, since there are two sell lines above current market price
                int currentGridLine = GetCurrentGridLine();

                // calculate the amount of grid lines
                // for LONG bot, it'll be the amount of grid lines ABOVE current grid line
                // for SHORT bot, it's going to be amount of grid lines BELOW current grid line
                var countGridLines = side == OrderSide.Buy ? gridLines.Count - currentGridLine - 1 : currentGridLine;

                await restClient.V5Api.Trading.CancelAllOrderAsync(
                        category: Category.Linear,
                        symbol: symbol
                        );

                if (!positionOpened)
                {
                    Console.WriteLine($"Opening a new position...");

                    if (countGridLines == 0)
                    {
                        var rangeLower = side == OrderSide.Sell ? gridLines[1] : priceLowerEnd;
                        var rangeUpper = side == OrderSide.Buy ? gridLines[gridLines.Count - 2] : priceUpperEnd;
                        Console.WriteLine($"Market outside the correct trading range. Current price: {currentPrice}, effective range: [{rangeLower}; {rangeUpper}]");
                        Thread.Sleep(10000);
                        continue;
                    }

                    Console.WriteLine($"Found a total of {countGridLines} grid lines applicable. Opening a position of size {initialOrderSize} * {countGridLines} = {initialOrderSize * countGridLines}");

                    // open the position at current price if there are none
                    var orderQuantity = decimal.Round(initialOrderSize * countGridLines, idData.LotSizeFilter.QuantityStep.Scale, MidpointRounding.ToZero);

                    var openOrder = await restClient.V5Api.Trading.PlaceOrderAsync(
                        category: Category.Linear,
                        symbol: symbol,
                        side: side,
                        type: NewOrderType.Market,
                        quantity: orderQuantity
                        );

                    if (!openOrder.Success)
                    {
                        Console.WriteLine($"Fatal error! {openOrder.Error.Message} - {openOrder.Error.Code}");
                        return;
                    }
                } else
                {
                    var gridPos = 0;
                    if (side == OrderSide.Buy)
                    {
                        gridPos = gridLines.Count - (int)Math.Round(posOfInt.Quantity / initialOrderSize) - 1;
                    } else
                    {
                        gridPos = (int)Math.Round(posOfInt.Quantity / initialOrderSize) - 1;
                    }
                    await PlaceLimitOrders(countGridLines, gridPos);
                }

                while (positionOpenRequest) Thread.Sleep(500); // wasting CPU cycles
            }
        }

        private static int GetCurrentGridLine(decimal? price = null)
        {
            if (price is null) price = currentPrice;

            int currentGridLine = -1;
            try
            {
                currentGridLine = gridLines.FindIndex((z) => z >= currentPrice) - (side == OrderSide.Buy ? 0 : 1);
            }
            catch
            {
                currentGridLine = gridCount;
            }
            return currentGridLine;
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

                    // BTCUSDT ENTRY: $72100, GRID COUNT: 6, INITIAL ENTRY: 1 BTC * grid lines above
                    // SIDE: LONG                                         | SIDE: SHORT
                    // ---
                    //6 $117616.9 - Close Long (1 BTC)                    | $117616.9 - Open Short (1 BTC)
                    //5 $93125.5 - Close Long (1 BTC)                     | $93125.5 - Open Short (1 BTC)
                    //4 $73734 - CURRENT GRID LINE (no orders)            | $73734 - Open Short (1 BTC)
                    // <- market price ($72100)                           | <- market price ($72100)
                    //3 $58380.4 - Open Long (1 BTC)                      | $58380.4 - CURRENT GRID LINE (no orders)
                    //2 $46223.9 - Open Long (1 BTC)                      | $46223.9 - Close Short (1 BTC)
                    //1 $36598.7 - Open Long (1 BTC)                      | $36598.7 - Close Short (1 BTC)
                    //0 $28977.8 - Open Long (1 BTC)                      | $28977.8 - Close Short (1 BTC)

                    int currentGridLine = GetCurrentGridLine(); // index, not price!
                    var countGridLines = side == OrderSide.Buy ? gridLines.Count - currentGridLine - 1 : currentGridLine;

                    await PlaceLimitOrders(countGridLines, currentGridLine);
                    
                    continue;
                }

                if (update.Status == Bybit.Net.Enums.V5.OrderStatus.Filled)
                {
                    var execPrice = update.Price.Value; // order price

                    var correspondingGridPrice = gridLines.MinBy((z) => Math.Abs(z - execPrice));
                    var correspondingGridIndex = gridLines.IndexOf(correspondingGridPrice);

                    if (update.Side == side)
                    {
                        // one of our OPEN POSITION limit orders got taken!
                        // time to create a new opposite order to close the position.

                        var newGridIndex = correspondingGridIndex + (side == OrderSide.Buy ? 1 : -1);
                        var newGridPrice = gridLines[newGridIndex];

                        var price = decimal.Round(newGridPrice, idData.PriceFilter.TickSize.Scale, MidpointRounding.ToZero);

                        Console.WriteLine($"Limit open position order executed @ ${execPrice}. Attempting to create a close position order @ ${price}");

                        // place the limit order
                        var limitOrder = await restClient.V5Api.Trading.PlaceOrderAsync(
                            category: Category.Linear,
                            symbol: symbol,
                            side: side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy,
                            type: NewOrderType.Limit,
                            quantity: initialOrderSize,
                            price: price,
                            reduceOnly: true
                            );

                        if (!limitOrder.Success)
                        {
                            Console.WriteLine($"Fatal error! {limitOrder.Error.Message} - {limitOrder.Error.Code}");
                            return;
                        }
                    }
                    else
                    {
                        // one of our CLOSE POSITION limit orders got taken!
                        // time to create a new opposite order to OPEN the position.

                        var newGridIndex = correspondingGridIndex + (side == OrderSide.Buy ? -1 : 1);
                        var newGridPrice = gridLines[newGridIndex];

                        var price = decimal.Round(newGridPrice, idData.PriceFilter.TickSize.Scale, MidpointRounding.ToZero);

                        Console.WriteLine($"Limit close position order executed @ ${execPrice}. Attempting to create a open position order @ ${price}");

                        // place the limit order
                        var limitOrder = await restClient.V5Api.Trading.PlaceOrderAsync(
                            category: Category.Linear,
                            symbol: symbol,
                            side: side,
                            type: NewOrderType.Limit,
                            quantity: initialOrderSize,
                            price: price
                            );

                        if (!limitOrder.Success)
                        {
                            Console.WriteLine($"Fatal error! {limitOrder.Error.Message} - {limitOrder.Error.Code}");
                            return;
                        }
                    }
                }
            }
        }

        private static async Task PlaceLimitOrders(int countGridLines, int currentGridLine)
        {
            // place limit orders for closing our position
            for (int i = 0; i < countGridLines; i++)
            {
                // this next line is tough.
                // essentially, it takes grid positions above current grid line for LONG positions
                // and grid positions below current grid line for SHORT positions
                var appropriateGridPosition = gridLines[currentGridLine + (i + 1) * (side == OrderSide.Buy ? 1 : -1)];

                // convert to Bybit-applicable price
                var price = decimal.Round(appropriateGridPosition, idData.PriceFilter.TickSize.Scale, MidpointRounding.ToZero);
                Console.WriteLine($"Putting CLOSE order at ${price}");

                // place the limit order
                var limitOrder = await restClient.V5Api.Trading.PlaceOrderAsync(
                    category: Category.Linear,
                    symbol: symbol,
                    side: side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy,
                    type: NewOrderType.Limit,
                    quantity: initialOrderSize,
                    price: price,
                    reduceOnly: true
                    );

                if (!limitOrder.Success)
                {
                    Console.WriteLine($"Fatal error! {limitOrder.Error.Message} - {limitOrder.Error.Code}");
                    return;
                }
            }

            // place limit orders for opening new positions
            var newCount = gridLines.Count - countGridLines - 1;
            for (int i = 0; i < newCount; i++)
            {
                var newApprGridPos = gridLines[side == OrderSide.Buy ? i : (gridLines.Count - i - 1)];

                // convert to Bybit-applicable price
                var price = decimal.Round(newApprGridPos, idData.PriceFilter.TickSize.Scale, MidpointRounding.ToZero);
                Console.WriteLine($"Putting OPEN order at ${price}");

                // place the limit order
                var limitOrder = await restClient.V5Api.Trading.PlaceOrderAsync(
                    category: Category.Linear,
                    symbol: symbol,
                    side: side,
                    type: NewOrderType.Limit,
                    quantity: initialOrderSize,
                    price: price
                    );

                if (!limitOrder.Success)
                {
                    Console.WriteLine($"Fatal error! {limitOrder.Error.Message} - {limitOrder.Error.Code}");
                    return;
                }
            }
        }

        public static void UpdateGridLines()
        {
            // for arithmetic bot, intervals = (upper price - lower price) / grid_count in QUOTE COIN
            // for geometric bot, intervals = (upper price / lower price) ^ (1 / grid_count) - 1 in %

            var intervalGeometric = (decimal)Math.Pow((double)(priceUpperEnd / priceLowerEnd), 1d / gridCount) - 1;
            var intervalArithmetic = (priceUpperEnd - priceLowerEnd) / gridCount;

            lock (gridLines)
            {
                gridLines = [];

                var currentPrice = priceLowerEnd;
                gridLines.Add(currentPrice);
                for (int i = 0; i < gridCount; i++)
                {
                    if (gridType == GridType.Geometric)
                    {
                        var newPrice = currentPrice * (1 + intervalGeometric);
                        gridLines.Add(newPrice);
                        currentPrice = newPrice;
                    } else if (gridType == GridType.Arithmetic)
                    {
                        gridLines.Add(currentPrice + intervalArithmetic * (i + 1));
                    }
                }
            }
        }

        private static void HandleTickerUpdates(DataEvent<BybitLinearTickerUpdate> ev)
        {
            var d = ev.Data;
            if (d.LastPrice is not null) currentPrice = d.LastPrice.Value;
        }
    }

    public enum GridType
    {
        Arithmetic, Geometric
    }
}

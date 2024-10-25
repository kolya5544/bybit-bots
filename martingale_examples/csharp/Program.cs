using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects.Sockets;
using Newtonsoft.Json;
using System.Xml.Linq;

namespace MartingaleBot
{
    internal class Program
    {
        public static readonly string API_KEY = "g3tUdWih4rI0d2MTt0";
        public static readonly string API_SECRET = "rOUmxpSJ6idhpck2SO7RW6Pf8p5vKcLDRqWC";
        public static readonly BybitEnvironment env = BybitEnvironment.DemoTrading; // Demo Account on Mainnet

        // Martingale options setup
        public static Category botCategory = Category.Linear;
        public static string symbol = "TONUSDT";
        public static OrderSide side = OrderSide.Buy;
        public static decimal initialOrderSize = 500m; // in USDT
        // strategy parameters
        public static decimal priceMovement = 0.02m; // add position order after 2% of price movement
        public static decimal posMultiplier = 1.2m; // each additional order is 1.2x times the previous order
        public static int leverage = 10; // 10x
        public static int maxAddition = 5; // no more than 5 additional orders
        public static decimal takeProfit = 0.02m; // take profit after 2% of price movement
        public static bool enableLoop = true; // should the bot continue running after takeProfit?
        // price range to operate
        public static decimal minPriceRange = 4.5m;
        public static decimal maxPriceRange = 8m;


        // vars
        public static BybitRestClient restClient;
        public static decimal currentPrice = 0m;
        public static bool positionOpened = false;
        public static bool positionOpenRequest = false;
        public static BybitLinearInverseSymbol idData;

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
                category: botCategory,
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
                category: botCategory,
                symbol: symbol
                );
            idData = id.Data.List.First();

            // === actual bot routine ===

            // check if position is opened
            var positions = await restClient.V5Api.Trading.GetPositionsAsync(
                category: botCategory,
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
                if (currentPrice < minPriceRange || currentPrice > maxPriceRange)
                {
                    Console.WriteLine($"Bot terminated! Fell out of price range specified: {currentPrice} is not in [{minPriceRange}; {maxPriceRange}]");
                    return;
                }
                positionOpenRequest = true;
                if (!positionOpened)
                {
                    await restClient.V5Api.Trading.CancelAllOrderAsync(
                        category: botCategory,
                        symbol: symbol
                        );

                    Console.WriteLine($"Opening a new position...");

                    // open the position at current price if there are none
                    var orderQuantity = decimal.Round(initialOrderSize / currentPrice, idData.LotSizeFilter.QuantityStep.Scale, MidpointRounding.ToZero);

                    var openOrder = await restClient.V5Api.Trading.PlaceOrderAsync(
                        category: botCategory,
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
                }
                else
                {
                    await UpdateTakeProfitAsync();
                }

                while (positionOpenRequest) Thread.Sleep(500); // wasting CPU cycles
                if (!enableLoop) return;
            }
        }

        private static async Task UpdateTakeProfitAsync()
        {
            // get current position
            var positions = await restClient.V5Api.Trading.GetPositionsAsync(
                category: botCategory,
                symbol: symbol
                );
            var posOfInt = positions.Data.List.FirstOrDefault((z) => (z.Side == PositionSide.Buy && side == OrderSide.Buy) ||
                                                                     (z.Side == PositionSide.Sell && side == OrderSide.Sell));

            // calculate appropriate TP
            var avgPrice = posOfInt.AveragePrice.Value;
            var tp = avgPrice * (1 + (side == OrderSide.Buy ? +takeProfit : -takeProfit));
            tp = decimal.Round(tp, idData.PriceFilter.TickSize.Scale, MidpointRounding.ToZero);

            Console.WriteLine($"Attempt to update TP value of a position.");

            // update TP for position
            var tpSet = await restClient.V5Api.Trading.SetTradingStopAsync(
                category: botCategory,
                symbol: symbol,
                positionIdx: Bybit.Net.Enums.V5.PositionIdx.OneWayMode,
                takeProfit: tp
                );

            if (!tpSet.Success)
            {
                Console.WriteLine($"Fatal error! {tpSet.Error.Message} - {tpSet.Error.Code}");
                return;
            }
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
                    var avgPrice = update.AveragePrice.Value;

                    // create a list of limit orders in accordance with additional orders
                    // BTCUSDT ENTRY: $50000, ADDITIONAL ORDERS: 5, INITIAL ENTRY: 1 BTC
                    // PRICE MOVEMENT: 2%, POS. MULTIPLIER: 120%, SIDE: LONG
                    // ---
                    // $49000 - 1.2 BTC
                    // $48020 - 1.44 BTC
                    // $47059.6 - 1.728 BTC
                    // $46118.4 - 2.074 BTC
                    // $45196 - 2.488 BTC
                    var previousPrice = avgPrice;
                    var previousQuantity = update.Quantity;
                    for (var i = 0; i < maxAddition; i++)
                    {
                        // the next line feels too complex? It's basically $50000 - 2% for long position, and $50000 + 2% for short position.
                        var newPrice = previousPrice * (1 + (side == OrderSide.Buy ? -priceMovement : +priceMovement));
                        var newQuantity = previousQuantity * posMultiplier;

                        // process in accordance with Bybit requirements on quantity and price.
                        newQuantity = decimal.Round(newQuantity, idData.LotSizeFilter.QuantityStep.Scale, MidpointRounding.ToZero);
                        newPrice = decimal.Round(newPrice, idData.PriceFilter.TickSize.Scale, MidpointRounding.ToZero);

                        previousPrice = newPrice;
                        previousQuantity = newQuantity;

                        // place the limit order
                        var limitOrder = await restClient.V5Api.Trading.PlaceOrderAsync(
                            category: botCategory,
                            symbol: symbol,
                            side: side,
                            type: NewOrderType.Limit,
                            quantity: newQuantity,
                            price: newPrice
                            );

                        if (!limitOrder.Success)
                        {
                            Console.WriteLine($"Fatal error! {limitOrder.Error.Message} - {limitOrder.Error.Code}");
                            return;
                        }
                    }
                    await UpdateTakeProfitAsync();

                    continue;
                }

                if (update.Status == Bybit.Net.Enums.V5.OrderStatus.Filled &&
                    update.Side == side)
                {
                    // one of our limit orders got taken!
                    // time to update TP.
                    Console.WriteLine($"Limit order executed.");
                    await UpdateTakeProfitAsync();
                }

                if (update.Status == Bybit.Net.Enums.V5.OrderStatus.Filled &&
                    update.Side != side)
                {
                    // our position was closed! hooray!
                    positionOpened = false;
                    positionOpenRequest = false;
                    Console.WriteLine($"Position closed, hopefully by TP. Trying to open a new one.");
                }
            }
        }

        private static void HandleTickerUpdates(DataEvent<BybitLinearTickerUpdate> ev)
        {
            var d = ev.Data;
            if (d.LastPrice is not null) currentPrice = d.LastPrice.Value;
        }
    }
}

using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects.Sockets;

namespace WSExample
{
    internal class Program
    {
        public static readonly string API_KEY = "4BAE52aOpxhqWGTbXl";
        public static readonly string API_SECRET = "l6xAmFWmqE411Kpp5yigY9WapBXRYeiwQHOr";
        public static readonly BybitEnvironment env = BybitEnvironment.Testnet; // Testnet account. KEEP IN MIND!! Demo doesn't support WebSocket trading.

        public static decimal BtcPrice = 0m;

        static async Task Main(string[] args)
        {
            // initialize WebSocket client for WS API interactions
            var wsClient = new BybitSocketClient((client) =>
            {
                client.ApiCredentials = new ApiCredentials(API_KEY, API_SECRET);
                client.Environment = env;
            });

            // example subscribing to updates of BTCUSDT price (linear perp futures)
            // https://bybit-exchange.github.io/docs/v5/websocket/public/ticker
            Console.WriteLine($"[MAIN] Subscribing to BTCUSDT price updates...");
            await wsClient.V5LinearApi.SubscribeToTickerUpdatesAsync(
                symbol: "BTCUSDT",
                handler: PriceUpdateHandler
                );

            // example subscribing to order updates for your account
            // https://bybit-exchange.github.io/docs/v5/websocket/private/order
            Console.WriteLine($"[MAIN] Subscribing to order updates...");
            await wsClient.V5PrivateApi.SubscribeToOrderUpdatesAsync(
                handler: OrderUpdateHandler
                );

            // wait for BTC price updates to get proper price
            while (BtcPrice == 0m) Thread.Sleep(500);

            // example creating an order through WebSocket.
            // WebSocker order placement is much faster than REST API due to less overhead
            Console.WriteLine($"[MAIN] Creating a LONG limit order for BTCUSDT...");
            var order = await wsClient.V5PrivateApi.PlaceOrderAsync(
                category: Category.Linear,
                symbol: "BTCUSDT",
                side: OrderSide.Buy,
                type: NewOrderType.Limit,
                quantity: 0.5m,
                price: BtcPrice // this is acquired from PriceUpdateHandler
                );

            if (!order.Success)
            {
                Console.WriteLine($"[MAIN] Failed to create a new order! Error: {order.Error.Message}");
                return;
            }
            Console.WriteLine($"[MAIN] Created new order #{order.Data.OrderId}! Waiting indefinitely...");

            while (true) Thread.Sleep(1000);
        }

        private static void OrderUpdateHandler(DataEvent<IEnumerable<BybitOrderUpdate>> ev)
        {
            var data = ev.Data;
            foreach (var upd in data)
            {
                Console.WriteLine($"[ORD] Order update for Ord#{upd.OrderId}@{upd.Symbol}. Status = {upd.Status}, filled quantity = {upd.QuantityFilled}/{upd.Quantity} (remaining: {upd.QuantityRemaining})");
            }
        }

        private static void PriceUpdateHandler(DataEvent<BybitLinearTickerUpdate> ev)
        {
            if (ev.Data.LastPrice is not null)
            {
                BtcPrice = ev.Data.LastPrice.Value;
                Console.WriteLine($"[PRICE] BTCUSDT current price = ${BtcPrice}");
            }
        }
    }
}

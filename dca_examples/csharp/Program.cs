using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Enums.V5;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Interfaces;
using System.Xml.Linq;

namespace DCABot
{
    internal class Program
    {
        public static readonly string API_KEY = "your api key here";
        public static readonly string API_SECRET = "your api secret here";
        public static readonly BybitEnvironment env = BybitEnvironment.DemoTrading; // Demo Account on Mainnet

        // configure DCA
        public static string symbol = "MNTUSDT";
        public static decimal investmentAmount = 1000m; // in USDT
        public static long intervalMinutes = 10080; // 60 minutes * 24 hours * 7 days = 10080 minutes

        static async Task Main(string[] args)
        {
            Console.WriteLine($"Starting the bot.");

            // initialize client for REST API interactions
            var restClient = new BybitRestClient((client) =>
            {
                client.ApiCredentials = new ApiCredentials(API_KEY, API_SECRET);
                client.Environment = env;
            });

            while (true)
            {
                // get all recent orders
                // https://bybit-exchange.github.io/docs/v5/order/order-list
                var orders = await restClient.V5Api.Trading.GetOrderHistoryAsync(
                    category: Category.Spot,
                    symbol: symbol
                    );

                var lastOrder = orders.Data.List.FirstOrDefault((z) => z.Status == Bybit.Net.Enums.V5.OrderStatus.Filled &&
                                                                       z.Side == OrderSide.Buy);

                DateTime orderTimestamp = lastOrder is null ? DateTime.MinValue : lastOrder.CreateTime;

                // check if more than intervalMinutes passed
                if (DateTime.UtcNow - orderTimestamp > TimeSpan.FromMinutes(intervalMinutes))
                {
                    // place a market order
                    // https://bybit-exchange.github.io/docs/v5/order/create-order
                    var order = await restClient.V5Api.Trading.PlaceOrderAsync(
                        category: Category.Spot,
                        symbol: symbol,
                        side: OrderSide.Buy,
                        type: NewOrderType.Market,
                        quantity: investmentAmount,
                        marketUnit: MarketUnit.QuoteAsset
                        );

                    if (!order.Success)
                    {
                        Console.WriteLine($"Fatal error! {order.Error.Message} - {order.Error.Code}");
                        return;
                    }
                    Console.WriteLine($"Success! Order ID#{order.Data.OrderId}");
                }

                Thread.Sleep(10000);
            }
        }
    }
}

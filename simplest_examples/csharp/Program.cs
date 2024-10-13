using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Enums;
using CryptoExchange.Net.Authentication;

namespace SimpleExample
{
    internal class Program
    {
        public static readonly string API_KEY = "MtE4aoOugtgMx8q4cs";
        public static readonly string API_SECRET = "UOu1Qt17nVwuCrjkihOmkLCkcws2LUwSpuzk";
        public static readonly BybitEnvironment env = BybitEnvironment.DemoTrading; // Demo Account on Mainnet


        static async Task Main(string[] args)
        {
            // initialize client for REST API interactions
            var restClient = new BybitRestClient((client) =>
            {
                client.ApiCredentials = new ApiCredentials(API_KEY, API_SECRET);
                client.Environment = env;
            });

            // example getting "Market" -> "Get Instruments Info" for Spot
            // https://bybit-exchange.github.io/docs/v5/market/instrument
            var spotSymbols = await restClient.V5Api.ExchangeData.GetSpotSymbolsAsync();
            var symbolList = spotSymbols.Data.List.ToList();

            Console.WriteLine($"Found a total of {symbolList.Count} spot symbols.");
            Console.WriteLine();

            var bitcoin = symbolList.First((z) => z.Name == "BTCUSDT");
            Console.WriteLine($"Information about BTCUSDT spot, Lot Size Filter:");
            Console.WriteLine($"Precision for base coin: {bitcoin.LotSizeFilter.BasePrecision} {bitcoin.BaseAsset}");
            Console.WriteLine($"Precision for quote coin: {bitcoin.LotSizeFilter.QuotePrecision} {bitcoin.QuoteAsset}");
            Console.WriteLine($"Max Order Quantity: {bitcoin.LotSizeFilter.MaxOrderQuantity} {bitcoin.BaseAsset}");
            Console.WriteLine($"Max Order Value: {bitcoin.LotSizeFilter.MaxOrderValue} {bitcoin.QuoteAsset}");
            Console.WriteLine($"Min Order Quantity: {bitcoin.LotSizeFilter.MinOrderQuantity} {bitcoin.BaseAsset}");
            Console.WriteLine($"Min Order Value: {bitcoin.LotSizeFilter.MinOrderValue} {bitcoin.QuoteAsset}");
            Console.WriteLine();

            // example placing an order "Trade" -> "Place Order" for Linear (futures) Perpetual contract
            // https://bybit-exchange.github.io/docs/v5/order/create-order
            var newOrder = await restClient.V5Api.Trading.PlaceOrderAsync(
                category: Category.Linear, // LINEAR = futures, perpetuals. INVERSE = inverse futures like BTCUSD. SPOT = spot, OPTIONS = options
                symbol: "TONUSDT",
                side: OrderSide.Buy, // buy = LONG, sell = SHORT
                type: NewOrderType.Market, // market order
                quantity: 100m, // quantity for LINEAR is ALWAYS base coin. In this case, 100 TON will be purchased.
                // optional parameters:
                timeInForce: TimeInForce.ImmediateOrCancel
                );

            if (!newOrder.Success)
            {
                Console.WriteLine($"Failure creating an order! Error: {newOrder.Error.Message}");
                return;
            }

            Console.WriteLine($"Successful order creation! Order ID: {newOrder.Data.OrderId}");
            Console.WriteLine();

            // example get list of positions "Position" -> "Get Position Info"
            // https://bybit-exchange.github.io/docs/v5/position
            var positions = await restClient.V5Api.Trading.GetPositionsAsync(
                category: Category.Linear, // futures positions only
                settleAsset: "USDT" // that is also required.
                );
            var positionList = positions.Data.List.ToList();
            Console.WriteLine($"Found a total of {positionList.Count} positions.");
            Console.WriteLine();

            var tonPosition = positionList.FirstOrDefault((z) => z.Symbol == "TONUSDT");
            if (tonPosition is null)
            {
                Console.WriteLine($"TONUSDT position was not found!");
                return;
            }

            Console.WriteLine($"TONUSDT position successfully found! Info:");
            Console.WriteLine($"Leverage: {tonPosition.Leverage}x");
            Console.WriteLine($"Quantity: {tonPosition.Quantity} TON");
            Console.WriteLine($"Value: {tonPosition.PositionValue} USDT");
            Console.WriteLine($"Entry price: {tonPosition.AveragePrice} USDT");
            Console.WriteLine($"Mark price: {tonPosition.MarkPrice} USDT");
            Console.WriteLine($"Estimated liquidation price: {(tonPosition.LiquidationPrice is null ? "-" : tonPosition.LiquidationPrice)} USDT");
            Console.WriteLine($"Initial Margin: {tonPosition.InitialMargin} USDT");
            Console.WriteLine($"Maintenance Margin: {tonPosition.MaintenanceMargin} USDT");
            Console.WriteLine($"Unrealized PnL: {tonPosition.UnrealizedPnl} USDT");
            Console.WriteLine($"Realized PnL: {tonPosition.RealizedPnl} USDT");
            Console.WriteLine();

            // closing all positions
            foreach (var pos in positionList)
            {
                Console.WriteLine($"Closing {pos.Symbol} position...");
                var closeOrder = await restClient.V5Api.Trading.PlaceOrderAsync(
                    category: Category.Linear,
                    symbol: pos.Symbol,
                    side: pos.Side == PositionSide.Buy ? OrderSide.Sell : OrderSide.Buy, // opposite
                    type: NewOrderType.Market,
                    quantity: 0, // if you pass qty="0" and specify reduceOnly=true AND closeOnTrigger=true, you can close the position
                    reduceOnly: true,
                    closeOnTrigger: true
                    );
                if (!closeOrder.Success)
                {
                    Console.WriteLine($"Failure closing the position! Error: {closeOrder.Error.Message}");
                    return;
                }
                Console.WriteLine($"Success! Closing order ID: {closeOrder.Data.OrderId}");
            }
           
        }
    }
}

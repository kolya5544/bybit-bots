import sys
from pybit.unified_trading import HTTP

API_KEY = "your api key here"
API_SECRET = "your api secret here"
demo = True # Demo Account on Mainnet

# Initialize client to interact with Bybit REST API
client = HTTP(
    api_key=API_KEY,
    api_secret=API_SECRET,
    demo=demo
    )

# example getting "Market" -> "Get Instruments Info" for Spot
# https://bybit-exchange.github.io/docs/v5/market/instrument
instruments = client.get_instruments_info(category="spot")
spotList = instruments["result"]["list"]

print(f"Found a total of {len(spotList)} spot symbols.")
print()

bitcoin = next((x for x in spotList if x["symbol"] == "BTCUSDT"), None)
print(f"Information about BTCUSDT spot, Lot Size Filter:")
print(f"Precision for base coin: {bitcoin['lotSizeFilter']['basePrecision']} {bitcoin['baseCoin']}")
print(f"Precision for quote coin: {bitcoin['lotSizeFilter']['quotePrecision']} {bitcoin['quoteCoin']}")
print(f"Max Order Quantity: {bitcoin['lotSizeFilter']['maxOrderQty']} {bitcoin['baseCoin']}")
print(f"Max Order Value: {bitcoin['lotSizeFilter']['maxOrderAmt']} {bitcoin['quoteCoin']}")
print(f"Min Order Quantity: {bitcoin['lotSizeFilter']['minOrderQty']} {bitcoin['baseCoin']}")
print(f"Min Order Value: {bitcoin['lotSizeFilter']['minOrderAmt']} {bitcoin['quoteCoin']}")
print()

# example placing an order "Trade" -> "Place Order" for Linear (futures) Perpetual contract
# https://bybit-exchange.github.io/docs/v5/order/create-order
newOrder = client.place_order(
    category="linear",
    symbol="TONUSDT",
    side="Buy",
    orderType="Market",
    qty="100"
)
print(f"Successful order creation! Order ID: {newOrder['result']['orderId']}")
print()

# example get list of positions "Position" -> "Get Position Info"
# https://bybit-exchange.github.io/docs/v5/position
positions = client.get_positions(
    category="linear",
    settleCoin="USDT"
)
positionList = positions["result"]["list"]
print(f"Found a total of {len(positionList)} positions.")
print()

tonPosition = next((x for x in positionList if x["symbol"] == "TONUSDT"), None)
if (tonPosition == None):
    print("TONUSDT position was not found!")
    sys.exit(0)
print(f"TONUSDT position successfully found! Info:");
print(f"Leverage: {tonPosition['leverage']}x");
print(f"Quantity: {tonPosition['size']} TON");
print(f"Value: {tonPosition['positionValue']} USDT");
print(f"Entry price: {tonPosition['avgPrice']} USDT");
print(f"Mark price: {tonPosition['markPrice']} USDT");
print(f"Estimated liquidation price: {'-' if tonPosition['liqPrice'] == '' else tonPosition['liqPrice']} USDT");
print(f"Initial Margin: {tonPosition['positionIM']} USDT");
print(f"Maintenance Margin: {tonPosition['positionMM']} USDT");
print(f"Unrealized PnL: {tonPosition['unrealisedPnl']} USDT");
print(f"Realized PnL: {tonPosition['cumRealisedPnl']} USDT");
print();

# closing all positions
for pos in positionList:
    print(f"Closing {pos['symbol']} position...")
    closeOrder = client.place_order(
        category="linear",
        symbol="TONUSDT",
        side="Sell" if pos["side"] == "Buy" else "Buy",
        orderType="Market",
        qty="0", # if you pass qty="0" and specify reduceOnly=true AND closeOnTrigger=true, you can close the position
        reduceOnly=True,
        closeOnTrigger=True
    )    
    print(f"Success! Closing order ID: {closeOrder['result']['orderId']}")
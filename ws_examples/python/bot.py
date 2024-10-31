import sys
import time
from pybit.unified_trading import HTTP, WebSocket, WebSocketTrading

API_KEY = "your api key here"
API_SECRET = "your api secret here"
demo = False
testnet = True # Testnet account. KEEP IN MIND!! Demo doesn't support WebSocket trading.

BtcPrice = 0

# Initialize PUBLIC WebSocket client for WS API interactions with PUBLIC linear channel
client = WebSocket(
    api_key=API_KEY,
    api_secret=API_SECRET,
    demo=demo,
    testnet=testnet,
    channel_type="linear"
    )

# Initialize PRIVATE WS client for interacting with PRIVATE channel (getting order info, position info, etc)
private = WebSocket(
    api_key=API_KEY,
    api_secret=API_SECRET,
    demo=demo,
    testnet=testnet,
    channel_type="private"
    )

# Initialize TRADE WS client for placing orders over WS
trade = WebSocketTrading(
    api_key=API_KEY,
    api_secret=API_SECRET,
    demo=demo,
    testnet=testnet  
    )

#trade = WebSocket(
#    api_key=API_KEY,
#    api_secret=API_SECRET,
#    demo=demo,
#    testnet=testnet,
#    channel_type="trade"
#    )

def priceUpdateHandler(ev):
    global BtcPrice
    ltp = ev["data"]["lastPrice"]
    if (ltp is None):
        return
    BtcPrice = float(ltp)
    print(f"[PRICE] BTC current price = ${BtcPrice}")
    
def orderUpdateHandler(ev):
    updates = ev["data"]
    for upd in updates:
        oid = upd["orderId"]
        symbol = upd["symbol"]
        status = upd["orderStatus"]
        qty_filled = upd["cumExecQty"]
        qty = upd["qty"]
        remain = float(qty) - float(qty_filled)
        
        print(f"[ORD] Order update for Ord#{oid}@{symbol}. Status = {status}, filled quantity = {qty_filled}/{qty} (remaining: {remain})")

# Example subscribing to updates of BTCUSDT price (linear perp futures)
# https://bybit-exchange.github.io/docs/v5/websocket/public/ticker
print("[MAIN] Subscribing to BTCUSDT price updates...")
client.ticker_stream("BTCUSDT", priceUpdateHandler)

# Example subscribing to order updates for your account
# https://bybit-exchange.github.io/docs/v5/websocket/private/order
print("[MAIN] Subscribing to order updates...")
private.order_stream(orderUpdateHandler)

# Wait for BTC price updates to get proper price
while BtcPrice == 0:
    time.sleep(1)

# Example creating an order through WebSocket.
# WebSocker order placement is much faster than REST API due to less overhead
print("[MAIN] Creating a LONG limit order for BTCUSDT...")
order = trade.place_order(
    category="linear",
    symbol="BTCUSDT",
    side="Buy",
    orderType="Limit",
    qty="0.05",
    price=str(BtcPrice),
    callback=None
    )
print(f"[MAIN] Created new order! Waiting indefinitely...");

while True:
    time.sleep(10)
    
import sys
import time
from pybit.unified_trading import HTTP

API_KEY = "your api key here"
API_SECRET = "your api secret here"
demo = True # Demo Account on Mainnet

symbol = "MNTUSDT"
investmentAmount = 1000 # in USDT
intervalMinutes = 10080 # 60 minutes * 24 hours * 7 days = 10080 minutes

# Initialize client to interact with Bybit REST API
client = HTTP(
    api_key=API_KEY,
    api_secret=API_SECRET,
    demo=demo
    )

while True:
    # get all recent orders
    # https://bybit-exchange.github.io/docs/v5/order/order-list
    orders = client.get_order_history(
        category="spot",
        symbol=symbol
    )
    orderList = orders["result"]["list"]
    
    # find the latest order for the symbol specified
    latestOrder = next((x for x in orderList if x["orderStatus"] == "Filled" and x["side"] == "Buy"), None)
    
    # get the timestamp of when this order was created - or 0, if there was no such order
    orderTimestamp = int(0 if latestOrder is None else latestOrder["createdTime"]) // 1000
    
    # if more than {intervalMinutes} minutes passed, place a new spot order
    now = time.time()
    if (now - orderTimestamp > intervalMinutes * 60):
        # place a market order
        # https://bybit-exchange.github.io/docs/v5/order/create-order
        order = client.place_order(
            category="spot",
            symbol=symbol,
            side="Buy",
            orderType="Market",
            qty=investmentAmount,
            marketUnit="quoteCoin"
        )
        
        print(f"Success! Order ID {order['result']['orderId']}")
        
        time.sleep(30)


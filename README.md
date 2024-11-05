# Bybit Bots by kolya5544
An extremely wide and versatile collection of Bybit bots for beginners to use, take apart, and repurpose. For free!

*Donations are welcome ♥*

TON - `kushnarenko.ton` / UQByJu13PhBBRkgtdv3nPlzRwKOQ5y1YeQok3JGI-A_Kolya

Ethereum, MNT and other tokens - `kolya5544.eth` / 0x27c5De49e72257c426D92b22f830Ed0b2BF2dcc0

Bybit ID for direct transfers of any coin: `118027304`

Bybit, please hire me 😥

## Breakdown

There are different categories of bots presented in this repository.

| Directory | Description | Complexity level |
| --- | --- | --- |
| [`simplest_examples`](https://github.com/kolya5544/bybit-bots/tree/master/simplest_examples) | Contains very simple examples of getting authenticated, make simple requests, request information from the market and so on | 🔥 - Very simple |
| [`ws_examples`](https://github.com/kolya5544/bybit-bots/tree/master/ws_examples) | Simple examples on WebSocket: authentication, simple trading, etc. | 🔥🔥 - Simple |
| [`martingale_examples`](https://github.com/kolya5544/bybit-bots/tree/master/martingale_examples) | Examples of linear short & long futures Martingale bots | 🔥🔥🔥 - Intermediate |
| [`grid_examples`](https://github.com/kolya5544/bybit-bots/tree/master/grid_examples) | Examples of short & long futures Grid bots | 🔥🔥🔥 - Intermediate |
| [`indicator_examples`](https://github.com/kolya5544/bybit-bots/tree/master/indicator_examples) | Examples of bots that report information on different indicators | 🔥🔥🔥 - Intermediate |

Each category will have examples in some of available languages. You can check out the libraries below.

Complexity level refers to the difficulty for beginners to grasp the concepts and understand the code behind each interaction, as well as how complex code is in terms of algorithms used. It's a subjective metric.

## Technologies

I don't plan on including any raw REST API bots, so for every language there'll be a specific library used to make things easier for everyone.

| Language | Library | Installation | Environment |
| --- | --- | --- | --- |
| C# | [Bybit.Net by JKorf](https://github.com/JKorf/Bybit.Net) | dotnet add package Bybit.Net | .NET 8 |
| Python | [pybit by Dexter Dickinson](https://github.com/bybit-exchange/pybit) | pip install pybit | Python 3.10 |

Interface used is `Bybit V5 API`. **Unified Trading account is REQUIRED!! Standard Accounts are not supported.**

## Useful articles

Here is the list of articles you'll likely find useful if you're working on Trading Bots. The order of articles attempts to represent the order you'll want to read them in.

| Topic | URL |
| --- | --- |
| Creating an API key | [How to Create Your API Key? @ Bybit Help Center](https://www.bybit.com/en/help-center/article/How-to-create-your-API-key) |
| Introduction to API trading and documentation | [Introduction @ Bybit API Docs](https://bybit-exchange.github.io/docs/v5/intro) |
| Introduction to Martingale bots | [Introduction to Futures Martingale Bot @ Bybit Help Center](https://www.bybit.com/en/help-center/article/Introduction-to-Futures-Martingale-Bot) |
| Introduction to Grid bots | [Introduction to Futures Grid Bot on Bybit @ Bybit Help Center](https://www.bybit.com/en/help-center/article/Introduction-to-Futures-Grid-Bot-on-Bybit) |

## Professional work - [SELF-ADVERTISEMENT]

Need a professional to build a trading bot for you? Wide portfolio of different trading bots and TradingView strategies implemented into C# and Python applications. Cheap hosting for your bots, backtesting of your strategies. Professional help with code/consulting. You can always find me here:

TG: @kolya5544

Discord: @kolya5544

WEB: https://nk.ax/

Rate is discussable

## Contributions

I do not accept any big contributions or major code changes. Feel free to use GitHub Issues to report bugs and request features to be implemented.

## License

MIT
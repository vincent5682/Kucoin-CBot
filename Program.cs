using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CryptoExchange.Net.Sockets;
using Discord.WebSocket;
using Kucoin.Net;
using Kucoin.Net.Objects;
using Kucoin.Net.Objects.Spot;
using Kucoin.Net.Objects.Spot.Socket;

namespace CBot
{
   class Program
    {
        public static DiscordSocketClient _client;
        public static KucoinClient kucoinClient;
        public static KucoinSocketClient kucoinSocketClient;
        private static List<Position> _positions;
        private static IEnumerable<KucoinSymbol> _currencies;

        static async Task Main(string[] args)
        {
            _positions = new List<Position>();
            _client = new DiscordSocketClient();
            _client.LoggedIn += HandleLogin;
            _client.MessageReceived += HandleNewMessage;
            _client.LoginAsync(0, "Discord-WebUserToken").GetAwaiter().GetResult();
            _client.StartAsync().GetAwaiter().GetResult();

            kucoinClient = new KucoinClient(new KucoinClientOptions()
            {
                ApiCredentials = new KucoinApiCredentials("Public-Key", "Private-Key", "Password")
            });

            kucoinSocketClient = new KucoinSocketClient(new KucoinSocketClientOptions()
            {
                // Specify options for the client
            });
            var res = kucoinSocketClient.Spot.SubscribeToAllTickerUpdatesAsync(HandleTickerUpdate).GetAwaiter().GetResult();
            if (!res.Success)
            {
                Console.WriteLine("Failed to subscribe to all tickers");
            }
            else
            {
                Console.WriteLine("Successfully subscribed to all tickers");
            }
            var test = kucoinClient.Spot.GetSymbolsAsync().GetAwaiter().GetResult();
            if (test != null && test.Data != null)
            {
                _currencies = test.Data;
            }
            Console.ReadLine();
        }
        static async Task MakeOrderAsync(string transfer)
        {
            try
            {
                string coin = transfer;
                coin = coin.Replace("/", "-");
                var ticker = await kucoinClient.Spot.GetTickerAsync(coin);
                //volume = USDT
                var volume = 1.0M;

                var result = await kucoinClient.Spot.PlaceOrderAsync(coin, null, KucoinOrderSide.Buy, KucoinNewOrderType.Market, funds: volume);

                //Buy Order
                if (result == null || result.Data == null || result.Data.OrderId == null || result.Data.OrderId == "")
                {
                    Console.WriteLine($"Failed to palce order {result.Error}");
                    return;
                }
                var id = result.Data.OrderId;
                var orderResult = await kucoinClient.Spot.GetOrderAsync(id);
                if (orderResult == null || orderResult.Data == null)
                {
                    Console.WriteLine($"Failed to get order {result.Error}");
                    return;
                }
                var avgPrice = orderResult.Data.DealFunds / orderResult.Data.DealQuantity;
                _positions.Add(new Position
                {
                    Amount = orderResult.Data.DealQuantity,
                    Price = (decimal)avgPrice,
                    Symbol = coin,
                    Status = "open"
                });
                Console.WriteLine($"Placed an order on Kucoin {result.Data.OrderId} Price: {avgPrice}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        private static void SellOn(DataEvent<KucoinStreamTick> obj)
        {
            var openPosition = _positions.FirstOrDefault(x => x.Symbol == obj.Data.Symbol);
            var cur = _currencies.FirstOrDefault(x => x.Symbol == obj.Data.Symbol);
            openPosition.Amount = openPosition.Amount - (openPosition.Amount % cur.BaseIncrement);
            Console.WriteLine($"Closing position {obj.Data.Symbol}, stop reached");
            var result = kucoinClient.Spot.PlaceOrderAsync(obj.Data.Symbol, null, KucoinOrderSide.Sell, KucoinNewOrderType.Market, quantity: openPosition.Amount).GetAwaiter().GetResult();

            if (result == null || result.Data == null || result.Data.OrderId == null || result.Data.OrderId == "")
            {
                Console.WriteLine($"Failed to palce order {result.Error}");
                return;
            }
            var id = result.Data.OrderId;
            var orderResult = kucoinClient.Spot.GetOrderAsync(id).GetAwaiter().GetResult();
            if (orderResult == null || orderResult.Data == null)
            {
                Console.WriteLine($"Failed to get order {result.Error}");
                return;
            }
            var avgPrice = orderResult.Data.DealFunds / orderResult.Data.DealQuantity;
            var finalProfit = avgPrice / openPosition.Price;
            Console.WriteLine($"Profit: {finalProfit}");
            _positions.Remove(openPosition);
        }
        private static void HandleTickerUpdate(DataEvent<KucoinStreamTick> obj)
        {
            //Take Profit & Stop Loss parameter
            //1.01 = 1%
            //0.999 = 1%
            var limit = 1.01M;
            var stop = 0.999M;

            var openPosition = _positions.FirstOrDefault(x => x.Symbol == obj.Data.Symbol);
            if (openPosition != null)
            {
                var profit = obj.Data.LastTradePrice / openPosition.Price;
                Console.WriteLine($"Open position for {obj.Data.Symbol} has profit {profit}");
                //Sell on Stop - Lost
                if (profit < stop)
                {
                    SellOn(obj);
                }
                //Sell on limit - win
                if (profit > limit)
                {
                    SellOn(obj);
                }
            }
        }
        private static Task HandleLogin()
        {
            Console.WriteLine("logged in");
            return Task.FromResult(true);
        }

        private static Task HandleNewMessage(SocketMessage arg)
        {
            Regex rgx = new Regex("([A-Z0-9]{3,7}/USDT) is Pumping on Kucoin!");
            foreach (Match match in rgx.Matches(arg.Content))
            {
                string pair = match.Groups[1].Value;
                Console.WriteLine("Matched " + pair);
                string transfer = pair;
                _ = MakeOrderAsync(transfer);
            }
            return Task.FromResult(true);
        }

    }

}
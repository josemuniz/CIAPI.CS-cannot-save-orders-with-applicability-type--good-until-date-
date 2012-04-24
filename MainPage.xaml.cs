﻿using System;
using System.Threading;
using System.Windows;
using CIAPI.DTO;
using CIAPI.Streaming;
using StreamingClient;
using Client = CIAPI.Rpc.Client;

namespace PhoneApp5
{
    public partial class MainPage
    {
        private const string UerName = "xx794680";
        private const string Password = "password";

        // 400494226 EUR/USD
        // 400494234 GBP/USD
        private const int MarketId = 400494226;

        private static readonly Uri RpcUri = new Uri("http://ciapi.cityindex.com/tradingapi/");
        private static readonly Uri StreamingUri = new Uri("https://push.cityindex.com/");

        public Client RpcClient;
        public IStreamingClient StreamingClient;
        public IStreamingListener<PriceDTO> PriceListener;
        public ApiMarketInformationDTO Market;
        public AccountInformationResponseDTO Account;

        private bool _ordered;
        private bool _listening;

        public MainPage()
        {
            InitializeComponent();
            Unloaded += OnMainPageUnloaded;
            BuildClients();
        }

        private void BuildClients()
        {
            Dispatcher.BeginInvoke(() => listBox1.Items.Add("creating rpc client"));
            RpcClient = new Client(RpcUri, StreamingUri, "CI-WP7");
            RpcClient.BeginLogIn(UerName, Password, ar =>
            {
                try
                {
                    RpcClient.EndLogIn(ar);

                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Dispatcher.BeginInvoke(() => listBox1.Items.Add("creating listeners"));
                        StreamingClient = RpcClient.CreateStreamingClient();
                        PriceListener = StreamingClient.BuildPricesListener(MarketId);
                        PriceListener.MessageReceived += OnMessageReceived;

                        Dispatcher.BeginInvoke(() => listBox1.Items.Add("getting account info"));
                        RpcClient.AccountInformation.BeginGetClientAndTradingAccount(ar2 =>
                        {
                            Account = RpcClient.AccountInformation.EndGetClientAndTradingAccount(ar2);

                            Dispatcher.BeginInvoke(() => listBox1.Items.Add("getting market info"));
                            RpcClient.Market.BeginGetMarketInformation(MarketId.ToString(), ar3 =>
                            {
                                Market = RpcClient.Market.EndGetMarketInformation(ar3).MarketInformation;
                                Dispatcher.BeginInvoke(() => Button1.IsEnabled = true);
                            }, null);
                        }, null);
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(() => listBox1.Items.Add("exception caught: " + ex));
                }
            }, null);
        }

        private void OnButton1Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                Button1.IsEnabled = false;
                _listening = true;
                _ordered = false;
            });
        }

        private void OnMessageReceived(object sender, MessageEventArgs<PriceDTO> e)
        {
            if (!_listening || _ordered || Market == null) return;
            _ordered = true;

            var order = new NewStopLimitOrderRequestDTO
            {
                MarketId = e.Data.MarketId,
                AuditId = e.Data.AuditId,
                BidPrice = e.Data.Bid,
                OfferPrice = e.Data.Offer,
                Quantity = Market.WebMinSize.GetValueOrDefault() + 1,
                TradingAccountId = Account.TradingAccounts[0].TradingAccountId,
                Direction = "buy",
                Applicability = "GTD",
                ExpiryDateTimeUTC = DateTime.UtcNow + TimeSpan.FromDays(1)
            };

            Dispatcher.BeginInvoke(() => listBox1.Items.Add("price update arrived, making a new trade"));

            RpcClient.TradesAndOrders.BeginOrder(order, ar =>
            {
                try
                {
                    var result = RpcClient.TradesAndOrders.EndTrade(ar);

                    Dispatcher.BeginInvoke(() =>
                    {
                        listBox1.Items.Add(String.Format("trading complete, status = {0}, status reason = {1}", result.Status, result.StatusReason));
                        if (result.OrderId > 0)
                        {
                            listBox1.Items.Add(String.Format("created order {0}", result.OrderId));
                        }
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(() => listBox1.Items.Add("trading failed!"));
                    Dispatcher.BeginInvoke(() => listBox1.Items.Add("exception caught: " + ex));
                }
                finally
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        Button1.IsEnabled = true;
                        _listening = false;
                    });
                }
            }, null);
        }

        private void OnMainPageUnloaded(object sender, RoutedEventArgs e)
        {
            if (StreamingClient == null) return;
            if (PriceListener != null) StreamingClient.TearDownListener(PriceListener);
        }
    }
}
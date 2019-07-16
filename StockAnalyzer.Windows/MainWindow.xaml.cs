﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using Newtonsoft.Json;
using StockAnalyzer.Core.Domain;
using StockAnalyzer.Windows.Services;

namespace StockAnalyzer.Windows
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private CancellationTokenSource _cancellationTokenSource = null;

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            #region Before loading stock data

            var watch = new Stopwatch();
            watch.Start();
            StockProgress.Visibility = Visibility.Visible;
            StockProgress.IsIndeterminate = true;

            #endregion

            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource = null;
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationTokenSource.Token.Register(() => { Notes.Text = "Cancellation requested"; });

            try
            {
                var tickers = Ticker.Text.Split(',', ' ');

                var service = new StockService();

                // List<T> is not thread safe consider using ConcurrentBag<T> instead
                var stocks = new ConcurrentBag<StockPrice>();

                var tickerLoadingTasks = new List<Task<IEnumerable<StockPrice>>>();

                foreach (var ticker in tickers)
                {
                    var loadTask = service.GetStockPricesFor(ticker, _cancellationTokenSource.Token)
                        .ContinueWith(t =>
                        {
                            foreach (var stock in t.Result.Take(5))
                            {
                                stocks.Add(stock);
                            }

                            Dispatcher.Invoke(() => { Stocks.ItemsSource = stocks.ToArray(); });

                            return t.Result;
                        });

                    tickerLoadingTasks.Add(loadTask);
                }

                var allStocksLoadingTask = Task.WhenAll(tickerLoadingTasks);

                #region Handling Timeout

                var timeoutTask = Task.Delay(2000);
                var completedTask = await Task.WhenAny(timeoutTask, allStocksLoadingTask);

                if (completedTask == timeoutTask)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource = null;

                    throw new Exception("Timeout!");
                }

                #endregion

                await allStocksLoadingTask;
            }
            catch (Exception exception)
            {
                Notes.Text = exception.Message;
            }

            #region After stock data is loaded

            StocksStatus.Text = $"Loaded stocks for {Ticker.Text} in {watch.ElapsedMilliseconds}ms";
            StockProgress.Visibility = Visibility.Hidden;

            #endregion

            //var result = await GetStockFor(Ticker.Text);

            //Notes.Text += $"Stocks loaded!{Environment.NewLine}";
        }

        public async Task<IEnumerable<StockPrice>> GetStockFor(string ticker)
        {
            var service = new StockService();
            var stocks = await service.GetStockPricesFor(ticker, CancellationToken.None)
                .ConfigureAwait(false);

            return stocks.Take(5);
        }

        private Task<List<string>> SearchForStocks(CancellationToken cancellationToken)
        {
            var loadLinesTask = Task.Run(async () =>
            {
                var lines = new List<string>();

                using (var stream = new StreamReader(File.OpenRead(@"D:\dev\StockAnalyzer\StockData\StockPrices_Small.csv")))
                {
                    string line;

                    while ((line = await stream.ReadLineAsync()) != null)
                    {
                        lines.Add(line);
                    }
                }

                return lines;
            }, cancellationToken);

            return loadLinesTask;
        }

        public async Task GetStocks()
        {
            using (var client = new HttpClient())
            {
                var response = await client.GetAsync($"http://localhost:61363/api/stocks/{Ticker.Text}");

                try
                {
                    response.EnsureSuccessStatusCode();
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonConvert.DeserializeObject<IEnumerable<StockPrice>>(content);

                    Stocks.ItemsSource = data;
                }
                catch (Exception ex)
                {
                    Notes.Text += ex.Message;
                }
            }
        }

        private void Hyperlink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));

            e.Handled = true;
        }

        private void Close_OnClick(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

    }
}

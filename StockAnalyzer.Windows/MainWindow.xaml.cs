using System;
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

        static object syncRoot = new object();

        private CancellationTokenSource _cancellationTokenSource = null;

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            #region Before loading stock data

            var watch = new Stopwatch();
            watch.Start();
            StockProgress.Visibility = Visibility.Visible;
            StockProgress.IsIndeterminate = true;

            #endregion

            #region Cancellation

            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource = null;
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationTokenSource.Token.Register(() => { Notes.Text = "Cancellation requested"; });

            #endregion

            try
            {
                await WorkInNotepad();

                Notes.Text += "Notepad closed, continuation!";
            }
            catch (Exception exception)
            {
                Notes.Text = exception.Message;
            }
            finally
            {
                _cancellationTokenSource = null;
            }

            #region After stock data is loaded

            StocksStatus.Text = $"Loaded stocks for {Ticker.Text} in {watch.ElapsedMilliseconds}ms";
            StockProgress.Visibility = Visibility.Hidden;

            #endregion
        }

        private async Task LoadStocks(IProgress<IEnumerable<StockPrice>> progress = null)
        {
            var tickers = Ticker.Text.Split(',', ' ');
            var service = new StockService();
            var tickerLoadingTasks = new List<Task<IEnumerable<StockPrice>>>();

            foreach (var ticker in tickers)
            {
                var loadTask = service.GetStockPricesFor(ticker, _cancellationTokenSource.Token);

                loadTask = loadTask.ContinueWith(stockTask =>
                {
                    progress?.Report(stockTask.Result);
                    return stockTask.Result;
                });

                tickerLoadingTasks.Add(loadTask);
            }
            var timeoutTask = Task.Delay(2000);

            var allStocksLoadingTask = Task.WhenAll(tickerLoadingTasks);

            var completedTask = await Task.WhenAny(timeoutTask, allStocksLoadingTask);

            Stocks.ItemsSource = allStocksLoadingTask.Result.SelectMany(stocks => stocks);
        }

        private decimal Compute(StockPrice stock)
        {
            Thread.Yield();

            decimal x = 0;
            for (var a = 0; a < 10; a++)
            {
                for (var b = 0; b < 20; b++)
                {
                    x += a + stock.Change;
                }
            }

            return x;
        }

        Random random = new Random();

        private decimal CalculateExpensiveComputation(IEnumerable<StockPrice> stocks)
        {
            Thread.Yield();

            var computedValue = 0m;

            foreach (var stock in stocks)
            {
                for (int i = 0; i < stocks.Count() - 2; i++)
                {
                    for (int a = 0; a < random.Next(50, 60); a++)
                    {
                        computedValue += stocks.ElementAt(i).Change + stocks.ElementAt(i + 1).Change;
                    }
                }
            }

            return computedValue;
        }

        public Task<IEnumerable<StockPrice>> GetStocksFor(string ticker)
        {
            var source = new TaskCompletionSource<IEnumerable<StockPrice>>();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var prices = new List<StockPrice>();
                    var lines = File.ReadAllLines(@"D:\dev\StockAnalyzer\StockData\StockPrices_Small.csv");

                    foreach (var line in lines.Skip(1))
                    {
                        var segments = line.Split(',');

                        for (var i = 0; i < segments.Length; i++) segments[i] = segments[i].Trim('\'', '"');
                        var price = new StockPrice
                        {
                            Ticker = segments[0],
                            TradeDate = DateTime.ParseExact(segments[1], "M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture),
                            Volume = Convert.ToInt32(segments[6]),
                            Change = Convert.ToDecimal(segments[7]),
                            ChangePercent = Convert.ToDecimal(segments[8]),
                        };
                        prices.Add(price);
                    }

                    source.SetResult(prices.Where(price => price.Ticker == ticker));
                }
                catch (Exception ex)
                {
                    source.SetException(ex);
                }
            });

            return source.Task;
        }

        public Task WorkInNotepad()
        {
            var source = new TaskCompletionSource<object>();

            var process = new Process()
            {
                EnableRaisingEvents = true,
                StartInfo = new ProcessStartInfo("Notepad.exe")
                {
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            process.Exited += (sender, e) => { source.SetResult(null); };
            process.Start();

            return source.Task;
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

    class StockCalculation
    {
        public string Ticker { get; set; }
        public decimal Result { get; set; }
    }
}

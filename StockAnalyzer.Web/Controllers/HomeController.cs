using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Mvc;
using StockAnalyzer.Core;
using StockAnalyzer.Core.Domain;

namespace StockAnalyzer.Web.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            ViewBag.Title = "Home Page";

            return View();
        }

        [Route("Stock/{ticker}")]
        public async Task<ActionResult> Stock(string ticker)
        {
            var data = await GetStocks();

            return View(data[ticker]);
        }

        public async Task<Dictionary<string, IEnumerable<StockPrice>>> GetStocks()
        {
            var store = new DataStore();

            // In ASP.NET 4.5 setting ConfigureAwait(false) means using the current tasks thread to execute the continuation
            // This only applies to traditional ASP.NET, not ASP.NET Core
            var data = await store.LoadStocks().ConfigureAwait(false);

            return data;
        }
    }
}

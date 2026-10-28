using Microsoft.AspNetCore.Mvc;
using PD.Mortgages.Web.Models;
using System.Diagnostics;

namespace PD.Mortgages.Web.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}

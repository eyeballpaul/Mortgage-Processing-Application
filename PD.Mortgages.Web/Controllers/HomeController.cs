using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Web.Models;
using PD.Mortgages.Web.Services;

namespace PD.Mortgages.Web.Controllers;

public sealed class HomeController(IMortgageApplicationService applicationService, IWizardSession wizardSession) : Controller
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (TempData["SubmissionRecovered"] is true)
            return RedirectToAction("Submitted", "Wizard");
        if (wizardSession.CurrentApplicationId is Guid id)
        {
            try
            {
                if (wizardSession.PendingSubmissionKey is Guid key &&
                    await applicationService.GetSubmissionReceiptAsync(id, key, cancellationToken) is not null)
                {
                    wizardSession.CompleteSubmission(id, key);
                    return RedirectToAction("Submitted", "Wizard");
                }
                await applicationService.GetAsync(id, cancellationToken);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or UnauthorizedAccessException)
            {
                wizardSession.Clear();
            }
            catch (HttpRequestException)
            {
                TempData["Notice"] = "We couldn’t check your saved application just now. Please try again later.";
            }
        }

        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PhotoApp.Controllers
{
    [Authorize] // nebo [AllowAnonymous] pro testování
    public class AdminController : Controller
    {
        // GET: /Admin/Database
        [HttpGet]
        public IActionResult Database()
        {
            // Vrátit view uložené v Views/Admin/Database/Database.cshtml (absolutní cesta zajišťuje, že se vždy najde)
            return View("~/Views/Admin/Database.cshtml");
        }
    }
}
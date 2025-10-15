using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoApp.Data;
using PhotoApp.Models;

namespace PhotoApp.Controllers
{
    public class PhotosController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public PhotosController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // GET: /Photos
        public async Task<IActionResult> Index()
        {
            var photos = await _context.Photos.OrderByDescending(p => p.CreatedAt).ToListAsync();
            return View(photos);
        }

        // GET: /Photos/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Photos/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PhotoRecord record, IFormFile? imageFile)
        {
            if (!ModelState.IsValid)
                return View(record);

            if (imageFile != null && imageFile.Length > 0)
            {
                var uploads = Path.Combine(_env.WebRootPath, "uploads");
                if (!Directory.Exists(uploads))
                    Directory.CreateDirectory(uploads);

                var fileName = Path.GetFileNameWithoutExtension(imageFile.FileName)
                               + "_" + Guid.NewGuid().ToString("N")
                               + Path.GetExtension(imageFile.FileName);
                var path = Path.Combine(uploads, fileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }

                record.ImagePath = "/uploads/" + fileName;
            }

            record.CreatedAt = DateTime.UtcNow;
            _context.Add(record);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: /Photos/Details/{id}
        public async Task<IActionResult> Details(int id)
        {
            var photo = await _context.Photos.FindAsync(id);
            if (photo == null)
                return NotFound();

            return View(photo);
        }
    }
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoApp.Data;
using PhotoApp.Models;

public class PhotosController : Controller
{
    private readonly AppDbContext _context;

    public PhotosController(AppDbContext context)
    {
        _context = context;
    }

    // GET: Photos
    public async Task<IActionResult> Index(string search)
    {
        var photos = from p in _context.Photos
                     select p;

        if (!string.IsNullOrEmpty(search))
        {
            photos = photos.Where(p => p.Name.Contains(search) || p.Code.Contains(search));
        }

        photos = photos.OrderByDescending(p => p.UpdatedAt);

        return View(await photos.ToListAsync());
    }

    // GET: Photos/Create
    public IActionResult Create()
    {
        return View();
    }

    // POST: Photos/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Name,Code,Type,Supplier,Notes,UpdatedAt,PhotoPath")] PhotoRecord photo, IFormFile PhotoFile)
    {
        if (ModelState.IsValid)
        {
            photo.UpdatedAt = DateTime.Now;

            // Uložení fotky do wwwroot/uploads
            if (PhotoFile != null && PhotoFile.Length > 0)
            {
                var uploads = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
                if (!Directory.Exists(uploads))
                    Directory.CreateDirectory(uploads);

                var fileName = Guid.NewGuid() + Path.GetExtension(PhotoFile.FileName);
                var path = Path.Combine(uploads, fileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await PhotoFile.CopyToAsync(stream);
                }

                photo.PhotoPath = "/uploads/" + fileName;
            }

            _context.Add(photo);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(photo);
    }

    // GET: Photos/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
            return NotFound();

        var photo = await _context.Photos.FindAsync(id);
        if (photo == null)
            return NotFound();

        return View(photo);
    }

    // POST: Photos/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Code,Type,Supplier,Notes,UpdatedAt,PhotoPath")] PhotoRecord photo, IFormFile? PhotoFile)
    {
        if (id != photo.Id)
            return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                photo.UpdatedAt = DateTime.Now;

                // Uložení nové fotky, pokud je vybrána
                if (PhotoFile != null && PhotoFile.Length > 0)
                {
                    var uploads = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
                    if (!Directory.Exists(uploads))
                        Directory.CreateDirectory(uploads);

                    var fileName = Guid.NewGuid() + Path.GetExtension(PhotoFile.FileName);
                    var path = Path.Combine(uploads, fileName);

                    using (var stream = new FileStream(path, FileMode.Create))
                    {
                        await PhotoFile.CopyToAsync(stream);
                    }

                    photo.PhotoPath = "/uploads/" + fileName;
                }

                _context.Update(photo);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Photos.Any(e => e.Id == photo.Id))
                    return NotFound();
                else
                    throw;
            }
            return RedirectToAction(nameof(Index));
        }
        return View(photo);
    }

    // GET: Photos/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
            return NotFound();

        var photo = await _context.Photos.FirstOrDefaultAsync(m => m.Id == id);
        if (photo == null)
            return NotFound();

        return View(photo);
    }

    // POST: Photos/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var photo = await _context.Photos.FindAsync(id);
        if (photo != null)
        {
            _context.Photos.Remove(photo);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
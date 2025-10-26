using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PhotoApp.Data;
using System.IO.Compression;

namespace PhotoApp.Controllers
{
    [ApiController]
    [Route("api/admin/db")]
    [Authorize]
    public class DatabaseBackupController : ControllerBase
    {
        private readonly string _dbPath;           // absolute path to the sqlite file
        private readonly string _dbConnString;     // connection string used to open the DB
        private readonly string _backupFolder;
        private readonly ILogger<DatabaseBackupController> _logger;
        private readonly IServiceProvider _services;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IWebHostEnvironment _env;
        private static readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);

        private const long MaxUploadBytes = 200L * 1024 * 1024;

        public DatabaseBackupController(IConfiguration config,
                                        ILogger<DatabaseBackupController> logger,
                                        IServiceProvider services,
                                        IHostApplicationLifetime appLifetime,
                                        IWebHostEnvironment env)
        {
            _logger = logger;
            _services = services;
            _appLifetime = appLifetime;
            _env = env ?? throw new ArgumentNullException(nameof(env));

            // Read possible DB config in multiple forms:
            // - absolute path
            // - relative path
            // - full connection string "Data Source=..."
            var cfg = config["SqliteDbPath"]
                      ?? config.GetConnectionString("DefaultConnection")
                      ?? config["ConnectionStrings:Sqlite"];

            if (string.IsNullOrWhiteSpace(cfg))
            {
                // default: content root + photoapp.db
                _dbPath = Path.Combine(_env.ContentRootPath ?? AppContext.BaseDirectory, "photoapp.db");
                _dbConnString = $"Data Source={_dbPath}";
            }
            else if (cfg.IndexOf("data source=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // cfg looks like connection string; parse Data Source out to an absolute path
                _dbConnString = cfg;
                _dbPath = ExtractDataSourceFromConnectionString(cfg, _env.ContentRootPath ?? AppContext.BaseDirectory);
            }
            else
            {
                // cfg is a path (absolute or relative to content root)
                var p = cfg;
                if (!Path.IsPathRooted(p))
                    p = Path.GetFullPath(Path.Combine(_env.ContentRootPath ?? AppContext.BaseDirectory, p));
                _dbPath = p;
                _dbConnString = $"Data Source={_dbPath}";
            }

            _backupFolder = Path.Combine(_env.ContentRootPath ?? AppContext.BaseDirectory, "db-backups");
            try { Directory.CreateDirectory(_backupFolder); } catch { /* ignore */ }

            _logger.LogInformation("DatabaseBackupController initialized. dbPath={DbPath}, connStringPreview={ConnPreview}", _dbPath, _dbConnString?.Substring(0, Math.Min(80, _dbConnString.Length)));
        }

        // GET: api/admin/db/backup
        // returns a zip containing database.db and uploads/*
        [HttpGet("backup")]
        public async Task<IActionResult> GetBackup()
        {
            await _opLock.WaitAsync();
            try
            {
                if (!System.IO.File.Exists(_dbPath))
                {
                    _logger.LogWarning("DB file not found at {DbPath}", _dbPath);
                    return NotFound("DB file not found.");
                }

                // 1) create a consistent tmp copy of DB using BackupDatabase
                var tmpDb = Path.Combine(Path.GetTempPath(), $"photoapp_backup_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}.db");
                try
                {
                    using (var source = new SqliteConnection(_dbConnString))
                    using (var dest = new SqliteConnection($"Data Source={tmpDb}"))
                    {
                        await source.OpenAsync();
                        await dest.OpenAsync();
                        source.BackupDatabase(dest);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create tmp DB via BackupDatabase, attempting direct copy fallback.");
                    try
                    {
                        System.IO.File.Copy(_dbPath, tmpDb, overwrite: true);
                    }
                    catch (Exception copyEx)
                    {
                        _logger.LogError(copyEx, "Fallback copy also failed.");
                        return StatusCode(500, "Failed to prepare DB for backup: " + copyEx.Message);
                    }
                }

                // 2) create zip in memory (stream) containing the tmp db and uploads folder
                var zipName = $"backup_{DateTime.UtcNow:yyyyMMddHHmmss}.zip";
                var uploadsFolder = Path.Combine(_env.WebRootPath ?? "", "uploads");

                using (var ms = new MemoryStream())
                {
                    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    {
                        // add database.db entry
                        var dbEntry = zip.CreateEntry("database.db", CompressionLevel.Optimal);
                        using (var zs = dbEntry.Open())
                        using (var fs = System.IO.File.Open(tmpDb, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            await fs.CopyToAsync(zs);
                        }

                        // add uploads recursively
                        if (Directory.Exists(uploadsFolder))
                        {
                            var files = Directory.GetFiles(uploadsFolder, "*", SearchOption.AllDirectories);
                            foreach (var file in files)
                            {
                                var relPath = Path.GetRelativePath(uploadsFolder, file).Replace('\\', '/');
                                var entryPath = Path.Combine("uploads", relPath).Replace('\\', '/');
                                var entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);

                                // try to open with read sharing (retry lightly if needed)
                                using (var zs = entry.Open())
                                using (var fs = System.IO.File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                {
                                    await fs.CopyToAsync(zs);
                                }
                            }
                        }
                    }

                    ms.Position = 0;
                    // schedule tmpDb deletion after response completes
                    Response.OnCompleted(() =>
                    {
                        try { if (System.IO.File.Exists(tmpDb)) System.IO.File.Delete(tmpDb); } catch { }
                        return Task.CompletedTask;
                    });

                    return File(ms.ToArray(), "application/zip", zipName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating DB+uploads backup");
                return StatusCode(500, "Failed to create backup: " + ex.Message);
            }
            finally
            {
                _opLock.Release();
            }
        }

        [HttpGet("ui")]
        [Authorize]
        public IActionResult Ui() => Redirect("/Admin/Database");

        // POST: api/admin/db/restore
        // accepts a ZIP that contains database.db and uploads/* and restores both
        [HttpPost("restore")]
        [RequestSizeLimit(MaxUploadBytes)]
        public async Task<IActionResult> RestoreBackup([FromForm] Microsoft.AspNetCore.Http.IFormFile backupZip)
        {
            if (backupZip == null || backupZip.Length == 0)
                return BadRequest("No file uploaded.");

            if (backupZip.Length > MaxUploadBytes)
                return BadRequest("Uploaded file too large.");

            await _opLock.WaitAsync();
            var tmpFolder = Path.Combine(Path.GetTempPath(), $"photoapp_import_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpFolder);
            try
            {
                // 1) save uploaded zip to tmp
                var zipPath = Path.Combine(tmpFolder, "backup.zip");
                await using (var fs = System.IO.File.Create(zipPath))
                {
                    await backupZip.CopyToAsync(fs);
                }

                // 2) extract
                try
                {
                    ZipFile.ExtractToDirectory(zipPath, tmpFolder, true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to extract uploaded ZIP");
                    return BadRequest("Uploaded file is not a valid ZIP or extraction failed.");
                }

                // 3) find database.db inside extracted folder
                var extractedDb = Path.Combine(tmpFolder, "database.db");
                if (!System.IO.File.Exists(extractedDb))
                {
                    return BadRequest("ZIP does not contain database.db at the root.");
                }

                // 4) integrity check of extracted DB
                try
                {
                    using var checkConn = new SqliteConnection($"Data Source={extractedDb}");
                    await checkConn.OpenAsync();
                    using var checkCmd = checkConn.CreateCommand();
                    checkCmd.CommandText = "PRAGMA integrity_check;";
                    var res = (string)await checkCmd.ExecuteScalarAsync();
                    if (!string.Equals(res, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest($"Uploaded DB failed integrity_check: {res}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Integrity check failed or could not open extracted DB.");
                    return BadRequest("Uploaded DB failed integrity check or could not be opened.");
                }

                // 5) Save fallback of running DB (best-effort)
                var fallback = Path.Combine(_backupFolder, $"pre-restore-{DateTime.UtcNow:yyyyMMdd_HHmmss}.sqlite");
                if (System.IO.File.Exists(_dbPath))
                {
                    try
                    {
                        using var source = new SqliteConnection(_dbConnString);
                        using var destFallback = new SqliteConnection($"Data Source={fallback}");
                        await source.OpenAsync();
                        await destFallback.OpenAsync();
                        source.BackupDatabase(destFallback);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create fallback backup before restore (continuing).");
                    }
                }

                // 6) Try to close DI DbContexts (best-effort) to minimize locks
                try
                {
                    using var scope = _services?.CreateScope();
                    if (scope != null)
                    {
                        var appDb = scope.ServiceProvider.GetService(typeof(AppDbContext)) as AppDbContext;
                        if (appDb != null)
                        {
                            try { await appDb.Database.CloseConnectionAsync(); } catch { /* ignore */ }
                            try { appDb.ChangeTracker.Clear(); } catch { /* ignore */ }
                            try { appDb.Dispose(); } catch { /* ignore */ }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve/close AppDbContext before restore (continuing).");
                }

                // 7) Import database content into running DB via Backup API
                try
                {
                    using var src = new SqliteConnection($"Data Source={extractedDb}");
                    using var dest = new SqliteConnection(_dbConnString);
                    await src.OpenAsync();
                    await dest.OpenAsync();
                    src.BackupDatabase(dest);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to apply uploaded DB into running DB.");
                    return StatusCode(500, "Failed to apply uploaded DB into running DB: " + ex.Message);
                }

                // 8) WAL checkpoint if needed
                try
                {
                    using var conn = new SqliteConnection(_dbConnString);
                    await conn.OpenAsync();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "wal_checkpoint failed after restore (continuing).");
                }

                // 9) Restore uploads folder (if present in zip)
                var extractedUploads = Path.Combine(tmpFolder, "uploads");
                var targetUploads = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath ?? Directory.GetCurrentDirectory(), "wwwroot"), "uploads");
                try
                {
                    if (Directory.Exists(extractedUploads))
                    {
                        // remove existing uploads folder (best-effort) and copy new
                        if (Directory.Exists(targetUploads))
                        {
                            Directory.Delete(targetUploads, true);
                        }
                        Directory.CreateDirectory(targetUploads);
                        CopyDirectory(extractedUploads, targetUploads);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to restore uploads fully. Some files may be missing.");
                    // continue - DB was restored
                }

                _logger.LogInformation("Restore applied into running DB and uploads restored (if present). Fallback saved to {Fallback}", fallback);

                // 10) cleanup and redirect to Photos/Index
                return RedirectToAction("Index", "Photos");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore failed");
                return StatusCode(500, $"Restore failed: {ex.Message}");
            }
            finally
            {
                try { if (Directory.Exists(tmpFolder)) Directory.Delete(tmpFolder, true); } catch { }
                _opLock.Release();
            }
        }

        // helper to copy directory recursively
        private void CopyDirectory(string sourceDir, string targetDir)
        {
            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                var targetSub = dir.Replace(sourceDir, targetDir);
                Directory.CreateDirectory(targetSub);
            }

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var dest = file.Replace(sourceDir, targetDir);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);
                System.IO.File.Copy(file, dest, overwrite: true);
            }
        }

        private static string ExtractDataSourceFromConnectionString(string connString, string contentRoot)
        {
            if (string.IsNullOrWhiteSpace(connString)) return null;
            var lower = connString.ToLowerInvariant();
            var key = "data source=";
            var idx = lower.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) { key = "datasource="; idx = lower.IndexOf(key, StringComparison.OrdinalIgnoreCase); }
            if (idx < 0) return null;
            var start = idx + key.Length;
            var rest = connString.Substring(start).Trim();
            var endIdx = rest.IndexOf(';');
            var path = endIdx >= 0 ? rest.Substring(0, endIdx) : rest;
            path = path.Trim().Trim('"').Trim('\'');
            if (!Path.IsPathRooted(path))
            {
                path = Path.GetFullPath(Path.Combine(contentRoot ?? Directory.GetCurrentDirectory(), path));
            }
            return path;
        }

        // kept for compatibility if some callers still use UploadFileModel
        public class UploadFileModel
        {
            public Microsoft.AspNetCore.Http.IFormFile File { get; set; }
        }
    }
}
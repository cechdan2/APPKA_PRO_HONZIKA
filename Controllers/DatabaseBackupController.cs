using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoApp.Data;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoApp.Controllers
{
    [ApiController]
    [Route("api/admin/db")]
    [Authorize]
    public class DatabaseBackupController : ControllerBase
    {
        private readonly string _dbPath;
        private readonly string _backupFolder;
        private readonly ILogger<DatabaseBackupController> _logger;
        private readonly IServiceProvider _services;
        private readonly IHostApplicationLifetime _appLifetime;
        private static readonly SemaphoreSlim _opLock = new SemaphoreSlim(1, 1);

        private const long MaxUploadBytes = 200L * 1024 * 1024;

        public DatabaseBackupController(IConfiguration config, ILogger<DatabaseBackupController> logger, IServiceProvider services, IHostApplicationLifetime appLifetime)
        {
            _logger = logger;
            _services = services;
            _appLifetime = appLifetime;

            var cfgPath = config["SqliteDbPath"] ?? config["ConnectionStrings:Sqlite"];
            if (string.IsNullOrWhiteSpace(cfgPath))
            {
                _dbPath = Path.Combine(AppContext.BaseDirectory, "photoapp.db");
            }
            else
            {
                _dbPath = cfgPath;
                if (!Path.IsPathRooted(_dbPath))
                    _dbPath = Path.Combine(AppContext.BaseDirectory, _dbPath);
            }

            _backupFolder = Path.Combine(AppContext.BaseDirectory, "db-backups");
            Directory.CreateDirectory(_backupFolder);
        }

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

                var tmpFolder = Path.GetTempPath();
                var baseName = $"photoapp-backup-{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
                var tmpPath = Path.Combine(tmpFolder, baseName + ".sqlite");
                var copyPath = Path.Combine(tmpFolder, baseName + "-copy.sqlite");

                const int maxBackupAttempts = 6;
                var backupSucceeded = false;
                Exception lastException = null;

                for (int attempt = 1; attempt <= maxBackupAttempts; attempt++)
                {
                    try
                    {
                        if (System.IO.File.Exists(tmpPath))
                            System.IO.File.Delete(tmpPath);

                        using (var source = new SqliteConnection($"Data Source={_dbPath}"))
                        using (var dest = new SqliteConnection($"Data Source={tmpPath}"))
                        {
                            await source.OpenAsync();
                            await dest.OpenAsync();
                            source.BackupDatabase(dest);
                        }

                        backupSucceeded = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        _logger.LogWarning(ex, "Attempt {Attempt} to create backup failed, retrying...", attempt);
                        await Task.Delay(500 * attempt);
                    }
                }

                if (!backupSucceeded)
                {
                    var msg = lastException != null ? lastException.Message : "Unknown error creating backup.";
                    _logger.LogError("Backup failed after retries: {Message}", msg);
                    return StatusCode(500, "Failed to create backup: " + msg);
                }

                // Copy to a safe file to avoid locks while streaming
                const int maxCopyAttempts = 6;
                for (int i = 1; i <= maxCopyAttempts; i++)
                {
                    try
                    {
                        if (System.IO.File.Exists(copyPath))
                            System.IO.File.Delete(copyPath);

                        System.IO.File.Copy(tmpPath, copyPath, overwrite: true);
                        break;
                    }
                    catch (IOException ioEx) when (i < maxCopyAttempts)
                    {
                        _logger.LogWarning(ioEx, "Attempt {Attempt} to copy backup file failed, retrying...", i);
                        await Task.Delay(300 * i);
                    }
                }

                Response.OnCompleted(() =>
                {
                    _ = Task.Run(async () =>
                    {
                        string[] paths = new[] { tmpPath, copyPath };
                        foreach (var p in paths)
                        {
                            const int maxDeleteAttempts = 6;
                            for (int i = 1; i <= maxDeleteAttempts; i++)
                            {
                                try
                                {
                                    if (System.IO.File.Exists(p))
                                        System.IO.File.Delete(p);
                                    break;
                                }
                                catch
                                {
                                    await Task.Delay(500 * i);
                                }
                            }
                        }
                    });
                    return Task.CompletedTask;
                });

                return PhysicalFile(copyPath, "application/x-sqlite3", Path.GetFileName(copyPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating DB backup");
                return StatusCode(500, "Failed to create backup: " + ex.Message);
            }
            finally
            {
                _opLock.Release();
            }
        }

        [HttpGet("ui")]
        [Authorize]
        public IActionResult Ui()
        {
            return Redirect("/Admin/Database");
        }

        // Robust restore that replaces the active DB file atomically
        [HttpPost("restore")]
        [RequestSizeLimit(MaxUploadBytes)]
        public async Task<IActionResult> RestoreBackup([FromForm] UploadFileModel model)
        {
            if (model?.File == null || model.File.Length == 0)
                return BadRequest("No file uploaded.");

            if (model.File.Length > MaxUploadBytes)
                return BadRequest("Uploaded file too large.");

            await _opLock.WaitAsync();
            try
            {
                // 1) Save upload to temp
                var uploadedFile = Path.Combine(_backupFolder, $"upload-{Guid.NewGuid()}.sqlite");
                await using (var fs = System.IO.File.Create(uploadedFile))
                {
                    await model.File.CopyToAsync(fs);
                }

                // 2) Integrity check
                try
                {
                    using (var srcConn = new SqliteConnection($"Data Source={uploadedFile}"))
                    {
                        await srcConn.OpenAsync();
                        using var cmd = srcConn.CreateCommand();
                        cmd.CommandText = "PRAGMA integrity_check;";
                        var result = (string)await cmd.ExecuteScalarAsync();
                        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                        {
                            System.IO.File.Delete(uploadedFile);
                            _logger.LogWarning("Uploaded DB failed integrity_check: {Result}", result);
                            return BadRequest($"Uploaded DB failed integrity_check: {result}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.Delete(uploadedFile);
                    _logger.LogWarning(ex, "Integrity check failed for uploaded file");
                    return BadRequest("Uploaded DB failed integrity check or could not be opened.");
                }

                // 3) Save fallback backup of current DB
                var fallback = Path.Combine(_backupFolder, $"pre-restore-{DateTime.UtcNow:yyyyMMdd_HHmmss}.sqlite");
                if (System.IO.File.Exists(_dbPath))
                {
                    try
                    {
                        using (var source = new SqliteConnection($"Data Source={_dbPath}"))
                        using (var dest = new SqliteConnection($"Data Source={fallback}"))
                        {
                            await source.OpenAsync();
                            await dest.OpenAsync();
                            source.BackupDatabase(dest);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create fallback backup before restore (continuing).");
                    }
                }

                // 4) Create a temp target DB in the same folder as the real DB
                var dbFolder = Path.GetDirectoryName(_dbPath) ?? AppContext.BaseDirectory;
                var tempTarget = Path.Combine(dbFolder, $"dest-{Guid.NewGuid()}.sqlite");

                // Use SQLite backup API to write uploaded DB into tempTarget
                try
                {
                    using (var src = new SqliteConnection($"Data Source={uploadedFile}"))
                    using (var dest = new SqliteConnection($"Data Source={tempTarget}"))
                    {
                        await src.OpenAsync();
                        await dest.OpenAsync();
                        src.BackupDatabase(dest);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to write uploaded DB to temp target.");
                    try { System.IO.File.Delete(uploadedFile); } catch { }
                    try { System.IO.File.Delete(tempTarget); } catch { }
                    return StatusCode(500, "Failed to apply uploaded DB to temp target: " + ex.Message);
                }

                // 5) Try to close any AppDbContext connections to reduce chance of lock
                try
                {
                    using var scope = _services.CreateScope();
                    var appDb = scope.ServiceProvider.GetService(typeof(AppDbContext)) as IDisposable;
                    if (appDb != null)
                    {
                        try
                        {
                            // If AppDbContext implements IDisposable, Dispose will be called.
                            appDb.Dispose();
                        }
                        catch { /* ignore */ }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve/dispose AppDbContext before replace (continuing).");
                }

                // 6) Atomically replace active DB with tempTarget.
                // Use File.Replace when target exists, fallback to Move when it doesn't.
                var replaceSucceeded = false;
                const int maxReplaceAttempts = 8;
                for (int attempt = 1; attempt <= maxReplaceAttempts; attempt++)
                {
                    try
                    {
                        if (System.IO.File.Exists(_dbPath))
                        {
                            // File.Replace requires target exists. The third param is a backup file path; pass null to skip backup.
                            System.IO.File.Replace(tempTarget, _dbPath, null);
                        }
                        else
                        {
                            System.IO.File.Move(tempTarget, _dbPath);
                        }

                        replaceSucceeded = true;
                        break;
                    }
                    catch (IOException ioEx)
                    {
                        _logger.LogWarning(ioEx, "Attempt {Attempt} to replace DB file failed (in use). Retrying...", attempt);
                        await Task.Delay(300 * attempt);
                    }
                    catch (UnauthorizedAccessException uaEx)
                    {
                        _logger.LogWarning(uaEx, "Attempt {Attempt} to replace DB file failed (access). Retrying...", attempt);
                        await Task.Delay(300 * attempt);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error replacing DB file");
                        break;
                    }
                }

                // Cleanup uploaded file
                try { if (System.IO.File.Exists(uploadedFile)) System.IO.File.Delete(uploadedFile); } catch { }

                if (!replaceSucceeded)
                {
                    // If replace failed, try to remove tempTarget and inform user to restart app and try again
                    try { if (System.IO.File.Exists(tempTarget)) System.IO.File.Delete(tempTarget); } catch { }
                    _logger.LogError("Failed to replace DB after multiple attempts. The uploaded temp file is at {Temp}", tempTarget);
                    return StatusCode(500, "Restore failed: cannot replace active DB file (likely locked). Stop the application and try again.");
                }

                // Optionally remove -wal/-shm of old DB (best-effort)
                try { var wal = _dbPath + "-wal"; if (System.IO.File.Exists(wal)) System.IO.File.Delete(wal); } catch { }
                try { var shm = _dbPath + "-shm"; if (System.IO.File.Exists(shm)) System.IO.File.Delete(shm); } catch { }

                // Important: depending on your AppDbContext lifetime, existing DbContexts in memory may still point to stale connections.
                // Recommend restarting the app after successful restore.
                _logger.LogInformation("Restore completed, DB replaced. A fallback copy is at {Fallback}", fallback);
                return Ok(new { message = "Restore completed. A fallback backup was saved (if an original DB existed). Please restart the application to ensure all contexts use the restored database.", fallbackFile = Path.GetFileName(fallback) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore failed");
                return StatusCode(500, $"Restore failed: {ex.Message}");
            }
            finally
            {
                _opLock.Release();
            }
        }

        public class UploadFileModel
        {
            public Microsoft.AspNetCore.Http.IFormFile File { get; set; }
        }
    }
}
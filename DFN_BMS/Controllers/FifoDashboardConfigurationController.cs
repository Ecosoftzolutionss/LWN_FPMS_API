using System.Diagnostics;
using System.Text;
using DFN_BMS.DB;
using LWN_IPMS_Dashboard.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LWN_IPMS_Dashboard.Controllers
{
    public class SaveFifoDashboardConfigurationRequest
    {
        public int DisplaySeconds { get; set; } = 5;
        public int DisplayRows { get; set; } = 8;
        public string InputPath { get; set; } = string.Empty;
        public List<ConfiguredPartRequest> Parts { get; set; } = new();
    }

    public class ConfiguredPartRequest
    {
        public int Position { get; set; }
        public int ItemId { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class FifoDashboardConfigurationController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<FifoDashboardConfigurationController> _logger;

        public FifoDashboardConfigurationController(
            AppDbContext db,
            ILogger<FifoDashboardConfigurationController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // GET: api/FifoDashboardConfiguration
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            try
            {
                var settings = await _db.FifoDashboardSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == 1);

                if (settings == null)
                {
                    return NotFound(new
                    {
                        message = "FIFO dashboard settings are not initialized."
                    });
                }

                var parts = await (
                    from config in _db.FifoDashboardPartConfigurations.AsNoTracking()
                    join item in _db.ItemMasters.AsNoTracking()
                        on config.ItemId equals item.Id
                    orderby config.Position
                    select new
                    {
                        position = config.Position,
                        itemId = item.Id,
                        itemNumber = item.ItemNumber,
                        itemName = item.ItemName,
                        modelName = item.ItemModel
                    })
                    .ToListAsync();

                return Ok(new
                {
                    displaySeconds = settings.DisplaySeconds,
                    displayRows = settings.DisplayRows,
                    inputPath = settings.InputPath,
                    parts
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to load FIFO dashboard configuration.");

                return StatusCode(500, new
                {
                    message = "Failed to load FIFO dashboard configuration."
                });
            }
        }

        // PUT: api/FifoDashboardConfiguration
        [HttpPut]
        public async Task<IActionResult> Save(
            [FromBody] SaveFifoDashboardConfigurationRequest request)
        {
            // 1. Validate request
            if (request == null)
            {
                return BadRequest(new
                {
                    message = "Configuration is required."
                });
            }

            if (request.DisplaySeconds < 1 ||
                request.DisplaySeconds > 3600)
            {
                return BadRequest(new
                {
                    message = "Display seconds must be between 1 and 3600."
                });
            }

            if (request.DisplayRows < 1 ||
                request.DisplayRows > 100)
            {
                return BadRequest(new
                {
                    message = "Display rows must be between 1 and 100."
                });
            }

            // Input path validation
            var inputPath = (request.InputPath ?? string.Empty).Trim();

            if (inputPath.Length == 0)
            {
                return BadRequest(new
                {
                    message = "Input path is required."
                });
            }

            if (inputPath.Length > 500)
            {
                return BadRequest(new
                {
                    message = "Input path cannot exceed 500 characters."
                });
            }

            if (inputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
                !Path.IsPathRooted(inputPath))
            {
                return BadRequest(new
                {
                    message = "Input path must be a valid absolute path, e.g. D:\\AutoFile\\PPT."
                });
            }

            if (request.Parts == null)
            {
                return BadRequest(new
                {
                    message = "Parts configuration is required."
                });
            }

            if (request.Parts.Any(p =>
                p.Position < 1 ||
                p.Position > request.DisplayRows))
            {
                return BadRequest(new
                {
                    message = "Every part position must be within Display Rows."
                });
            }

            if (request.Parts.Any(p => p.ItemId <= 0))
            {
                return BadRequest(new
                {
                    message = "Every ItemId must be valid."
                });
            }

            if (request.Parts.GroupBy(p => p.Position)
                .Any(g => g.Count() > 1))
            {
                return BadRequest(new
                {
                    message = "A display position can contain only one part."
                });
            }

            if (request.Parts.GroupBy(p => p.ItemId)
                .Any(g => g.Count() > 1))
            {
                return BadRequest(new
                {
                    message = "A part cannot occupy multiple display positions."
                });
            }

            try
            {
                // 2. Validate that all configured parts exist
                var itemIds = request.Parts
                    .Select(p => p.ItemId)
                    .Distinct()
                    .ToList();

                var existingIds = await _db.ItemMasters
                    .AsNoTracking()
                    .Where(i => itemIds.Contains(i.Id))
                    .Select(i => i.Id)
                    .ToListAsync();

                if (existingIds.Count != itemIds.Count)
                {
                    return BadRequest(new
                    {
                        message = "One or more configured parts do not exist."
                    });
                }

                // 3. Use the configured SQL Server execution strategy.
                var strategy = _db.Database.CreateExecutionStrategy();

                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction =
                        await _db.Database.BeginTransactionAsync();

                    try
                    {
                        // 4. Load or create dashboard settings
                        var settings = await _db.FifoDashboardSettings
                            .FirstOrDefaultAsync(x => x.Id == 1);

                        if (settings == null)
                        {
                            settings = new FifoDashboardSetting
                            {
                                Id = 1,
                                DisplaySeconds = request.DisplaySeconds,
                                DisplayRows = request.DisplayRows,
                                InputPath = inputPath,
                                ModifiedDate = DateTime.Now
                            };

                            _db.FifoDashboardSettings.Add(settings);
                        }
                        else
                        {
                            settings.DisplaySeconds = request.DisplaySeconds;
                            settings.DisplayRows = request.DisplayRows;
                            settings.InputPath = inputPath;
                            settings.ModifiedDate = DateTime.Now;
                        }

                        // 5. Remove the previous part configuration
                        var currentConfigurations =
                            await _db.FifoDashboardPartConfigurations
                                .ToListAsync();

                        _db.FifoDashboardPartConfigurations
                            .RemoveRange(currentConfigurations);

                        // 6. Insert the newly configured parts
                        foreach (var part in request.Parts)
                        {
                            _db.FifoDashboardPartConfigurations.Add(
                                new FifoDashboardPartConfiguration
                                {
                                    Position = part.Position,
                                    ItemId = part.ItemId,
                                    ModifiedDate = DateTime.Now
                                });
                        }

                        // 7. Save everything within the transaction
                        await _db.SaveChangesAsync();

                        // 8. Commit the transaction
                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                });

                return Ok(new
                {
                    message = "FIFO dashboard configuration saved successfully.",
                    displaySeconds = request.DisplaySeconds,
                    displayRows = request.DisplayRows,
                    inputPath,
                    configuredParts = request.Parts.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to save FIFO dashboard configuration.");

                return StatusCode(500, new
                {
                    message = "Failed to save FIFO dashboard configuration.",
                    detail = ex.GetBaseException().Message
                });
            }
        }

        // GET: api/FifoDashboardConfiguration/pick-folder?initialPath=C:\SOMS
        // Opens the native Windows "Select Folder" dialog (File Explorer style)
        // on the machine that runs this API and returns the chosen full path.
        // Works when the API runs in the user's interactive Windows session
        // (e.g. the app runs on the same PC as the browser).
        [HttpGet("pick-folder")]
        public async Task<IActionResult> PickFolder(
            [FromQuery] string? initialPath,
            CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
            {
                return StatusCode(501, new
                {
                    message = "The native folder dialog is only available on Windows."
                });
            }

            const string script = @"
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$dialog = New-Object System.Windows.Forms.FolderBrowserDialog
$dialog.Description = 'Select input folder'
$dialog.ShowNewFolderButton = $true
if ($env:FIFO_INITIAL_PATH -and (Test-Path -LiteralPath $env:FIFO_INITIAL_PATH)) {
    $dialog.SelectedPath = $env:FIFO_INITIAL_PATH
}
$owner = New-Object System.Windows.Forms.Form
$owner.TopMost = $true
$result = $dialog.ShowDialog($owner)
if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
    [Console]::Out.Write($dialog.SelectedPath)
}
$owner.Dispose()
";

            try
            {
                var encodedScript = Convert.ToBase64String(
                    Encoding.Unicode.GetBytes(script));

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        $"-NoProfile -STA -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                startInfo.Environment["FIFO_INITIAL_PATH"] = initialPath ?? string.Empty;

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return StatusCode(500, new
                    {
                        message = "Could not open the folder dialog."
                    });
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();

                using var timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));

                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch { /* already closed */ }

                    return StatusCode(408, new
                    {
                        message = "The folder dialog timed out."
                    });
                }

                var selected = (await outputTask)
                    .Trim('\uFEFF', ' ', '\r', '\n');

                return Ok(new
                {
                    cancelled = selected.Length == 0,
                    path = selected
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open the native folder dialog.");

                return StatusCode(500, new
                {
                    message = "Failed to open the folder dialog."
                });
            }
        }

        // GET: api/FifoDashboardConfiguration/browse?path=D:\AutoFile
        [HttpGet("browse")]
        public IActionResult Browse([FromQuery] string? path)
        {
            try
            {
                // No path: list available drives
                if (string.IsNullOrWhiteSpace(path))
                {
                    var drives = DriveInfo.GetDrives()
                        .Where(d => d.IsReady)
                        .Select(d => new
                        {
                            name = d.Name,
                            path = d.RootDirectory.FullName
                        })
                        .ToList();

                    return Ok(new
                    {
                        current = "",
                        parent = (string?)null,
                        folders = drives
                    });
                }

                var full = Path.GetFullPath(path);

                if (!Directory.Exists(full))
                {
                    return NotFound(new { message = "Folder not found." });
                }

                var folders = new DirectoryInfo(full)
                    .EnumerateDirectories()
                    .Where(d => (d.Attributes &
                        (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    .OrderBy(d => d.Name)
                    .Select(d => new
                    {
                        name = d.Name,
                        path = d.FullName
                    })
                    .ToList();

                // Empty string means "go back to the drive list"
                var parent = Directory.GetParent(full)?.FullName ?? "";

                return Ok(new
                {
                    current = full,
                    parent,
                    folders
                });
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(403, new
                {
                    message = "Access to this folder is denied."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to browse folders.");

                return StatusCode(500, new
                {
                    message = "Failed to browse folders."
                });
            }
        }
    }
}

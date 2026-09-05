using DFN_BMS.DB;
using DFN_BMS.Models;
using DFN_BMS.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DFN_BMS.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly EncryptionService _enc;

        public AuthController(
            AppDbContext context,
            EncryptionService enc)
        {
            _context = context;
            _enc = enc;
        }

        public class StorageInfo
        {
            public string Drive { get; set; }
            public double FreeSpaceGB { get; set; }
            public double TotalSpaceGB { get; set; }
            public string VolumeLabel { get; set; }
        }

        // =========================================================
        // STORAGE CALIBRATION
        // POST: /api/Auth/storage/calibrate
        // =========================================================

        [HttpPost("storage/calibrate")]
        public async Task<IActionResult> CalibrateStorage()
        {
            try
            {
                var driveList = new List<StorageInfo>();

                // -----------------------------------------------------
                // Check available drives
                // -----------------------------------------------------

                string[] drives =
                {
            @"C:\",
            @"D:\",
            @"E:\"
        };

                foreach (string driveLetter in drives)
                {
                    try
                    {
                        DriveInfo di = new DriveInfo(driveLetter);

                        if (di.IsReady)
                        {
                            driveList.Add(new StorageInfo
                            {
                                Drive = di.Name.Replace(@":\", ""),
                                FreeSpaceGB = Math.Round(
                                    di.AvailableFreeSpace /
                                    (1024d * 1024 * 1024),
                                    2
                                ),

                                TotalSpaceGB = Math.Round(
                                    di.TotalSize /
                                    (1024d * 1024 * 1024),
                                    2
                                ),

                                VolumeLabel = di.VolumeLabel
                            });
                        }
                    }
                    catch
                    {
                        // Ignore unavailable drives
                    }
                }

                // -----------------------------------------------------
                // Recalculate Database Storage
                // -----------------------------------------------------

                decimal freeSpaceMB = 0;
                decimal currentSizeMB = 0;
                string dbName = "";

                using (var conn = _context.Database.GetDbConnection())
                {
                    await conn.OpenAsync();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                    SELECT TOP 1
                        DB_NAME() AS DbName,
                        size / 128.0 AS CurrentSizeMB,
                        size / 128.0 -
                        CAST(
                            FILEPROPERTY(
                                name,
                                'SpaceUsed'
                            ) AS INT
                        ) / 128.0 AS FreeSpaceMB
                    FROM sys.database_files
                    WHERE type IN (0,1)
                    ORDER BY size DESC";

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                dbName =
                                    reader["DbName"]?.ToString() ?? "";

                                currentSizeMB =
                                    Convert.ToDecimal(
                                        reader["CurrentSizeMB"]
                                    );

                                freeSpaceMB =
                                    Convert.ToDecimal(
                                        reader["FreeSpaceMB"]
                                    );
                            }
                        }
                    }
                }

                // -----------------------------------------------------
                // Calibration Result
                // -----------------------------------------------------

                return Ok(new
                {
                    success = true,

                    message = "Storage calibration completed successfully",

                    calibratedAt = DateTime.Now,

                    databaseName = dbName,

                    databaseSizeMB =
                        Math.Round(
                            currentSizeMB,
                            2
                        ),

                    databaseFreeMB =
                        Math.Round(
                            freeSpaceMB,
                            2
                        ),

                    warning = freeSpaceMB < 500,

                    drives = driveList
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,

                    message =
                        ex.InnerException?.Message
                        ?? ex.Message
                });
            }
        }

        // =========================================================
        // LOGIN
        // POST: /api/Auth/login
        // =========================================================

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            try
            {
                // =================================================
                // VALIDATE USERNAME
                // =================================================

                if (string.IsNullOrWhiteSpace(request.Username))
                {
                    return BadRequest(new
                    {
                        message = "Username is required"
                    });
                }

                // =================================================
                // VALIDATE PASSWORD
                // =================================================

                if (string.IsNullOrWhiteSpace(request.Password))
                {
                    return BadRequest(new
                    {
                        message = "Password is required"
                    });
                }

                // =================================================
                // NORMALIZE USERNAME
                // =================================================

                string loginText =
                    request.Username.Trim().ToLower();

                // =================================================
                // FIND ACTIVE USER
                // =================================================

                var user = await _context.UserMasters
                    .FirstOrDefaultAsync(u =>
                        u.IsActive &&
                        (
                            u.UserCode.ToLower() == loginText ||
                            u.EmployeeId.ToLower() == loginText ||
                            u.UserName.ToLower() == loginText
                        ));

                // =================================================
                // USER NOT FOUND
                // =================================================

                if (user == null)
                {
                    return Unauthorized(new
                    {
                        message =
                            "Invalid User ID / Employee ID / User Name"
                    });
                }

                // =================================================
                // DECRYPT PASSWORD
                // =================================================

                string decryptPassword =
                    _enc.Decrypt(user.PasswordHash);

                // =================================================
                // PASSWORD VALIDATION
                // =================================================

                if (decryptPassword != request.Password)
                {
                    return Unauthorized(new
                    {
                        message = "Invalid Password"
                    });
                }

                // =================================================
                // GET DEPARTMENT
                // =================================================

                var department =
                    await _context.DepartmentMasters
                        .FirstOrDefaultAsync(
                            x => x.Id == user.DepartmentId
                        );

                // =================================================
                // NO SESSION
                // NO DEVICE VALIDATION
                // NO ISLOGGEDIN
                // NO SESSIONID
                // NO LASTACTIVITY
                // =================================================

                return Ok(new
                {
                    message = "Login Success",

                    user = new
                    {
                        user.Id,

                        UserId = user.UserCode,

                        user.EmployeeId,

                        user.UserName,

                        user.DepartmentId,

                        DepartmentName =
                            department?.DepName
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message =
                        ex.InnerException?.Message
                        ?? ex.Message
                });
            }
        }
    }
}
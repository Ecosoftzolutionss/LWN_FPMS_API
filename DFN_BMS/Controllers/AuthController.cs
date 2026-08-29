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
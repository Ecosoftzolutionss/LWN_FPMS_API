using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DFN_BMS.DB;
using DFN_BMS.Models;

namespace DFN_BMS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CustomerMasterController : ControllerBase
    {
        private readonly AppDbContext _context;

        private static readonly Regex GstRegex =
            new Regex(
                @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$"
            );

        private static readonly Regex NameRegex =
            new Regex(@"^[A-Za-z0-9_ ]+$");

        public CustomerMasterController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // GET ALL
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await _context.CustomerMasters
                .Include(x => x.CustomerGroup)
                .OrderByDescending(x => x.Id)
                .ToListAsync();

            return Ok(list);
        }

        // =========================================================
        // GET BY ID
        // =========================================================
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.CustomerMasters
                .Include(x => x.CustomerGroup)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (item == null)
                return NotFound(new
                {
                    message = "Customer not found"
                });

            return Ok(item);
        }

        // =========================================================
        // CREATE
        // =========================================================
        [HttpPost]
        public async Task<IActionResult> Create(
            [FromBody] CustomerMaster model)
        {
            // -----------------------------------------------------
            // Basic validation
            // -----------------------------------------------------
            if (string.IsNullOrWhiteSpace(model.CustomerCode) ||
                string.IsNullOrWhiteSpace(model.CustomerName) ||
                string.IsNullOrWhiteSpace(model.CustomerDivision))
            {
                return BadRequest(new
                {
                    message = "Please fill all required fields"
                });
            }

            // -----------------------------------------------------
            // Customer Group
            // CustomerDivision currently contains:
            // Internal / External
            // -----------------------------------------------------
            if (model.CustomerGroupId <= 0)
            {
                return BadRequest(new
                {
                    message = "Customer Group is required"
                });
            }

            var customerGroup = await _context.CustomerGroupMasters
                .FirstOrDefaultAsync(x => x.Id == model.CustomerGroupId);

            if (customerGroup == null)
            {
                return BadRequest(new
                {
                    message = "Invalid Customer Group"
                });
            }

            // Internal customers do not use GST.
            // External customers may or may not have GST.
            bool isExternal = customerGroup.CustomerGroupType.Equals(
                "External", StringComparison.OrdinalIgnoreCase);

            bool isInternal = customerGroup.CustomerGroupType.Equals(
                "Internal", StringComparison.OrdinalIgnoreCase);

            if (!isExternal && !isInternal)
            {
                return BadRequest(new
                {
                    message = "Customer Group must be Internal or External"
                });
            }

            // External customers may or may not have a GST number.
            // The frontend sends the GST value only when the GST Available checkbox is selected.
            bool isGstAvailable = isExternal && model.GstAvailable;

            // -----------------------------------------------------
            // Customer Name
            // -----------------------------------------------------
            var customerName =
                model.CustomerName.Trim();

            if (!NameRegex.IsMatch(customerName))
            {
                return BadRequest(new
                {
                    message =
                        "Customer Name: only letters, numbers, underscore and spaces are allowed (e.g. Test_233)"
                });
            }

            // -----------------------------------------------------
            // Mobile
            // -----------------------------------------------------
            // Mobile Number is optional.
            // Validate the format only when a value is provided.
            string? mobileNumber = null;

            if (!string.IsNullOrWhiteSpace(model.MobileNumber))
            {
                mobileNumber = model.MobileNumber.Trim();

                if (!Regex.IsMatch(mobileNumber, @"^[0-9]{10}$"))
                {
                    return BadRequest(new
                    {
                        message =
                            "Mobile Number must be exactly 10 digits"
                    });
                }
            }

            // -----------------------------------------------------
            // Email
            // -----------------------------------------------------
            string? email = null;

            if (!string.IsNullOrWhiteSpace(model.EmailId))
            {
                email = model.EmailId.Trim();

                if (!Regex.IsMatch(
                        email,
                        @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
                {
                    return BadRequest(new
                    {
                        message = "Enter a valid email address"
                    });
                }
            }

            // -----------------------------------------------------
            // GST VALIDATION
            //
            // External + GST Available checked -> validate GST.
            // External + unchecked / Internal -> NOTPROVIDED.
            // -----------------------------------------------------
            string gstNo;

            if (isExternal && model.GstAvailable)
            {
                if (string.IsNullOrWhiteSpace(model.GstNo))
                {
                    return BadRequest(new
                    {
                        message = "GST No is required when GST Available is selected"
                    });
                }

                gstNo =
                    model.GstNo.Trim().ToUpper();

                if (!GstRegex.IsMatch(gstNo))
                {
                    return BadRequest(new
                    {
                        message =
                            "Enter a valid 15-character GSTIN (e.g. 33ABCDE1234F1Z5)"
                    });
                }
            }
            else
            {
                // GST not applicable for this Customer Group.
                gstNo = "NOTPROVIDED";
            }

            // -----------------------------------------------------
            // Customer ID duplicate
            // -----------------------------------------------------
            var code =
                model.CustomerCode.Trim();

            var codeExists = await _context.CustomerMasters
                .AnyAsync(x =>
                    x.CustomerCode.ToLower() ==
                    code.ToLower());

            if (codeExists)
            {
                return BadRequest(new
                {
                    message = "Customer ID already exists"
                });
            }

            // -----------------------------------------------------
            // Customer Name duplicate
            // -----------------------------------------------------
            var nameExists = await _context.CustomerMasters
                .AnyAsync(x =>
                    x.CustomerName.ToLower() ==
                    customerName.ToLower());

            if (nameExists)
            {
                return BadRequest(new
                {
                    message = "Customer Name already exists"
                });
            }

            // -----------------------------------------------------
            // Email duplicate
            // -----------------------------------------------------
            if (!string.IsNullOrWhiteSpace(email))
            {
                var emailExists = await _context.CustomerMasters
                    .AnyAsync(x =>
                        x.EmailId != null &&
                        x.EmailId.ToLower() ==
                        email.ToLower());

                if (emailExists)
                {
                    return BadRequest(new
                    {
                        message = "Email ID already exists"
                    });
                }
            }

            // -----------------------------------------------------
            // GST duplicate
            //
            // Only check actual GSTIN.
            // Don't check NOTPROVIDED.
            // -----------------------------------------------------
            if (isGstAvailable)
            {
                var gstExists = await _context.CustomerMasters
                    .AnyAsync(x =>
                        x.GstNo != null &&
                        x.GstNo.ToLower() ==
                        gstNo.ToLower());

                if (gstExists)
                {
                    return BadRequest(new
                    {
                        message = "GST No already exists"
                    });
                }
            }

            // -----------------------------------------------------
            // CREATE ENTITY
            // -----------------------------------------------------
            var entity = new CustomerMaster
            {
                CustomerCode = code,
                CustomerName = customerName,
                CustomerDivision =
                    customerGroup.CustomerGroupType,
                CustomerGroupId = customerGroup.Id,
                MobileNumber = mobileNumber,
                EmailId = email,
                GstNo = gstNo,
                CreatedDate = DateTime.Now
            };

            _context.CustomerMasters.Add(entity);

            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        // =========================================================
        // UPDATE
        // =========================================================
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(
            int id,
            [FromBody] CustomerMaster model)
        {
            var entity = await _context.CustomerMasters
                .FindAsync(id);

            if (entity == null)
            {
                return NotFound(new
                {
                    message = "Customer not found"
                });
            }

            // -----------------------------------------------------
            // Basic validation
            // -----------------------------------------------------
            if (string.IsNullOrWhiteSpace(model.CustomerName) ||
                string.IsNullOrWhiteSpace(model.CustomerDivision))
            {
                return BadRequest(new
                {
                    message = "Please fill all required fields"
                });
            }

            // -----------------------------------------------------
            // Find Customer Group
            // -----------------------------------------------------
            if (model.CustomerGroupId <= 0)
            {
                return BadRequest(new
                {
                    message = "Customer Group is required"
                });
            }

            var customerGroup =
                await _context.CustomerGroupMasters
                    .FirstOrDefaultAsync(x => x.Id == model.CustomerGroupId);

            if (customerGroup == null)
            {
                return BadRequest(new
                {
                    message = "Invalid Customer Group"
                });
            }

            // Internal customers do not use GST.
            // External customers may or may not have GST.
            bool isExternal = customerGroup.CustomerGroupType.Equals(
                "External", StringComparison.OrdinalIgnoreCase);

            bool isInternal = customerGroup.CustomerGroupType.Equals(
                "Internal", StringComparison.OrdinalIgnoreCase);

            if (!isExternal && !isInternal)
            {
                return BadRequest(new
                {
                    message = "Customer Group must be Internal or External"
                });
            }

            // External customers may or may not have a GST number.
            // The frontend sends the GST value only when the GST Available checkbox is selected.
            bool isGstAvailable = isExternal && model.GstAvailable;

            // -----------------------------------------------------
            // Customer Name
            // -----------------------------------------------------
            var customerName =
                model.CustomerName.Trim();

            if (!NameRegex.IsMatch(customerName))
            {
                return BadRequest(new
                {
                    message =
                        "Customer Name: only letters, numbers, underscore and spaces are allowed (e.g. Test_233)"
                });
            }

            // -----------------------------------------------------
            // Mobile
            // -----------------------------------------------------
            // Mobile Number is optional.
            // Validate the format only when a value is provided.
            string? mobileNumber = null;

            if (!string.IsNullOrWhiteSpace(model.MobileNumber))
            {
                mobileNumber = model.MobileNumber.Trim();

                if (!Regex.IsMatch(
                        mobileNumber,
                        @"^[0-9]{10}$"))
                {
                    return BadRequest(new
                    {
                        message =
                            "Mobile Number must be exactly 10 digits"
                    });
                }
            }

            // -----------------------------------------------------
            // Email
            // -----------------------------------------------------
            string? email = null;

            if (!string.IsNullOrWhiteSpace(model.EmailId))
            {
                email = model.EmailId.Trim();

                if (!Regex.IsMatch(
                        email,
                        @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
                {
                    return BadRequest(new
                    {
                        message = "Enter a valid email address"
                    });
                }
            }

            // -----------------------------------------------------
            // GST
            // -----------------------------------------------------
            string gstNo;

            if (isExternal && model.GstAvailable)
            {
                if (string.IsNullOrWhiteSpace(model.GstNo))
                {
                    return BadRequest(new
                    {
                        message = "GST No is required when GST Available is selected"
                    });
                }

                gstNo =
                    model.GstNo.Trim().ToUpper();

                if (!GstRegex.IsMatch(gstNo))
                {
                    return BadRequest(new
                    {
                        message =
                            "Enter a valid 15-character GSTIN (e.g. 33ABCDE1234F1Z5)"
                    });
                }
            }
            else
            {
                gstNo = "NOTPROVIDED";
            }

            // -----------------------------------------------------
            // Name duplicate
            // -----------------------------------------------------
            var nameExists =
                await _context.CustomerMasters
                    .AnyAsync(x =>
                        x.CustomerName.ToLower() ==
                        customerName.ToLower() &&
                        x.Id != id);

            if (nameExists)
            {
                return BadRequest(new
                {
                    message =
                        "Customer Name already exists"
                });
            }

            // -----------------------------------------------------
            // Email duplicate
            // -----------------------------------------------------
            if (!string.IsNullOrWhiteSpace(email))
            {
                var emailExists =
                    await _context.CustomerMasters
                        .AnyAsync(x =>
                            x.EmailId != null &&
                            x.EmailId.ToLower() ==
                            email.ToLower() &&
                            x.Id != id);

                if (emailExists)
                {
                    return BadRequest(new
                    {
                        message = "Email ID already exists"
                    });
                }
            }

            // -----------------------------------------------------
            // GST duplicate
            // -----------------------------------------------------
            if (isGstAvailable)
            {
                var gstExists =
                    await _context.CustomerMasters
                        .AnyAsync(x =>
                            x.GstNo != null &&
                            x.GstNo.ToLower() ==
                            gstNo.ToLower() &&
                            x.Id != id);

                if (gstExists)
                {
                    return BadRequest(new
                    {
                        message = "GST No already exists"
                    });
                }
            }

            // -----------------------------------------------------
            // UPDATE
            // -----------------------------------------------------
            entity.CustomerName = customerName;

            entity.CustomerDivision =
                customerGroup.CustomerGroupType;

            entity.CustomerGroupId =
                customerGroup.Id;

            entity.MobileNumber =
                mobileNumber;

            entity.EmailId =
                email;

            entity.GstNo =
                gstNo;

            entity.ModifiedDate =
                DateTime.Now;

            // CustomerCode intentionally not changed

            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        // =========================================================
        // DELETE
        // =========================================================
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity =
                await _context.CustomerMasters
                    .FindAsync(id);

            if (entity == null)
            {
                return NotFound(new
                {
                    message = "Customer not found"
                });
            }

            _context.CustomerMasters.Remove(entity);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Deleted Successfully"
            });
        }
    }
}
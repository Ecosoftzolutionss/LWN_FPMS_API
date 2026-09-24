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
    public class SupplierMasterController : ControllerBase
    {
        private readonly AppDbContext _context;

        private static readonly Regex ContactRegex =
            new Regex(@"^[0-9]{10}$");

        private static readonly Regex GstRegex =
            new Regex(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$");

        private static readonly Regex PanRegex =
            new Regex(@"^[A-Z]{5}[0-9]{4}[A-Z]{1}$");

        private static readonly Regex EmailRegex =
            new Regex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$");

        public SupplierMasterController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var list = await _context.SupplierMasters
                    .Include(x => x.SupplierGroup)
                    .OrderByDescending(x => x.Id)
                    .Select(x => new
                    {
                        x.Id,
                        x.SupplierCode,
                        x.SupplierName,
                        x.SupplierGroupId,
                        SupplierGroupName = x.SupplierGroup != null
                            ? x.SupplierGroup.SupplierGroupType
                            : null,
                        x.Email,
                        x.ContactNumber,
                        x.PersonToContact,
                        x.GstNo,
                        x.PanNo,
                        x.CreatedDate,
                        x.ModifiedDate
                    })
                    .ToListAsync();

                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to load suppliers",
                    error = ex.Message
                });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                var item = await _context.SupplierMasters
                    .Include(x => x.SupplierGroup)
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (item == null)
                {
                    return NotFound(new
                    {
                        message = "Supplier not found"
                    });
                }

                return Ok(item);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to load supplier",
                    error = ex.Message
                });
            }
        }

        private IActionResult? ValidateSupplierFields(SupplierMaster model)
        {
            if (model == null)
            {
                return BadRequest(new
                {
                    message = "Invalid supplier data"
                });
            }

            if (string.IsNullOrWhiteSpace(model.SupplierCode))
            {
                return BadRequest(new
                {
                    message = "Supplier ID is required"
                });
            }

            if (string.IsNullOrWhiteSpace(model.SupplierName))
            {
                return BadRequest(new
                {
                    message = "Supplier Name is required"
                });
            }

            if (model.SupplierGroupId <= 0)
            {
                return BadRequest(new
                {
                    message = "Supplier Group is required"
                });
            }

            // Email is optional. Validate only when provided.
            if (!string.IsNullOrWhiteSpace(model.Email) &&
                !EmailRegex.IsMatch(model.Email.Trim()))
            {
                return BadRequest(new
                {
                    message = "Enter a valid email address"
                });
            }

            // Contact Number is optional. Validate only when provided.
            if (!string.IsNullOrWhiteSpace(model.ContactNumber) &&
                !ContactRegex.IsMatch(model.ContactNumber.Trim()))
            {
                return BadRequest(new
                {
                    message = "Contact Number must be exactly 10 digits"
                });
            }

            // PersonToContact is optional - no required validation.

            if (string.IsNullOrWhiteSpace(model.GstNo))
            {
                return BadRequest(new
                {
                    message = "GST No is required"
                });
            }

            if (!GstRegex.IsMatch(model.GstNo.Trim().ToUpper()))
            {
                return BadRequest(new
                {
                    message = "Enter a valid 15-character GSTIN (e.g. 33ABCDE1234F1Z5)"
                });
            }

            if (string.IsNullOrWhiteSpace(model.PanNo))
            {
                return BadRequest(new
                {
                    message = "PAN No is required"
                });
            }

            if (!PanRegex.IsMatch(model.PanNo.Trim().ToUpper()))
            {
                return BadRequest(new
                {
                    message = "Enter a valid 10-character PAN (e.g. ABCDE1234F)"
                });
            }

            return null;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SupplierMaster model)
        {
            try
            {
                var validationResult = ValidateSupplierFields(model);

                if (validationResult != null)
                {
                    return validationResult;
                }

                var groupExists = await _context.SupplierGroupMasters
                    .AnyAsync(g => g.Id == model.SupplierGroupId);

                if (!groupExists)
                {
                    return BadRequest(new
                    {
                        message = "Selected Supplier Group does not exist"
                    });
                }

                var supplierCode = model.SupplierCode.Trim();

                var codeExists = await _context.SupplierMasters
                    .AnyAsync(x =>
                        x.SupplierCode.ToLower() == supplierCode.ToLower());

                if (codeExists)
                {
                    return BadRequest(new
                    {
                        message = "Supplier ID already exists"
                    });
                }

                var supplierName = model.SupplierName.Trim();

                var nameExists = await _context.SupplierMasters
                    .AnyAsync(x =>
                        x.SupplierName.ToLower() == supplierName.ToLower());

                if (nameExists)
                {
                    return BadRequest(new
                    {
                        message = "Supplier Name already exists"
                    });
                }

                var gstNo = model.GstNo.Trim().ToUpper();

                var gstExists = await _context.SupplierMasters
                    .AnyAsync(x =>
                        x.GstNo.ToLower() == gstNo.ToLower());

                if (gstExists)
                {
                    return BadRequest(new
                    {
                        message = "GST No already exists"
                    });
                }

                var panNo = model.PanNo.Trim().ToUpper();

                var panExists = await _context.SupplierMasters
                    .AnyAsync(x =>
                        x.PanNo.ToLower() == panNo.ToLower());

                if (panExists)
                {
                    return BadRequest(new
                    {
                        message = "PAN No already exists"
                    });
                }

                var entity = new SupplierMaster
                {
                    SupplierCode = model.SupplierCode.Trim(),
                    SupplierName = model.SupplierName.Trim(),
                    SupplierGroupId = model.SupplierGroupId,

                    Email = string.IsNullOrWhiteSpace(model.Email)
                        ? null
                        : model.Email.Trim(),

                    ContactNumber = string.IsNullOrWhiteSpace(model.ContactNumber)
                        ? null
                        : model.ContactNumber.Trim(),

                    PersonToContact = string.IsNullOrWhiteSpace(model.PersonToContact)
                        ? null
                        : model.PersonToContact.Trim(),

                    GstNo = model.GstNo.Trim().ToUpper(),
                    PanNo = model.PanNo.Trim().ToUpper(),
                    CreatedDate = DateTime.Now,
                    ModifiedDate = null
                };

                _context.SupplierMasters.Add(entity);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Supplier Saved Successfully",
                    data = entity
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to save supplier",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(
            int id,
            [FromBody] SupplierMaster model)
        {
            try
            {
                var entity = await _context.SupplierMasters.FindAsync(id);

                if (entity == null)
                {
                    return NotFound(new
                    {
                        message = "Supplier not found"
                    });
                }

                var validationResult = ValidateSupplierFields(model);

                if (validationResult != null)
                {
                    return validationResult;
                }

                var groupExists = await _context.SupplierGroupMasters
                    .AnyAsync(g => g.Id == model.SupplierGroupId);

                if (!groupExists)
                {
                    return BadRequest(new
                    {
                        message = "Selected Supplier Group does not exist"
                    });
                }

                var supplierName = model.SupplierName.Trim();

                var nameExists = await _context.SupplierMasters
                    .AnyAsync(x =>
                        x.SupplierName.ToLower() == supplierName.ToLower()
                        && x.Id != id);

                if (nameExists)
                {
                    return BadRequest(new
                    {
                        message = "Supplier Name already exists"
                    });
                }

                var gstNo = model.GstNo.Trim().ToUpper();

                var gstExists = await _context.SupplierMasters
                    .AnyAsync(x =>
                        x.GstNo.ToLower() == gstNo.ToLower()
                        && x.Id != id);

                if (gstExists)
                {
                    return BadRequest(new
                    {
                        message = "GST No already exists"
                    });
                }

                var panNo = model.PanNo.Trim().ToUpper();

                var panExists = await _context.SupplierMasters
                    .AnyAsync(x =>
                        x.PanNo.ToLower() == panNo.ToLower()
                        && x.Id != id);

                if (panExists)
                {
                    return BadRequest(new
                    {
                        message = "PAN No already exists"
                    });
                }

                // Supplier Code intentionally remains unchanged.
                entity.SupplierName = model.SupplierName.Trim();
                entity.SupplierGroupId = model.SupplierGroupId;

                entity.Email = string.IsNullOrWhiteSpace(model.Email)
                    ? null
                    : model.Email.Trim();

                entity.ContactNumber = string.IsNullOrWhiteSpace(model.ContactNumber)
                    ? null
                    : model.ContactNumber.Trim();

                entity.PersonToContact = string.IsNullOrWhiteSpace(model.PersonToContact)
                    ? null
                    : model.PersonToContact.Trim();

                entity.GstNo = model.GstNo.Trim().ToUpper();
                entity.PanNo = model.PanNo.Trim().ToUpper();
                entity.ModifiedDate = DateTime.Now;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Supplier Updated Successfully",
                    data = entity
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to update supplier",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var entity = await _context.SupplierMasters.FindAsync(id);

                if (entity == null)
                {
                    return NotFound(new
                    {
                        message = "Supplier not found"
                    });
                }

                var usedInGrn = await _context.GrnHeaders
                    .AnyAsync(x => x.SupplierId == id);

                if (usedInGrn)
                {
                    return BadRequest(new
                    {
                        message =
                            "This supplier cannot be deleted because it is already used in GRN transactions."
                    });
                }

                _context.SupplierMasters.Remove(entity);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Supplier deleted successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to delete supplier",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }
    }
}

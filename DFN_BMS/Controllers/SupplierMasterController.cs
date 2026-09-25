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

        private static readonly Regex SupplierCodeRegex =
            new Regex(@"^[A-Z0-9-]+$");

        public SupplierMasterController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/SupplierMaster
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
                        SupplierGroupDescription = x.SupplierGroup != null
                            ? x.SupplierGroup.Description
                            : null,
                        RequiresGst = x.SupplierGroup != null && x.SupplierGroup.RequiresGst,
                        RequiresPan = x.SupplierGroup != null && x.SupplierGroup.RequiresPan,
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
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        // GET: api/SupplierMaster/5
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
                    return NotFound(new { message = "Supplier not found" });
                }

                return Ok(new
                {
                    item.Id,
                    item.SupplierCode,
                    item.SupplierName,
                    item.SupplierGroupId,
                    SupplierGroupName = item.SupplierGroup?.SupplierGroupType,
                    SupplierGroupDescription = item.SupplierGroup?.Description,
                    RequiresGst = item.SupplierGroup?.RequiresGst ?? false,
                    RequiresPan = item.SupplierGroup?.RequiresPan ?? false,
                    item.Email,
                    item.ContactNumber,
                    item.PersonToContact,
                    item.GstNo,
                    item.PanNo,
                    item.CreatedDate,
                    item.ModifiedDate
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to load supplier",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        private IActionResult? ValidateBasicFields(SupplierMaster model)
        {
            if (model == null)
                return BadRequest(new { message = "Invalid supplier data" });

            var supplierCode = model.SupplierCode?.Trim() ?? string.Empty;
            var supplierName = model.SupplierName?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(supplierCode))
                return BadRequest(new { message = "Supplier ID is required" });

            if (supplierCode.Length > 30 || !SupplierCodeRegex.IsMatch(supplierCode.ToUpper()))
            {
                return BadRequest(new
                {
                    message = "Supplier ID can contain only letters, numbers and hyphen (-)"
                });
            }

            if (string.IsNullOrWhiteSpace(supplierName))
                return BadRequest(new { message = "Supplier Name is required" });

            if (supplierName.Length > 150)
                return BadRequest(new { message = "Supplier Name cannot exceed 150 characters" });

            if (model.SupplierGroupId <= 0)
                return BadRequest(new { message = "Supplier Group is required" });

            if (!string.IsNullOrWhiteSpace(model.Email) &&
                !EmailRegex.IsMatch(model.Email.Trim()))
            {
                return BadRequest(new { message = "Enter a valid email address" });
            }

            if (!string.IsNullOrWhiteSpace(model.ContactNumber) &&
                !ContactRegex.IsMatch(model.ContactNumber.Trim()))
            {
                return BadRequest(new { message = "Contact Number must be exactly 10 digits" });
            }

            return null;
        }

        private IActionResult? ValidateTaxFields(
            SupplierMaster model,
            SupplierGroupMaster group)
        {
            var gst = model.GstNo?.Trim().ToUpper() ?? string.Empty;
            var pan = model.PanNo?.Trim().ToUpper() ?? string.Empty;

            if (group.RequiresGst)
            {
                if (string.IsNullOrWhiteSpace(gst))
                    return BadRequest(new { message = "GST No is required for the selected Supplier Group" });

                if (!GstRegex.IsMatch(gst))
                {
                    return BadRequest(new
                    {
                        message = "Enter a valid 15-character GSTIN (e.g. 33ABCDE1234F1Z5)"
                    });
                }
            }

            if (group.RequiresPan)
            {
                if (string.IsNullOrWhiteSpace(pan))
                    return BadRequest(new { message = "PAN No is required for the selected Supplier Group" });

                if (!PanRegex.IsMatch(pan))
                {
                    return BadRequest(new
                    {
                        message = "Enter a valid 10-character PAN (e.g. ABCDE1234F)"
                    });
                }
            }

            return null;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SupplierMaster model)
        {
            try
            {
                var basicValidation = ValidateBasicFields(model);
                if (basicValidation != null)
                    return basicValidation;

                var group = await _context.SupplierGroupMasters
                    .FirstOrDefaultAsync(x => x.Id == model.SupplierGroupId && x.IsActive);

                if (group == null)
                {
                    return BadRequest(new
                    {
                        message = "Selected Supplier Group does not exist or is inactive"
                    });
                }

                var taxValidation = ValidateTaxFields(model, group);
                if (taxValidation != null)
                    return taxValidation;

                var supplierCode = model.SupplierCode.Trim().ToUpper();
                var supplierName = model.SupplierName.Trim();
                var gstNo = model.GstNo?.Trim().ToUpper();
                var panNo = model.PanNo?.Trim().ToUpper();

                var codeExists = await _context.SupplierMasters
                    .AnyAsync(x => x.SupplierCode.ToLower() == supplierCode.ToLower());

                if (codeExists)
                    return BadRequest(new { message = "Supplier ID already exists" });

                var nameExists = await _context.SupplierMasters
                    .AnyAsync(x => x.SupplierName.ToLower() == supplierName.ToLower());

                if (nameExists)
                    return BadRequest(new { message = "Supplier Name already exists" });

                if (!string.IsNullOrWhiteSpace(gstNo))
                {
                    var gstExists = await _context.SupplierMasters
                        .AnyAsync(x => x.GstNo != null &&
                                      x.GstNo.ToLower() == gstNo.ToLower());

                    if (gstExists)
                        return BadRequest(new { message = "GST No already exists" });
                }

                if (!string.IsNullOrWhiteSpace(panNo))
                {
                    var panExists = await _context.SupplierMasters
                        .AnyAsync(x => x.PanNo != null &&
                                      x.PanNo.ToLower() == panNo.ToLower());

                    if (panExists)
                        return BadRequest(new { message = "PAN No already exists" });
                }

                var entity = new SupplierMaster
                {
                    SupplierCode = supplierCode,
                    SupplierName = supplierName,
                    SupplierGroupId = model.SupplierGroupId,
                    Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim(),
                    ContactNumber = string.IsNullOrWhiteSpace(model.ContactNumber) ? null : model.ContactNumber.Trim(),
                    PersonToContact = string.IsNullOrWhiteSpace(model.PersonToContact) ? null : model.PersonToContact.Trim(),

                    // Only save values that the selected group requires.
                    GstNo = group.RequiresGst ? gstNo : null,
                    PanNo = group.RequiresPan ? panNo : null,

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
        public async Task<IActionResult> Update(int id, [FromBody] SupplierMaster model)
        {
            try
            {
                var entity = await _context.SupplierMasters.FindAsync(id);

                if (entity == null)
                    return NotFound(new { message = "Supplier not found" });

                var basicValidation = ValidateBasicFields(model);
                if (basicValidation != null)
                    return basicValidation;

                var group = await _context.SupplierGroupMasters
                    .FirstOrDefaultAsync(x => x.Id == model.SupplierGroupId && x.IsActive);

                if (group == null)
                {
                    return BadRequest(new
                    {
                        message = "Selected Supplier Group does not exist or is inactive"
                    });
                }

                var taxValidation = ValidateTaxFields(model, group);
                if (taxValidation != null)
                    return taxValidation;

                var supplierCode = model.SupplierCode.Trim().ToUpper();
                var supplierName = model.SupplierName.Trim();
                var gstNo = model.GstNo?.Trim().ToUpper();
                var panNo = model.PanNo?.Trim().ToUpper();

                var codeExists = await _context.SupplierMasters
                    .AnyAsync(x => x.SupplierCode.ToLower() == supplierCode.ToLower() && x.Id != id);

                if (codeExists)
                    return BadRequest(new { message = "Supplier ID already exists" });

                var nameExists = await _context.SupplierMasters
                    .AnyAsync(x => x.SupplierName.ToLower() == supplierName.ToLower() && x.Id != id);

                if (nameExists)
                    return BadRequest(new { message = "Supplier Name already exists" });

                if (!string.IsNullOrWhiteSpace(gstNo))
                {
                    var gstExists = await _context.SupplierMasters
                        .AnyAsync(x => x.GstNo != null &&
                                      x.GstNo.ToLower() == gstNo.ToLower() &&
                                      x.Id != id);

                    if (gstExists)
                        return BadRequest(new { message = "GST No already exists" });
                }

                if (!string.IsNullOrWhiteSpace(panNo))
                {
                    var panExists = await _context.SupplierMasters
                        .AnyAsync(x => x.PanNo != null &&
                                      x.PanNo.ToLower() == panNo.ToLower() &&
                                      x.Id != id);

                    if (panExists)
                        return BadRequest(new { message = "PAN No already exists" });
                }

                entity.SupplierCode = supplierCode;
                entity.SupplierName = supplierName;
                entity.SupplierGroupId = model.SupplierGroupId;
                entity.Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim();
                entity.ContactNumber = string.IsNullOrWhiteSpace(model.ContactNumber) ? null : model.ContactNumber.Trim();
                entity.PersonToContact = string.IsNullOrWhiteSpace(model.PersonToContact) ? null : model.PersonToContact.Trim();
                entity.GstNo = group.RequiresGst ? gstNo : null;
                entity.PanNo = group.RequiresPan ? panNo : null;
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
                    return NotFound(new { message = "Supplier not found" });

                var usedInGrn = await _context.GrnHeaders
                    .AnyAsync(x => x.SupplierId == id);

                if (usedInGrn)
                {
                    return BadRequest(new
                    {
                        message = "This supplier cannot be deleted because it is already used in GRN transactions."
                    });
                }

                _context.SupplierMasters.Remove(entity);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Supplier deleted successfully" });
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

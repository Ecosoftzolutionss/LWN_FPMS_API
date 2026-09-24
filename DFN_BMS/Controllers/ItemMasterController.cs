using DFN_BMS.DB;
using DFN_BMS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace DFN_BMS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ItemMasterController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ItemMasterController(AppDbContext context)
        {
            _context = context;
        }

        // ============================================================
        // GET: api/ItemMaster/uom-list
        // ============================================================
        [HttpGet("uom-list")]
        public async Task<IActionResult> GetUomList()
        {
            var data = await _context.UomMasters
                .Where(x => x.IsActive)
                .Select(x => new
                {
                    value = x.UomName,
                    label = x.UomName
                })
                .OrderBy(x => x.label)
                .ToListAsync();

            return Ok(data);
        }

        // ============================================================
        // GET: api/ItemMaster
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var list = await _context.ItemMasters
                    .Include(x => x.ItemGroup)
                    .OrderByDescending(x => x.Id)
                    .Select(x => new
                    {
                        x.Id,
                        x.ItemNumber,
                        x.ItemName,
                        x.ItemGroupId,
                        ItemGroupName = x.ItemGroup != null
                            ? x.ItemGroup.GroupName
                            : null,

                        x.HsnCode,
                        x.UnitPrice,
                        x.CustomerOrSupplier,

                        // Effective date range
                        x.EffectiveFrom,
                        x.EffectiveTo,

                        x.Uom,
                        x.WeightPerUnit,
                        x.StuffQuantity,
                        x.ItemModel,
                        x.Usage,
                        x.Length,
                        x.Width,
                        x.Height,
                        x.Description,
                        x.SafetyLevel,
                        x.ReorderLevel,
                        x.DangerLevel,
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
                    message = "An error occurred while retrieving items.",
                    error = ex.Message
                });
            }
        }

        // ============================================================
        // GET: api/ItemMaster/{id}
        // ============================================================
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.ItemMasters
                .Include(x => x.ItemGroup)
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,
                    x.ItemNumber,
                    x.ItemName,
                    x.ItemGroupId,
                    ItemGroupName = x.ItemGroup != null
                        ? x.ItemGroup.GroupName
                        : null,

                    x.HsnCode,
                    x.UnitPrice,
                    x.CustomerOrSupplier,

                    // Effective date range
                    x.EffectiveFrom,
                    x.EffectiveTo,

                    x.Uom,
                    x.WeightPerUnit,
                    x.StuffQuantity,
                    x.ItemModel,
                    x.Usage,
                    x.Length,
                    x.Width,
                    x.Height,
                    x.Description,
                    x.SafetyLevel,
                    x.ReorderLevel,
                    x.DangerLevel,
                    x.CreatedDate,
                    x.ModifiedDate
                })
                .FirstOrDefaultAsync();

            if (item == null)
                return NotFound(new
                {
                    message = "Item not found"
                });

            return Ok(item);
        }

        // ============================================================
        // Register UOM if it does not already exist
        // ============================================================
        private async Task RegisterUomIfNewAsync(ItemMaster item)
        {
            if (string.IsNullOrWhiteSpace(item.Uom))
                return;

            var uomName = item.Uom.Trim().ToUpper();

            var exists = await _context.UomMasters
                .AnyAsync(x => x.UomName.ToUpper() == uomName);

            if (!exists)
            {
                _context.UomMasters.Add(new UomMaster
                {
                    UomName = uomName,
                    IsActive = true
                });

                await _context.SaveChangesAsync();
            }

            item.Uom = uomName;
        }

        // ============================================================
        // POST: api/ItemMaster
        // ============================================================
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ItemMaster model)
        {
            if (model == null)
            {
                return BadRequest(new
                {
                    message = "Invalid request"
                });
            }

            // --------------------------------------------------------
            // Required field validation
            // --------------------------------------------------------
            if (string.IsNullOrWhiteSpace(model.ItemNumber) ||
                string.IsNullOrWhiteSpace(model.ItemName) ||
                string.IsNullOrWhiteSpace(model.CustomerOrSupplier) ||
                string.IsNullOrWhiteSpace(model.Uom) ||
                model.ItemGroupId <= 0)
            {
                return BadRequest(new
                {
                    message =
                        "Item Number, Item Name, Item Group, Customer / Supplier and UOM are required"
                });
            }

            // --------------------------------------------------------
            // Unit Price validation
            // --------------------------------------------------------
            if (model.UnitPrice <= 0)
            {
                return BadRequest(new
                {
                    message = "Unit Price must be greater than 0"
                });
            }

            // --------------------------------------------------------
            // Effective From validation
            // --------------------------------------------------------
            if (model.EffectiveFrom == default)
            {
                return BadRequest(new
                {
                    message = "Effective From is required"
                });
            }

            if (model.EffectiveFrom.Date < DateTime.Today)
            {
                return BadRequest(new
                {
                    message = "Effective From cannot be a past date"
                });
            }

            // --------------------------------------------------------
            // Effective To validation
            // --------------------------------------------------------
            // --------------------------------------------------------
            // Effective To validation
            // --------------------------------------------------------
            if (model.EffectiveTo == default)
            {
                return BadRequest(new
                {
                    message = "Effective To is required"
                });
            }

            if (model.EffectiveTo.Date < model.EffectiveFrom.Date)
            {
                return BadRequest(new
                {
                    message = "Effective To cannot be before Effective From"
                });
            }

            // --------------------------------------------------------
            // Customer / Supplier validation
            // --------------------------------------------------------
            var customerOrSupplier = model.CustomerOrSupplier.Trim();

            if (!string.Equals(
                    customerOrSupplier,
                    "Customer",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    customerOrSupplier,
                    "Supplier",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    message =
                        "Customer / Supplier must be Customer or Supplier"
                });
            }

            customerOrSupplier =
                string.Equals(
                    customerOrSupplier,
                    "Customer",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Customer"
                    : "Supplier";

            // --------------------------------------------------------
            // Item Group validation
            // --------------------------------------------------------
            var groupExists = await _context.ItemGroupMasters
                .AnyAsync(g => g.Id == model.ItemGroupId);

            if (!groupExists)
            {
                return BadRequest(new
                {
                    message = "Selected Item Group does not exist"
                });
            }

            // --------------------------------------------------------
            // Trim values
            // --------------------------------------------------------
            var itemNumber = model.ItemNumber.Trim();
            var itemName = model.ItemName.Trim();

            // --------------------------------------------------------
            // Duplicate Item Number validation
            // --------------------------------------------------------
            var numberExists = await _context.ItemMasters
                .AnyAsync(x =>
                    x.ItemNumber != null &&
                    x.ItemNumber.ToLower() == itemNumber.ToLower());

            if (numberExists)
            {
                return BadRequest(new
                {
                    message = "Item Number already exists"
                });
            }

            // --------------------------------------------------------
            // Duplicate Item Name validation
            // --------------------------------------------------------
            var nameExists = await _context.ItemMasters
                .AnyAsync(x =>
                    x.ItemName != null &&
                    x.ItemName.ToLower() == itemName.ToLower());

            if (nameExists)
            {
                return BadRequest(new
                {
                    message = "Item Name already exists"
                });
            }

            // --------------------------------------------------------
            // Register UOM
            // --------------------------------------------------------
            await RegisterUomIfNewAsync(model);

            // --------------------------------------------------------
            // Dimensions are meaningful only for PCS
            // --------------------------------------------------------
            if (!string.Equals(
                    model.Uom,
                    "PCS",
                    StringComparison.OrdinalIgnoreCase))
            {
                model.Length = null;
                model.Width = null;
                model.Height = null;
            }

            // --------------------------------------------------------
            // Create entity
            // --------------------------------------------------------
            var entity = new ItemMaster
            {
                ItemNumber = itemNumber,
                ItemName = itemName,
                ItemGroupId = model.ItemGroupId,

                HsnCode = string.IsNullOrWhiteSpace(model.HsnCode)
                    ? null
                    : model.HsnCode.Trim(),

                UnitPrice = model.UnitPrice,

                CustomerOrSupplier = customerOrSupplier,

                // Effective date range
                EffectiveFrom = model.EffectiveFrom.Date,
                EffectiveTo = model.EffectiveTo.Date,

                Uom = model.Uom.Trim().ToUpper(),

                WeightPerUnit = model.WeightPerUnit,
                StuffQuantity = model.StuffQuantity,

                ItemModel = string.IsNullOrWhiteSpace(model.ItemModel)
                    ? null
                    : model.ItemModel.Trim(),

                Usage = string.IsNullOrWhiteSpace(model.Usage)
                    ? null
                    : model.Usage.Trim(),

                Length = model.Length,
                Width = model.Width,
                Height = model.Height,

                Description = string.IsNullOrWhiteSpace(model.Description)
                    ? null
                    : model.Description.Trim(),

                SafetyLevel = model.SafetyLevel,
                ReorderLevel = model.ReorderLevel,

                DangerLevel = string.IsNullOrWhiteSpace(model.DangerLevel)
                    ? null
                    : model.DangerLevel.Trim(),

                CreatedDate = DateTime.Now
            };

            // --------------------------------------------------------
            // Save
            // --------------------------------------------------------
            _context.ItemMasters.Add(entity);

            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        // ============================================================
        // PUT: api/ItemMaster/{id}
        // ============================================================
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(
            int id,
            [FromBody] ItemMaster model)
        {
            if (model == null)
            {
                return BadRequest(new
                {
                    message = "Invalid request"
                });
            }

            // --------------------------------------------------------
            // Find existing entity
            // --------------------------------------------------------
            var entity = await _context.ItemMasters.FindAsync(id);

            if (entity == null)
            {
                return NotFound(new
                {
                    message = "Item not found"
                });
            }

            // --------------------------------------------------------
            // Required field validation
            // --------------------------------------------------------
            if (string.IsNullOrWhiteSpace(model.ItemName) ||
                string.IsNullOrWhiteSpace(model.CustomerOrSupplier) ||
                string.IsNullOrWhiteSpace(model.Uom) ||
                model.ItemGroupId <= 0)
            {
                return BadRequest(new
                {
                    message =
                        "Item Name, Item Group, Customer / Supplier and UOM are required"
                });
            }

            // --------------------------------------------------------
            // Unit Price validation
            // --------------------------------------------------------
            if (model.UnitPrice <= 0)
            {
                return BadRequest(new
                {
                    message = "Unit Price must be greater than 0"
                });
            }

            // --------------------------------------------------------
            // Effective From validation
            // --------------------------------------------------------
            if (model.EffectiveFrom == default)
            {
                return BadRequest(new
                {
                    message = "Effective From is required"
                });
            }

            if (model.EffectiveFrom.Date < DateTime.Today)
            {
                return BadRequest(new
                {
                    message = "Effective From cannot be a past date"
                });
            }

            // --------------------------------------------------------
            // Effective To validation
            // --------------------------------------------------------
            // --------------------------------------------------------
            // Effective To validation
            // --------------------------------------------------------
            if (model.EffectiveTo == default)
            {
                return BadRequest(new
                {
                    message = "Effective To is required"
                });
            }

            if (model.EffectiveTo.Date < model.EffectiveFrom.Date)
            {
                return BadRequest(new
                {
                    message = "Effective To cannot be before Effective From"
                });
            }

            // --------------------------------------------------------
            // Customer / Supplier validation
            // --------------------------------------------------------
            var customerOrSupplier = model.CustomerOrSupplier.Trim();

            if (!string.Equals(
                    customerOrSupplier,
                    "Customer",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    customerOrSupplier,
                    "Supplier",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    message =
                        "Customer / Supplier must be Customer or Supplier"
                });
            }

            customerOrSupplier =
                string.Equals(
                    customerOrSupplier,
                    "Customer",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Customer"
                    : "Supplier";

            // --------------------------------------------------------
            // Item Group validation
            // --------------------------------------------------------
            var groupExists = await _context.ItemGroupMasters
                .AnyAsync(g => g.Id == model.ItemGroupId);

            if (!groupExists)
            {
                return BadRequest(new
                {
                    message = "Selected Item Group does not exist"
                });
            }

            // --------------------------------------------------------
            // Trim Item Name
            // --------------------------------------------------------
            var itemName = model.ItemName.Trim();

            // --------------------------------------------------------
            // Duplicate Item Name validation
            // --------------------------------------------------------
            var nameExists = await _context.ItemMasters
                .AnyAsync(x =>
                    x.ItemName != null &&
                    x.ItemName.ToLower() == itemName.ToLower() &&
                    x.Id != id);

            if (nameExists)
            {
                return BadRequest(new
                {
                    message = "Item Name already exists"
                });
            }

            // --------------------------------------------------------
            // Register UOM
            // --------------------------------------------------------
            await RegisterUomIfNewAsync(model);

            // --------------------------------------------------------
            // Dimensions are meaningful only for PCS
            // --------------------------------------------------------
            if (!string.Equals(
                    model.Uom,
                    "PCS",
                    StringComparison.OrdinalIgnoreCase))
            {
                model.Length = null;
                model.Width = null;
                model.Height = null;
            }

            // --------------------------------------------------------
            // Update entity
            // --------------------------------------------------------
            entity.ItemName = itemName;

            entity.ItemGroupId = model.ItemGroupId;

            entity.HsnCode = string.IsNullOrWhiteSpace(model.HsnCode)
                ? null
                : model.HsnCode.Trim();

            entity.UnitPrice = model.UnitPrice;

            entity.CustomerOrSupplier = customerOrSupplier;

            // Effective date range
            entity.EffectiveFrom = model.EffectiveFrom.Date;
            entity.EffectiveTo = model.EffectiveTo.Date;

            entity.Uom = model.Uom.Trim().ToUpper();

            entity.WeightPerUnit = model.WeightPerUnit;
            entity.StuffQuantity = model.StuffQuantity;

            entity.ItemModel = string.IsNullOrWhiteSpace(model.ItemModel)
                ? null
                : model.ItemModel.Trim();

            entity.Usage = string.IsNullOrWhiteSpace(model.Usage)
                ? null
                : model.Usage.Trim();

            entity.Length = model.Length;
            entity.Width = model.Width;
            entity.Height = model.Height;

            entity.Description = string.IsNullOrWhiteSpace(model.Description)
                ? null
                : model.Description.Trim();

            entity.SafetyLevel = model.SafetyLevel;
            entity.ReorderLevel = model.ReorderLevel;

            entity.DangerLevel = string.IsNullOrWhiteSpace(model.DangerLevel)
                ? null
                : model.DangerLevel.Trim();

            entity.ModifiedDate = DateTime.Now;

            // ItemNumber intentionally remains unchanged on update.

            // --------------------------------------------------------
            // Save
            // --------------------------------------------------------
            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        // ============================================================
        // DELETE: api/ItemMaster/{id}
        // ============================================================
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.ItemMasters.FindAsync(id);

            if (entity == null)
            {
                return NotFound(new
                {
                    message = "Item not found"
                });
            }

            _context.ItemMasters.Remove(entity);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Deleted Successfully"
            });
        }
    }
}

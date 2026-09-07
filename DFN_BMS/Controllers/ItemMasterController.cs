using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DFN_BMS.DB;
using DFN_BMS.Models;

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

        // GET: api/ItemMaster/uom-list
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

        // GET: api/ItemMaster
        [HttpGet]
        public async Task<IActionResult> GetAll()
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
                    ItemGroupName = x.ItemGroup != null ? x.ItemGroup.GroupName : null,
                    x.HsnCode,
                    x.UnitPrice,
                    x.CustomerOrSupplier,
                    x.EffectiveDate,
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

        // GET: api/ItemMaster/5
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
                    ItemGroupName = x.ItemGroup != null ? x.ItemGroup.GroupName : null,
                    x.HsnCode,
                    x.UnitPrice,
                    x.CustomerOrSupplier,
                    x.EffectiveDate,
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
                return NotFound(new { message = "Item not found" });

            return Ok(item);
        }

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

        // POST: api/ItemMaster
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ItemMaster model)
        {
            if (model == null)
                return BadRequest(new { message = "Invalid request" });

            if (string.IsNullOrWhiteSpace(model.ItemNumber) ||
                string.IsNullOrWhiteSpace(model.ItemName) ||
                string.IsNullOrWhiteSpace(model.CustomerOrSupplier) ||
                string.IsNullOrWhiteSpace(model.Uom) ||
                model.ItemGroupId <= 0)
            {
                return BadRequest(new
                {
                    message = "Item Number, Item Name, Item Group, Customer / Supplier and UOM are required"
                });
            }

            if (model.UnitPrice <= 0)
                return BadRequest(new { message = "Unit Price must be greater than 0" });

            if (model.EffectiveDate == default)
                return BadRequest(new { message = "Effective Date is required" });

            if (model.EffectiveDate.Date < DateTime.Today)
                return BadRequest(new { message = "Effective Date cannot be a past date" });

            var customerOrSupplier = model.CustomerOrSupplier.Trim();

            if (!string.Equals(customerOrSupplier, "Customer", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(customerOrSupplier, "Supplier", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    message = "Customer / Supplier must be Customer or Supplier"
                });
            }

            customerOrSupplier =
                string.Equals(customerOrSupplier, "Customer", StringComparison.OrdinalIgnoreCase)
                    ? "Customer"
                    : "Supplier";

            var groupExists = await _context.ItemGroupMasters
                .AnyAsync(g => g.Id == model.ItemGroupId);

            if (!groupExists)
                return BadRequest(new { message = "Selected Item Group does not exist" });

            var itemNumber = model.ItemNumber.Trim();
            var itemName = model.ItemName.Trim();

            var numberExists = await _context.ItemMasters
                .AnyAsync(x => x.ItemNumber.ToLower() == itemNumber.ToLower());

            if (numberExists)
                return BadRequest(new { message = "Item Number already exists" });

            var nameExists = await _context.ItemMasters
                .AnyAsync(x => x.ItemName.ToLower() == itemName.ToLower());

            if (nameExists)
                return BadRequest(new { message = "Item Name already exists" });

            await RegisterUomIfNewAsync(model);

            // Dimensions are meaningful only for PCS.
            if (!string.Equals(model.Uom, "PCS", StringComparison.OrdinalIgnoreCase))
            {
                model.Length = null;
                model.Width = null;
                model.Height = null;
            }

            var entity = new ItemMaster
            {
                ItemNumber = itemNumber,
                ItemName = itemName,
                ItemGroupId = model.ItemGroupId,
                HsnCode = string.IsNullOrWhiteSpace(model.HsnCode) ? null : model.HsnCode.Trim(),
                UnitPrice = model.UnitPrice,
                CustomerOrSupplier = customerOrSupplier,
                EffectiveDate = model.EffectiveDate.Date,
                Uom = model.Uom.Trim().ToUpper(),
                WeightPerUnit = model.WeightPerUnit,
                StuffQuantity = model.StuffQuantity,
                ItemModel = string.IsNullOrWhiteSpace(model.ItemModel) ? null : model.ItemModel.Trim(),
                Usage = string.IsNullOrWhiteSpace(model.Usage) ? null : model.Usage.Trim(),
                Length = model.Length,
                Width = model.Width,
                Height = model.Height,
                Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
                SafetyLevel = model.SafetyLevel,
                ReorderLevel = model.ReorderLevel,
                DangerLevel = string.IsNullOrWhiteSpace(model.DangerLevel) ? null : model.DangerLevel.Trim(),
                CreatedDate = DateTime.Now
            };

            _context.ItemMasters.Add(entity);
            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        // PUT: api/ItemMaster/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] ItemMaster model)
        {
            if (model == null)
                return BadRequest(new { message = "Invalid request" });

            var entity = await _context.ItemMasters.FindAsync(id);

            if (entity == null)
                return NotFound(new { message = "Item not found" });

            if (string.IsNullOrWhiteSpace(model.ItemName) ||
                string.IsNullOrWhiteSpace(model.CustomerOrSupplier) ||
                string.IsNullOrWhiteSpace(model.Uom) ||
                model.ItemGroupId <= 0)
            {
                return BadRequest(new
                {
                    message = "Item Name, Item Group, Customer / Supplier and UOM are required"
                });
            }

            if (model.UnitPrice <= 0)
                return BadRequest(new { message = "Unit Price must be greater than 0" });

            if (model.EffectiveDate == default)
                return BadRequest(new { message = "Effective Date is required" });

            if (model.EffectiveDate.Date < DateTime.Today)
                return BadRequest(new { message = "Effective Date cannot be a past date" });

            var customerOrSupplier = model.CustomerOrSupplier.Trim();

            if (!string.Equals(customerOrSupplier, "Customer", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(customerOrSupplier, "Supplier", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    message = "Customer / Supplier must be Customer or Supplier"
                });
            }

            customerOrSupplier =
                string.Equals(customerOrSupplier, "Customer", StringComparison.OrdinalIgnoreCase)
                    ? "Customer"
                    : "Supplier";

            var groupExists = await _context.ItemGroupMasters
                .AnyAsync(g => g.Id == model.ItemGroupId);

            if (!groupExists)
                return BadRequest(new { message = "Selected Item Group does not exist" });

            var itemName = model.ItemName.Trim();

            var nameExists = await _context.ItemMasters
                .AnyAsync(x =>
                    x.ItemName.ToLower() == itemName.ToLower() &&
                    x.Id != id);

            if (nameExists)
                return BadRequest(new { message = "Item Name already exists" });

            await RegisterUomIfNewAsync(model);

            if (!string.Equals(model.Uom, "PCS", StringComparison.OrdinalIgnoreCase))
            {
                model.Length = null;
                model.Width = null;
                model.Height = null;
            }

            entity.ItemName = itemName;
            entity.ItemGroupId = model.ItemGroupId;
            entity.HsnCode = string.IsNullOrWhiteSpace(model.HsnCode) ? null : model.HsnCode.Trim();
            entity.UnitPrice = model.UnitPrice;
            entity.CustomerOrSupplier = customerOrSupplier;
            entity.EffectiveDate = model.EffectiveDate.Date;
            entity.Uom = model.Uom.Trim().ToUpper();
            entity.WeightPerUnit = model.WeightPerUnit;
            entity.StuffQuantity = model.StuffQuantity;
            entity.ItemModel = string.IsNullOrWhiteSpace(model.ItemModel) ? null : model.ItemModel.Trim();
            entity.Usage = string.IsNullOrWhiteSpace(model.Usage) ? null : model.Usage.Trim();
            entity.Length = model.Length;
            entity.Width = model.Width;
            entity.Height = model.Height;
            entity.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
            entity.SafetyLevel = model.SafetyLevel;
            entity.ReorderLevel = model.ReorderLevel;
            entity.DangerLevel = string.IsNullOrWhiteSpace(model.DangerLevel) ? null : model.DangerLevel.Trim();
            entity.ModifiedDate = DateTime.Now;

            // ItemNumber intentionally remains unchanged on update.

            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        // DELETE: api/ItemMaster/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.ItemMasters.FindAsync(id);

            if (entity == null)
                return NotFound(new { message = "Item not found" });

            _context.ItemMasters.Remove(entity);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Deleted Successfully" });
        }
    }
}

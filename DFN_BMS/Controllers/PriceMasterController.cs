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
    public class PriceMasterController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PriceMasterController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/PriceMaster/parts-list
        // Feeds the Part Number dropdown on the Price Master form.
        // The frontend uses the label (which already contains the
        // Part Name) to auto-fill Part Name on selection — no extra
        // round-trip needed.
        [HttpGet("parts-list")]
        public async Task<IActionResult> GetPartsList()
        {
            var data = await _context.ItemMasters
                .Select(x => new
                {
                    value = x.Id,
                    partNumber = x.ItemNumber,
                    partName = x.ItemName,
                    label = $"{x.ItemNumber} - {x.ItemName}"
                })
                .OrderBy(x => x.partNumber)
                .ToListAsync();

            return Ok(data);
        }

        // GET: api/PriceMaster
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await _context.PriceMasters
                .Include(x => x.PartNumber)
                .OrderByDescending(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.PartNumberId,
                    PartNumberCode = x.PartNumber.ItemNumber,
                    PartName = x.PartNumber.ItemName,
                    x.CustomerOrSupplier,
                    x.Rate,
                    x.EffectiveDate
                })
                .ToListAsync();

            return Ok(list);
        }

        // GET: api/PriceMaster/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.PriceMasters
                .Include(x => x.PartNumber)
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,
                    x.PartNumberId,
                    PartNumberCode = x.PartNumber.ItemNumber,
                    PartName = x.PartNumber.ItemName,
                    x.CustomerOrSupplier,
                    x.Rate,
                    x.EffectiveDate
                })
                .FirstOrDefaultAsync();

            if (item == null)
                return NotFound(new { message = "Price record not found" });

            return Ok(item);
        }

        // POST: api/PriceMaster
        // POST: api/PriceMaster
        // POST: api/PriceMaster
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PriceMaster model)
        {
            if (model.PartNumberId <= 0 ||
                string.IsNullOrWhiteSpace(model.CustomerOrSupplier) ||
                model.Rate <= 0)
            {
                return BadRequest(new
                {
                    message = "Part Number, Customer/Supplier and Rate are required"
                });
            }

            var partExists = await _context.ItemMasters
                .AnyAsync(x => x.Id == model.PartNumberId);

            if (!partExists)
            {
                return BadRequest(new
                {
                    message = "Selected Part Number does not exist"
                });
            }

            var customerOrSupplier = model.CustomerOrSupplier.Trim();

            // Only ONE record allowed for:
            // Part Number + Customer/Supplier
            var existingRecord = await _context.PriceMasters
                .FirstOrDefaultAsync(x =>
                    x.PartNumberId == model.PartNumberId &&
                    x.CustomerOrSupplier == customerOrSupplier
                );

            if (existingRecord != null)
            {
                return Conflict(new
                {
                    message = "Price already exists for this Part Number and Customer/Supplier. Please edit the existing record."
                });
            }

            var entity = new PriceMaster
            {
                PartNumberId = model.PartNumberId,
                CustomerOrSupplier = customerOrSupplier,
                Rate = model.Rate,
                EffectiveDate = model.EffectiveDate == default
                    ? DateTime.Now
                    : model.EffectiveDate,
                CreatedDate = DateTime.Now
            };

            _context.PriceMasters.Add(entity);

            await _context.SaveChangesAsync();

            return Ok(entity);
        }
        // PUT: api/PriceMaster/5
        // PUT: api/PriceMaster/5
        // PUT: api/PriceMaster/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(
            int id,
            [FromBody] PriceMaster model)
        {
            var entity = await _context.PriceMasters
                .FindAsync(id);

            if (entity == null)
            {
                return NotFound(new
                {
                    message = "Price record not found"
                });
            }

            if (model.PartNumberId <= 0 ||
                string.IsNullOrWhiteSpace(model.CustomerOrSupplier) ||
                model.Rate <= 0)
            {
                return BadRequest(new
                {
                    message = "Part Number, Customer/Supplier and Rate are required"
                });
            }

            var partExists = await _context.ItemMasters
                .AnyAsync(x => x.Id == model.PartNumberId);

            if (!partExists)
            {
                return BadRequest(new
                {
                    message = "Selected Part Number does not exist"
                });
            }

            var customerOrSupplier = model.CustomerOrSupplier.Trim();

            // Check whether another record already exists
            // for the same Part Number + Customer/Supplier
            var duplicateExists = await _context.PriceMasters
                .AnyAsync(x =>
                    x.Id != id &&
                    x.PartNumberId == model.PartNumberId &&
                    x.CustomerOrSupplier == customerOrSupplier
                );

            if (duplicateExists)
            {
                return Conflict(new
                {
                    message = "Another price record already exists for this Part Number and Customer/Supplier."
                });
            }

            // Update existing record
            entity.PartNumberId = model.PartNumberId;
            entity.CustomerOrSupplier = customerOrSupplier;
            entity.Rate = model.Rate;
            entity.EffectiveDate = model.EffectiveDate;
            entity.ModifiedDate = DateTime.Now;

            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        // DELETE: api/PriceMaster/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.PriceMasters.FindAsync(id);

            if (entity == null)
                return NotFound(new { message = "Price record not found" });

            _context.PriceMasters.Remove(entity);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Deleted Successfully" });
        }
    }
}
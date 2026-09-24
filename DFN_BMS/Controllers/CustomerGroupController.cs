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
    public class CustomerGroupController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CustomerGroupController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await _context.CustomerGroupMasters
                .OrderByDescending(x => x.Id)
                .ToListAsync();

            return Ok(list);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.CustomerGroupMasters.FindAsync(id);

            if (item == null)
                return NotFound(new { message = "Customer Group not found" });

            return Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CustomerGroupMaster model)
        {
            if (model == null)
                return BadRequest(new { message = "Invalid Customer Group data" });

            if (string.IsNullOrWhiteSpace(model.CustomerGroupType))
                return BadRequest(new { message = "Customer Group Type is required" });

            var customerGroupType = model.CustomerGroupType.Trim();
            var description = model.Description?.Trim() ?? string.Empty;

            // Same Type is allowed when Description is different.
            // Same Type + same Description is not allowed.
            var combinationExists = await _context.CustomerGroupMasters
                .AnyAsync(x =>
                    x.CustomerGroupType != null &&
                    x.CustomerGroupType.Trim().ToLower() == customerGroupType.ToLower() &&
                    (x.Description ?? "").Trim().ToLower() == description.ToLower());

            if (combinationExists)
            {
                return BadRequest(new
                {
                    message = string.IsNullOrWhiteSpace(description)
                        ? "Customer Group Type already exists"
                        : "Customer Group Type with this Description already exists"
                });
            }

            // GST is applicable only for External.
            var hasGST = customerGroupType.Equals(
                "External",
                StringComparison.OrdinalIgnoreCase)
                && model.HasGST;

            var entity = new CustomerGroupMaster
            {
                CustomerGroupType = customerGroupType,
                Description = string.IsNullOrWhiteSpace(description)
                    ? null
                    : description,
                HasGST = hasGST,
                IsActive = true,
                CreatedDate = DateTime.Now
            };

            _context.CustomerGroupMasters.Add(entity);
            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(
            int id,
            [FromBody] CustomerGroupMaster model)
        {
            if (model == null)
                return BadRequest(new { message = "Invalid Customer Group data" });

            var entity =
                await _context.CustomerGroupMasters.FindAsync(id);

            if (entity == null)
                return NotFound(new { message = "Customer Group not found" });

            if (string.IsNullOrWhiteSpace(model.CustomerGroupType))
                return BadRequest(new { message = "Customer Group Type is required" });

            var customerGroupType = model.CustomerGroupType.Trim();
            var description = model.Description?.Trim() ?? string.Empty;

            var combinationExists = await _context.CustomerGroupMasters
                .AnyAsync(x =>
                    x.Id != id &&
                    x.CustomerGroupType != null &&
                    x.CustomerGroupType.Trim().ToLower() == customerGroupType.ToLower() &&
                    (x.Description ?? "").Trim().ToLower() == description.ToLower());

            if (combinationExists)
            {
                return BadRequest(new
                {
                    message = string.IsNullOrWhiteSpace(description)
                        ? "Customer Group Type already exists"
                        : "Customer Group Type with this Description already exists"
                });
            }

            // GST is applicable only for External.
            var hasGST = customerGroupType.Equals(
                "External",
                StringComparison.OrdinalIgnoreCase)
                && model.HasGST;

            entity.CustomerGroupType = customerGroupType;
            entity.Description = string.IsNullOrWhiteSpace(description)
                ? null
                : description;
            entity.HasGST = hasGST;
            entity.ModifiedDate = DateTime.Now;

            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity =
                await _context.CustomerGroupMasters.FindAsync(id);

            if (entity == null)
            {
                return NotFound(new
                {
                    message = "Customer Group not found"
                });
            }

            var isUsed = await _context.CustomerMasters
                .AnyAsync(x => x.CustomerGroupId == id);

            if (isUsed)
            {
                return BadRequest(new
                {
                    message =
                        "This Customer Group cannot be deleted because it is already used by a Customer."
                });
            }

            _context.CustomerGroupMasters.Remove(entity);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Deleted Successfully"
            });
        }
    }
}

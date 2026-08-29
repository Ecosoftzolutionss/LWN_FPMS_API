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
    public class ItemGroupController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ItemGroupController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/ItemGroup
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await _context.ItemGroupMasters
                .OrderByDescending(x => x.Id)
                .ToListAsync();

            return Ok(list);
        }

        // GET: api/ItemGroup/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.ItemGroupMasters.FindAsync(id);

            if (item == null)
                return NotFound(new { message = "Item Group not found" });

            return Ok(item);
        }

        // POST: api/ItemGroup
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ItemGroupMaster model)
        {
            if (string.IsNullOrWhiteSpace(model.GroupName))
                return BadRequest(new { message = "Group Name is required" });

            var nameExists = await _context.ItemGroupMasters
                .AnyAsync(x => x.GroupName.ToLower() == model.GroupName.Trim().ToLower());

            if (nameExists)
                return BadRequest(new { message = "Group Name already exists" });

            var entity = new ItemGroupMaster
            {
                GroupName = model.GroupName.Trim(),
                Description = model.Description?.Trim(),
                IsActive = model.IsActive,
                CreatedDate = DateTime.Now
            };

            _context.ItemGroupMasters.Add(entity);
            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        // PUT: api/ItemGroup/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] ItemGroupMaster model)
        {
            var entity = await _context.ItemGroupMasters.FindAsync(id);

            if (entity == null)
                return NotFound(new { message = "Item Group not found" });

            if (string.IsNullOrWhiteSpace(model.GroupName))
                return BadRequest(new { message = "Group Name is required" });

            var nameExists = await _context.ItemGroupMasters
                .AnyAsync(x => x.GroupName.ToLower() == model.GroupName.Trim().ToLower() && x.Id != id);

            if (nameExists)
                return BadRequest(new { message = "Group Name already exists" });

           
            entity.GroupName = model.GroupName.Trim();
            entity.Description = model.Description?.Trim();
            entity.IsActive = model.IsActive;
            entity.ModifiedDate = DateTime.Now;

            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        // DELETE: api/ItemGroup/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.ItemGroupMasters.FindAsync(id);

            if (entity == null)
                return NotFound(new { message = "Item Group not found" });

            // Check whether this Item Group is already used
            var isUsed = await _context.ItemMasters
                .AnyAsync(x => x.ItemGroupId == id);

            if (isUsed)
            {
                return BadRequest(new
                {
                    message = "This Item Group cannot be deleted because it is already used by an Item."
                });
            }

            _context.ItemGroupMasters.Remove(entity);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Deleted Successfully" });
        }
    }
}
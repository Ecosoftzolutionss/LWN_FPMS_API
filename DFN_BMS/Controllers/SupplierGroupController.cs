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
    public class SupplierGroupController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SupplierGroupController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // GET ALL
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await _context.SupplierGroupMasters
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
            var item = await _context.SupplierGroupMasters
                .FindAsync(id);

            if (item == null)
            {
                return NotFound(new
                {
                    message = "Supplier Group not found"
                });
            }

            return Ok(item);
        }

        // =========================================================
        // CREATE
        // =========================================================

        [HttpPost]
        public async Task<IActionResult> Create(
            [FromBody] SupplierGroupMaster model)
        {
            if (model == null)
            {
                return BadRequest(new
                {
                    message = "Invalid supplier group data"
                });
            }

            if (string.IsNullOrWhiteSpace(
                model.SupplierGroupType))
            {
                return BadRequest(new
                {
                    message =
                        "Supplier Group Type is required"
                });
            }

            var type =
                model.SupplierGroupType.Trim();

            var description =
                model.Description?.Trim() ?? string.Empty;

            // -----------------------------------------------------
            // Supplier Group Type can repeat.
            //
            // Type + Description must be unique.
            //
            // Example:
            // Packaging + Test  -> allowed
            // Packaging + Test2 -> allowed
            // Packaging + Test  -> duplicate
            // -----------------------------------------------------

            var combinationExists =
                await _context.SupplierGroupMasters
                    .AnyAsync(x =>
                        x.SupplierGroupType != null &&
                        x.SupplierGroupType.Trim().ToLower()
                            == type.ToLower() &&
                        (x.Description ?? string.Empty)
                            .Trim()
                            .ToLower()
                            == description.ToLower());

            if (combinationExists)
            {
                return BadRequest(new
                {
                    message = string.IsNullOrWhiteSpace(
                        description)
                        ? "Supplier Group Type already exists"
                        : "Supplier Group Type with this Description already exists"
                });
            }

            var entity = new SupplierGroupMaster
            {
                SupplierGroupType = type,

                Description =
                    string.IsNullOrWhiteSpace(description)
                        ? null
                        : description,

                RequiresPan =
                    model.RequiresPan,

                RequiresGst =
                    model.RequiresGst,

                IsActive = true,

                CreatedDate = DateTime.Now
            };

            _context.SupplierGroupMasters.Add(entity);

            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        // =========================================================
        // UPDATE
        // =========================================================

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(
            int id,
            [FromBody] SupplierGroupMaster model)
        {
            if (model == null)
            {
                return BadRequest(new
                {
                    message = "Invalid supplier group data"
                });
            }

            var entity =
                await _context.SupplierGroupMasters
                    .FindAsync(id);

            if (entity == null)
            {
                return NotFound(new
                {
                    message =
                        "Supplier Group not found"
                });
            }

            if (string.IsNullOrWhiteSpace(
                model.SupplierGroupType))
            {
                return BadRequest(new
                {
                    message =
                        "Supplier Group Type is required"
                });
            }

            var type =
                model.SupplierGroupType.Trim();

            var description =
                model.Description?.Trim() ?? string.Empty;

            // Ignore the current record while checking duplicate.
            var combinationExists =
                await _context.SupplierGroupMasters
                    .AnyAsync(x =>
                        x.Id != id &&
                        x.SupplierGroupType != null &&
                        x.SupplierGroupType.Trim().ToLower()
                            == type.ToLower() &&
                        (x.Description ?? string.Empty)
                            .Trim()
                            .ToLower()
                            == description.ToLower());

            if (combinationExists)
            {
                return BadRequest(new
                {
                    message = string.IsNullOrWhiteSpace(
                        description)
                        ? "Supplier Group Type already exists"
                        : "Supplier Group Type with this Description already exists"
                });
            }

            entity.SupplierGroupType = type;

            entity.Description =
                string.IsNullOrWhiteSpace(description)
                    ? null
                    : description;

            entity.RequiresPan =
                model.RequiresPan;

            entity.RequiresGst =
                model.RequiresGst;

            entity.ModifiedDate =
                DateTime.Now;

            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        // =========================================================
        // DELETE
        // =========================================================

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var entity =
                    await _context.SupplierGroupMasters
                        .FindAsync(id);

                if (entity == null)
                {
                    return NotFound(new
                    {
                        message =
                            "Supplier Group not found"
                    });
                }

                // One Supplier Group can be used by
                // multiple suppliers.
                //
                // Therefore the group itself cannot be
                // deleted while at least one supplier uses it.

                var usedInSupplierMaster =
                    await _context.SupplierMasters
                        .AnyAsync(x =>
                            x.SupplierGroupId == id);

                if (usedInSupplierMaster)
                {
                    return BadRequest(new
                    {
                        message =
                            "This Supplier Group cannot be deleted because it is already used in Supplier Master."
                    });
                }

                _context.SupplierGroupMasters
                    .Remove(entity);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message =
                        "Supplier Group deleted successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message =
                        "Failed to delete Supplier Group",

                    error =
                        ex.InnerException?.Message
                        ?? ex.Message
                });
            }
        }
    }
}

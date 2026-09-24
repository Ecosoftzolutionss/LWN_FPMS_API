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
    public class LocationMasterController : ControllerBase
    {
        private readonly AppDbContext _context;

        public LocationMasterController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await _context.LocationMasters
                .Include(x => x.StoreMaster)
                .Include(x => x.Racks)
                .OrderByDescending(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.StoreCode,
                    x.StoreMasterId,
                    StoreLocation = x.StoreMaster.StoreLocation,
                    PalletNumber = x.StoreMaster.PalletNumber,
                    PartNumber = x.StoreMaster.PartNumber,
                    ColourCode = x.StoreMaster.ColourCode,
                    RackCount = x.Racks.Count
                })
                .ToListAsync();

            return Ok(list);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var store = await _context.LocationMasters
                .Include(x => x.StoreMaster)
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,
                    x.StoreCode,
                    x.StoreMasterId,
                    StoreLocation = x.StoreMaster.StoreLocation,
                    PalletNumber = x.StoreMaster.PalletNumber,
                    PartNumber = x.StoreMaster.PartNumber,
                    ColourCode = x.StoreMaster.ColourCode
                })
                .FirstOrDefaultAsync();

            if (store == null)
                return NotFound(new { message = "Store not found" });

            return Ok(store);
        }

        [HttpGet("next-code")]
        public async Task<IActionResult> GetNextCode()
        {
            var storeCode = await GenerateStoreCodeAsync();
            return Ok(new { storeCode });
        }

        private async Task<string> GenerateStoreCodeAsync()
        {
            var last = await _context.LocationMasters
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            int nextSeq = 1;

            if (last != null && last.StoreCode.StartsWith("ST"))
            {
                if (int.TryParse(last.StoreCode.Substring(2), out int lastSeq))
                    nextSeq = lastSeq + 1;
            }

            return $"ST{nextSeq:D6}";
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] LocationMaster model)
        {
            if (model.StoreMasterId <= 0)
                return BadRequest(new { message = "Store Name is required" });

            var storeMasterExists = await _context.StoreMasters.AnyAsync(x => x.Id == model.StoreMasterId);
            if (!storeMasterExists)
                return BadRequest(new { message = "Selected Store does not exist" });

            var entity = new LocationMaster
            {
                StoreCode = await GenerateStoreCodeAsync(),
                StoreMasterId = model.StoreMasterId,
                CreatedDate = DateTime.Now
            };

            _context.LocationMasters.Add(entity);
            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] LocationMaster model)
        {
            var entity = await _context.LocationMasters.FindAsync(id);

            if (entity == null)
                return NotFound(new { message = "Store not found" });

            if (model.StoreMasterId <= 0)
                return BadRequest(new { message = "Store Name is required" });

            var storeMasterExists = await _context.StoreMasters.AnyAsync(x => x.Id == model.StoreMasterId);
            if (!storeMasterExists)
                return BadRequest(new { message = "Selected Store does not exist" });

            entity.StoreMasterId = model.StoreMasterId;
            entity.ModifiedDate = DateTime.Now;

            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.LocationMasters.FindAsync(id);

            if (entity == null)
                return NotFound(new { message = "Store not found" });

            _context.LocationMasters.Remove(entity);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Deleted Successfully" });
        }
    }
}

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
    public class StoreMasterController : ControllerBase
    {
        private readonly AppDbContext _context;

        public StoreMasterController(AppDbContext context)
        {
            _context = context;
        }


        // ============================================================
        // GET: api/StoreMaster/configured-parts
        // ============================================================

        [HttpGet("configured-parts")]
        public async Task<IActionResult> GetConfiguredParts()
        {
            var data = await _context.StoreMasters
                .Where(x => x.PartNumberId.HasValue)
                .Include(x => x.PartNumber)
                .Select(x => new
                {
                    id = x.PartNumber.Id,
                    itemNumber = x.PartNumber.ItemNumber,
                    itemName = x.PartNumber.ItemName,
                    uom = x.PartNumber.Uom
                })
                .Distinct()
                .OrderBy(x => x.itemNumber)
                .ToListAsync();

            return Ok(data);
        }


        // ============================================================
        // GET: api/StoreMaster/parts-list
        // ============================================================

        [HttpGet("parts-list")]
        public async Task<IActionResult> GetPartsList()
        {
            var raw = await _context.ItemMasters
                .Select(x => new
                {
                    x.Id,
                    x.ItemNumber,
                    x.ItemName
                })
                .ToListAsync();

            var data = raw
                .Select(x => new
                {
                    value = x.Id,
                    label = $"{x.ItemNumber} - {x.ItemName}"
                })
                .OrderBy(x => x.label)
                .ToList();

            return Ok(data);
        }


        // ============================================================
        // GET: api/StoreMaster/pallet-types
        //
        // Returns Pallet Type + Colour
        // Colour is used by frontend only for display.
        // ============================================================

        [HttpGet("pallet-types")]
        public async Task<IActionResult> GetPalletTypes()
        {
            var data = await _context.PalletTypeMasters
                .Select(x => new
                {
                    value = x.Id,

                    palletName = x.PalletName,

                    colourCode = x.ColourCode,

                    currentSequence = x.CurrentSequence,

                    rangeFrom = x.RangeFrom,

                    rangeTo = x.RangeTo
                })
                .OrderBy(x => x.palletName)
                .ToListAsync();

            return Ok(data);
        }


        // ============================================================
        // GET: api/StoreMaster
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await _context.StoreMasters
                .Include(x => x.PalletType)
                .Include(x => x.PartNumber)

                .OrderByDescending(x => x.Id)

                .Select(x => new
                {
                    x.Id,

                    x.StoreLocation,

                    x.PalletTypeId,

                    PalletTypeName =
                        x.PalletType != null
                            ? x.PalletType.PalletName
                            : null,

                    x.PalletNumber,

                    /*
                     * Colour comes from STORE_MASTER.
                     *
                     * It is used only for the colour square
                     * in the grid.
                     */

                    x.ColourCode,

                    x.PartNumberId,

                    PartNumberCode =
                        x.PartNumber != null
                            ? x.PartNumber.ItemNumber
                            : null,

                    PartName =
                        x.PartNumber != null
                            ? x.PartNumber.ItemName
                            : null
                })

                .ToListAsync();

            return Ok(list);
        }


        // ============================================================
        // GET: api/StoreMaster/5
        // ============================================================

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.StoreMasters

                .Include(x => x.PalletType)

                .Include(x => x.PartNumber)

                .Where(x => x.Id == id)

                .Select(x => new
                {
                    x.Id,

                    x.StoreLocation,

                    x.PalletTypeId,

                    PalletTypeName =
                        x.PalletType != null
                            ? x.PalletType.PalletName
                            : null,

                    x.PalletNumber,

                    x.ColourCode,

                    x.PartNumberId,

                    PartNumberCode =
                        x.PartNumber != null
                            ? x.PartNumber.ItemNumber
                            : null,

                    PartName =
                        x.PartNumber != null
                            ? x.PartNumber.ItemName
                            : null
                })

                .FirstOrDefaultAsync();

            if (item == null)
            {
                return NotFound(
                    new
                    {
                        message =
                            "Store record not found"
                    });
            }

            return Ok(item);
        }


        // ============================================================
        // Generate Pallet Number
        // ============================================================

        private async Task<string?> GeneratePalletNumberAsync(
            int palletTypeId)
        {
            var palletType =
                await _context.PalletTypeMasters
                    .FirstOrDefaultAsync(
                        x => x.Id == palletTypeId);

            if (palletType == null)
            {
                return null;
            }


            // -----------------------------------------------
            // Determine next sequence
            // -----------------------------------------------

            var nextSeq =
                palletType.CurrentSequence == 0
                    ? palletType.RangeFrom
                    : palletType.CurrentSequence + 1;


            // -----------------------------------------------
            // Check range
            // -----------------------------------------------

            if (nextSeq > palletType.RangeTo)
            {
                return null;
            }


            // -----------------------------------------------
            // Update sequence
            // -----------------------------------------------

            palletType.CurrentSequence =
                nextSeq;


            // -----------------------------------------------
            // Generate prefix
            // -----------------------------------------------

            var prefix =
                palletType.PalletName.Length >= 2

                    ? palletType.PalletName
                        .Substring(0, 2)
                        .ToUpper()

                    : palletType.PalletName
                        .ToUpper();


            return $"{prefix}-{nextSeq:D2}";
        }


        // ============================================================
        // POST: api/StoreMaster
        // ============================================================

        [HttpPost]
        public async Task<IActionResult> Create(
            [FromBody] StoreMaster model)
        {
            // -----------------------------------------------
            // Basic validation
            // -----------------------------------------------

            if (string.IsNullOrWhiteSpace(
                    model.StoreLocation))
            {
                return BadRequest(
                    new
                    {
                        message =
                            "Store Location is required"
                    });
            }


            if (model.PalletTypeId <= 0)
            {
                return BadRequest(
                    new
                    {
                        message =
                            "Pallet Type is required"
                    });
            }


            // -----------------------------------------------
            // Get selected pallet type
            // -----------------------------------------------

            var palletType =
                await _context.PalletTypeMasters
                    .FirstOrDefaultAsync(
                        x => x.Id ==
                             model.PalletTypeId);


            if (palletType == null)
            {
                return BadRequest(
                    new
                    {
                        message =
                            "Selected Pallet Type does not exist"
                    });
            }


            // -----------------------------------------------
            // Validate colour configured
            // -----------------------------------------------

            if (string.IsNullOrWhiteSpace(
                    palletType.ColourCode))
            {
                return BadRequest(
                    new
                    {
                        message =
                            "Colour is not configured for the selected Pallet Type."
                    });
            }


            // -----------------------------------------------
            // Validate Part Number
            // -----------------------------------------------

            if (model.PartNumberId.HasValue)
            {
                var partExists =
                    await _context.ItemMasters
                        .AnyAsync(
                            x => x.Id ==
                                 model.PartNumberId.Value);

                if (!partExists)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "Selected Part Number does not exist"
                        });
                }
            }


            // -----------------------------------------------
            // Generate pallet number
            // -----------------------------------------------

            var palletNumber =
                await GeneratePalletNumberAsync(
                    model.PalletTypeId);


            if (palletNumber == null)
            {
                return BadRequest(
                    new
                    {
                        message =
                            "Pallet number range is completed for the selected Pallet Type."
                    });
            }


            // -----------------------------------------------
            // Create entity
            // -----------------------------------------------

            var entity = new StoreMaster
            {
                StoreLocation =
                    model.StoreLocation.Trim(),

                PalletTypeId =
                    model.PalletTypeId,

                PalletNumber =
                    palletNumber,

                /*
                 * IMPORTANT:
                 *
                 * Colour is NOT received from frontend.
                 *
                 * It is taken directly from
                 * PALLET_TYPE_MASTER.
                 */

                ColourCode =
                    palletType.ColourCode.Trim(),

                PartNumberId =
                    model.PartNumberId,

                CreatedDate =
                    DateTime.Now
            };


            _context.StoreMasters.Add(entity);

            await _context.SaveChangesAsync();


            // -----------------------------------------------
            // Return response
            // -----------------------------------------------

            return Ok(
                new
                {
                    id = entity.Id,

                    storeLocation =
                        entity.StoreLocation,

                    palletTypeId =
                        entity.PalletTypeId,

                    palletNumber =
                        entity.PalletNumber,

                    colourCode =
                        entity.ColourCode,

                    partNumberId =
                        entity.PartNumberId,

                    message =
                        "Store Saved Successfully"
                });
        }


        // ============================================================
        // PUT: api/StoreMaster/5
        // ============================================================

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(
            int id,
            [FromBody] StoreMaster model)
        {
            // -----------------------------------------------
            // Find existing store
            // -----------------------------------------------

            var entity =
                await _context.StoreMasters
                    .FirstOrDefaultAsync(
                        x => x.Id == id);


            if (entity == null)
            {
                return NotFound(
                    new
                    {
                        message =
                            "Store record not found"
                    });
            }


            // -----------------------------------------------
            // Validate location
            // -----------------------------------------------

            if (string.IsNullOrWhiteSpace(
                    model.StoreLocation))
            {
                return BadRequest(
                    new
                    {
                        message =
                            "Store Location is required"
                    });
            }


            // -----------------------------------------------
            // Part Number validation
            // -----------------------------------------------

            if (model.PartNumberId.HasValue)
            {
                var partExists =
                    await _context.ItemMasters
                        .AnyAsync(
                            x => x.Id ==
                                 model.PartNumberId.Value);

                if (!partExists)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "Selected Part Number does not exist"
                        });
                }
            }


            // -----------------------------------------------
            // Update fields
            // -----------------------------------------------

            entity.StoreLocation =
                model.StoreLocation.Trim();

            entity.PartNumberId =
                model.PartNumberId;

            /*
             * PalletTypeId is intentionally NOT changed.
             *
             * Pallet Number and Colour remain associated
             * with the original pallet type.
             */

            entity.ModifiedDate =
                DateTime.Now;


            await _context.SaveChangesAsync();


            return Ok(
                new
                {
                    id = entity.Id,

                    storeLocation =
                        entity.StoreLocation,

                    palletTypeId =
                        entity.PalletTypeId,

                    palletNumber =
                        entity.PalletNumber,

                    colourCode =
                        entity.ColourCode,

                    partNumberId =
                        entity.PartNumberId,

                    message =
                        "Store Updated Successfully"
                });
        }


        // ============================================================
        // DELETE: api/StoreMaster/5
        // ============================================================

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity =
                await _context.StoreMasters
                    .FirstOrDefaultAsync(
                        x => x.Id == id);


            if (entity == null)
            {
                return NotFound(
                    new
                    {
                        message =
                            "Store record not found"
                    });
            }


            _context.StoreMasters.Remove(
                entity);


            await _context.SaveChangesAsync();


            return Ok(
                new
                {
                    message =
                        "Deleted Successfully"
                });
        }
    }
}
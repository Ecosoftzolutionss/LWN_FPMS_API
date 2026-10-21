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

        // Default pallet number range
        private const int DEFAULT_RANGE_FROM = 1;
        private const int DEFAULT_RANGE_TO = 70;

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
                    id = x.PartNumber!.Id,
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
        // Used by frontend Pallet Type dropdown.
        // Existing pallet types are shown here.
        // New pallet types can also be created directly
        // from Pallet Master frontend.
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
                    id = x.Id,

                    storeLocation =
                        x.StoreLocation,

                    palletTypeId =
                        x.PalletTypeId,

                    palletTypeName =
                        x.PalletType != null
                            ? x.PalletType.PalletName
                            : null,

                    palletNumber =
                        x.PalletNumber,

                    colourCode =
                        x.ColourCode,

                    partNumberId =
                        x.PartNumberId,

                    partNumberCode =
                        x.PartNumber != null
                            ? x.PartNumber.ItemNumber
                            : null,

                    partName =
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

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _context.StoreMasters
                .Include(x => x.PalletType)
                .Include(x => x.PartNumber)
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    id = x.Id,

                    storeLocation =
                        x.StoreLocation,

                    palletTypeId =
                        x.PalletTypeId,

                    palletTypeName =
                        x.PalletType != null
                            ? x.PalletType.PalletName
                            : null,

                    palletNumber =
                        x.PalletNumber,

                    colourCode =
                        x.ColourCode,

                    partNumberId =
                        x.PartNumberId,

                    partNumberCode =
                        x.PartNumber != null
                            ? x.PartNumber.ItemNumber
                            : null,

                    partName =
                        x.PartNumber != null
                            ? x.PartNumber.ItemName
                            : null
                })
                .FirstOrDefaultAsync();

            if (item == null)
            {
                return NotFound(new
                {
                    message = "Store record not found"
                });
            }

            return Ok(item);
        }


        // ============================================================
        // Find or Create Pallet Type
        //
        // If pallet type already exists:
        //     Use existing PalletTypeMaster
        //
        // If pallet type does not exist:
        //     Create new PalletTypeMaster
        //
        // Example:
        //     User types TRH
        //
        // Creates:
        //
        //     Id = new
        //     PalletName = TRH
        //     RangeFrom = 1
        //     RangeTo = 70
        //     CurrentSequence = 0
        //     ColourCode = selected colour
        // ============================================================

        private async Task<PalletTypeMaster?> GetOrCreatePalletTypeAsync(
            string palletTypeName,
            string colourCode)
        {
            palletTypeName =
                palletTypeName.Trim().ToUpper();

            // --------------------------------------------------------
            // Check existing pallet type
            // --------------------------------------------------------

            var existing =
                await _context.PalletTypeMasters
                    .FirstOrDefaultAsync(x =>
                        x.PalletName.ToUpper() ==
                        palletTypeName);

            if (existing != null)
            {
                return existing;
            }


            // --------------------------------------------------------
            // Create new pallet type
            // --------------------------------------------------------

            var palletType =
                new PalletTypeMaster
                {
                    PalletName =
                        palletTypeName,

                    RangeFrom =
                        DEFAULT_RANGE_FROM,

                    RangeTo =
                        DEFAULT_RANGE_TO,

                    CurrentSequence =
                        0,

                    ColourCode =
                        colourCode.Trim().ToUpper()
                };


            _context.PalletTypeMasters.Add(
                palletType
            );


            await _context.SaveChangesAsync();


            return palletType;
        }


        // ============================================================
        // Generate Pallet Number
        //
        // Example:
        //
        // TRH + CurrentSequence 0
        //     ↓
        // TRH-01
        //
        // Next:
        // TRH-02
        //
        // ============================================================

        private async Task<string?> GeneratePalletNumberAsync(
            int palletTypeId)
        {
            var palletType =
                await _context.PalletTypeMasters
                    .FirstOrDefaultAsync(
                        x => x.Id == palletTypeId
                    );

            if (palletType == null)
            {
                return null;
            }


            // --------------------------------------------------------
            // Determine next sequence
            // --------------------------------------------------------

            var nextSeq =
                palletType.CurrentSequence == 0
                    ? palletType.RangeFrom
                    : palletType.CurrentSequence + 1;


            // --------------------------------------------------------
            // Check range
            // --------------------------------------------------------

            if (nextSeq > palletType.RangeTo)
            {
                return null;
            }


            // --------------------------------------------------------
            // Update sequence
            // --------------------------------------------------------

            palletType.CurrentSequence =
                nextSeq;


            // --------------------------------------------------------
            // Generate pallet number
            // --------------------------------------------------------

            var prefix =
                palletType.PalletName
                    .Trim()
                    .ToUpper();


            return $"{prefix}-{nextSeq:D2}";
        }


        // ============================================================
        // POST: api/StoreMaster
        //
        // Creates:
        //
        // 1. Pallet Type if it does not exist
        // 2. Pallet Number
        // 3. Store Master record
        // ============================================================

        // ============================================================
        // POST: api/StoreMaster
        //
        // Creates:
        // 1. Pallet Type if it does not exist
        // 2. Pallet Number
        // 3. Store Master record
        // ============================================================

        [HttpPost]
        public async Task<IActionResult> Create(
            [FromBody] CreateStoreMasterRequest model)
        {
            if (model == null)
            {
                return BadRequest(new
                {
                    message = "Invalid pallet data"
                });
            }

            // ========================================================
            // Validate Store Location
            // ========================================================

            if (string.IsNullOrWhiteSpace(model.StoreLocation))
            {
                return BadRequest(new
                {
                    message = "Store Location is required"
                });
            }

            // ========================================================
            // Validate Pallet Type
            // ========================================================

            if (string.IsNullOrWhiteSpace(model.PalletTypeName))
            {
                return BadRequest(new
                {
                    message = "Pallet Type is required"
                });
            }

            var palletTypeName = model.PalletTypeName
                .Trim()
                .ToUpper();

            // PALLET_TYPE_MASTER.PalletName max length = 10
            if (palletTypeName.Length > 10)
            {
                return BadRequest(new
                {
                    message = "Pallet Type cannot exceed 10 characters"
                });
            }

            // ========================================================
            // Validate Colour
            // ========================================================

            if (string.IsNullOrWhiteSpace(model.ColourCode))
            {
                return BadRequest(new
                {
                    message = "Pallet Colour is required"
                });
            }

            var colourCode = model.ColourCode
                .Trim()
                .ToUpper();

            if (!System.Text.RegularExpressions.Regex.IsMatch(
                colourCode,
                @"^#[0-9A-Fa-f]{6}$"))
            {
                return BadRequest(new
                {
                    message = "Pallet Colour must be a valid hex code."
                });
            }

            // ========================================================
            // Validate Part Number
            // ========================================================

            if (model.PartNumberId.HasValue)
            {
                var partExists = await _context.ItemMasters
                    .AnyAsync(x => x.Id == model.PartNumberId.Value);

                if (!partExists)
                {
                    return BadRequest(new
                    {
                        message = "Selected Part Number does not exist"
                    });
                }
            }

            try
            {
                // ====================================================
                // IMPORTANT
                // Use EF Core execution strategy because SQL Server
                // is configured with retry strategy.
                // ====================================================

                var strategy = _context.Database.CreateExecutionStrategy();

                int createdId = 0;
                string? createdPalletNumber = null;
                int createdPalletTypeId = 0;
                string? createdPalletTypeName = null;

                await strategy.ExecuteAsync(async () =>
                {
                    // =================================================
                    // Start transaction INSIDE execution strategy
                    // =================================================

                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(
                            System.Data.IsolationLevel.Serializable
                        );

                    try
                    {
                        // =============================================
                        // Find existing pallet type or create new one
                        // =============================================

                        var palletType =
                            await GetOrCreatePalletTypeAsync(
                                palletTypeName,
                                colourCode
                            );

                        if (palletType == null)
                        {
                            await transaction.RollbackAsync();

                            throw new Exception(
                                "Unable to create Pallet Type"
                            );
                        }

                        // =============================================
                        // Generate pallet number
                        // =============================================

                        var palletNumber =
                            await GeneratePalletNumberAsync(
                                palletType.Id
                            );

                        if (palletNumber == null)
                        {
                            await transaction.RollbackAsync();

                            throw new Exception(
                                $"Pallet number range is completed for Pallet Type '{palletType.PalletName}'."
                            );
                        }

                        // =============================================
                        // Create STORE_MASTER record
                        // =============================================

                        var entity = new StoreMaster
                        {
                            StoreLocation =
                                model.StoreLocation.Trim(),

                            PalletTypeId =
                                palletType.Id,

                            PalletNumber =
                                palletNumber,

                            ColourCode =
                                colourCode,

                            PartNumberId =
                                model.PartNumberId,

                            CreatedDate =
                                DateTime.Now
                        };

                        _context.StoreMasters.Add(entity);

                        // =============================================
                        // Save StoreMaster
                        // =============================================

                        await _context.SaveChangesAsync();

                        // =============================================
                        // Commit transaction
                        // =============================================

                        await transaction.CommitAsync();

                        // =============================================
                        // Store response values
                        // =============================================

                        createdId = entity.Id;

                        createdPalletNumber =
                            entity.PalletNumber;

                        createdPalletTypeId =
                            entity.PalletTypeId;

                        createdPalletTypeName =
                            palletType.PalletName;
                    }
                    catch
                    {
                        try
                        {
                            await transaction.RollbackAsync();
                        }
                        catch
                        {
                            // Ignore rollback exception
                        }

                        throw;
                    }
                });

                // ====================================================
                // Return success response
                // ====================================================

                return Ok(new
                {
                    id = createdId,

                    storeLocation =
                        model.StoreLocation.Trim(),

                    palletTypeId =
                        createdPalletTypeId,

                    palletTypeName =
                        createdPalletTypeName,

                    palletNumber =
                        createdPalletNumber,

                    colourCode =
                        colourCode,

                    partNumberId =
                        model.PartNumberId,

                    message =
                        "Pallet Saved Successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to save pallet",
                    error = ex.Message
                });
            }
        }


        // ============================================================
        // PUT: api/StoreMaster/5
        //
        // Pallet Type is NOT changed during edit.
        //
        // Only:
        //     Store Location
        //     Part Number
        //     Colour
        //
        // are updated.
        // ============================================================

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(
            int id,
            [FromBody] UpdateStoreMasterRequest model)
        {
            if (model == null)
            {
                return BadRequest(new
                {
                    message =
                        "Invalid pallet data"
                });
            }


            // ========================================================
            // Find existing record
            // ========================================================

            var entity =
                await _context.StoreMasters
                    .FirstOrDefaultAsync(
                        x => x.Id == id
                    );


            if (entity == null)
            {
                return NotFound(new
                {
                    message =
                        "Pallet record not found"
                });
            }


            // ========================================================
            // Validate Store Location
            // ========================================================

            if (string.IsNullOrWhiteSpace(
                model.StoreLocation))
            {
                return BadRequest(new
                {
                    message =
                        "Store Location is required"
                });
            }


            // ========================================================
            // Validate Colour
            // ========================================================

            if (string.IsNullOrWhiteSpace(
                model.ColourCode))
            {
                return BadRequest(new
                {
                    message =
                        "Pallet Colour is required"
                });
            }


            var colourCode =
                model.ColourCode
                    .Trim()
                    .ToUpper();


            if (!System.Text.RegularExpressions.Regex.IsMatch(
                colourCode,
                @"^#[0-9A-Fa-f]{6}$"))
            {
                return BadRequest(new
                {
                    message =
                        "Pallet Colour must be a valid hex code."
                });
            }


            // ========================================================
            // Validate Part Number
            // ========================================================

            if (model.PartNumberId.HasValue)
            {
                var partExists =
                    await _context.ItemMasters
                        .AnyAsync(x =>
                            x.Id ==
                            model.PartNumberId.Value);

                if (!partExists)
                {
                    return BadRequest(new
                    {
                        message =
                            "Selected Part Number does not exist"
                    });
                }
            }


            // ========================================================
            // Update fields
            // ========================================================

            entity.StoreLocation =
                model.StoreLocation.Trim();


            entity.PartNumberId =
                model.PartNumberId;


            entity.ColourCode =
                colourCode;


            // IMPORTANT:
            //
            // Do NOT change:
            //
            // entity.PalletTypeId
            //
            // entity.PalletNumber
            //
            // because those were already generated
            // when the pallet was created.


            entity.ModifiedDate =
                DateTime.Now;


            await _context.SaveChangesAsync();


            // ========================================================
            // Return
            // ========================================================

            return Ok(new
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
                    "Pallet Updated Successfully"
            });
        }


        // ============================================================
        // DELETE: api/StoreMaster/5
        // ============================================================

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity =
                await _context.StoreMasters
                    .FirstOrDefaultAsync(
                        x => x.Id == id
                    );


            if (entity == null)
            {
                return NotFound(new
                {
                    message =
                        "Pallet record not found"
                });
            }


            _context.StoreMasters.Remove(
                entity
            );


            await _context.SaveChangesAsync();


            return Ok(new
            {
                message =
                    "Deleted Successfully"
            });
        }
    }


    // ================================================================
    // CREATE REQUEST DTO
    // ================================================================
    //
    // Frontend sends:
    //
    // {
    //     storeLocation: "TRH STORE",
    //     palletTypeName: "TRH",
    //     colourCode: "#1E88E5",
    //     partNumberId: 5
    // }
    //
    // ================================================================

    public class CreateStoreMasterRequest
    {
        public string? StoreLocation { get; set; }

        public string? PalletTypeName { get; set; }

        public string? ColourCode { get; set; }

        public int? PartNumberId { get; set; }
    }


    // ================================================================
    // UPDATE REQUEST DTO
    // ================================================================

    public class UpdateStoreMasterRequest
    {
        public string? StoreLocation { get; set; }

        public string? ColourCode { get; set; }

        public int? PartNumberId { get; set; }
    }
}
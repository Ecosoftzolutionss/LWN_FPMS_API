using System;
using System.Collections.Generic;
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
    public class StoreMovementController : ControllerBase
    {
        private readonly AppDbContext _context;

        public StoreMovementController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // GET: api/StoreMovement/available-pallets
        // =========================================================
        //
        // Used by Material Issue.
        //
        // Returns pallets which:
        // 1. Have actually been stuffed into the store.
        // 2. Have positive current StoreMovement quantity.
        //
        // IMPORTANT:
        // A pallet may be partially issued multiple times.
        // Therefore MaterialIssue alone must NOT remove it from
        // this list.
        //
        // StoreMovements.Quantity represents the current physical
        // quantity remaining in the store.
        //
        // =========================================================

        [HttpGet("available-pallets")]
        public async Task<IActionResult> GetAvailablePallets()
        {
            try
            {
                var movements = await _context.StoreMovements
                    .Where(m => m.GrnPalletId != null)

                    .Include(m => m.GrnPallet)
                        .ThenInclude(p => p.GrnLine)
                            .ThenInclude(l => l.Item)

                    .Include(m => m.GrnPallet)
                        .ThenInclude(p => p.GrnLine)
                            .ThenInclude(l => l.Header)

                    .Include(m => m.StorePosition)
                        .ThenInclude(sp => sp.Store)

                    .Include(m => m.RackRow)
                        .ThenInclude(r => r.Column)
                            .ThenInclude(c => c.Rack)
                                .ThenInclude(rk => rk.Store)
                                    .ThenInclude(lm => lm.StoreMaster)

                    .ToListAsync();

                // ---------------------------------------------------------
                // Group StoreMovement rows by real pallet ID.
                //
                // A pallet can have multiple movement rows because it
                // can be stored in multiple locations / sides.
                // ---------------------------------------------------------

                var grouped = movements
                    .GroupBy(m => m.GrnPalletId)
                    .Select(g =>
                    {
                        var first = g
                            .OrderBy(m => m.MovementDate)
                            .First();

                        var pallet = first.GrnPallet;
                        var line = pallet.GrnLine;

                        // -------------------------------------------------
                        // Resolve store location.
                        // -------------------------------------------------

                        string location =
                            first.StorePosition?.Store?.StoreLocation;

                        if (location == null && first.RackRow != null)
                        {
                            location =
                                first.RackRow
                                    .Column?
                                    .Rack?
                                    .Store?
                                    .StoreMaster?
                                    .StoreLocation;
                        }

                        return new
                        {
                            // -------------------------------------------------
                            // REAL PALLET DATABASE ID
                            // -------------------------------------------------

                            id = g.Key,

                            // -------------------------------------------------
                            // ITEM
                            // -------------------------------------------------

                            itemId = line.ItemId,

                            partLabel =
                                $"{line.Item.ItemNumber} - {line.Item.ItemName}",

                            // -------------------------------------------------
                            // LOCATION
                            // -------------------------------------------------

                            storeLocation = location,

                            // -------------------------------------------------
                            // PALLET NUMBERS
                            // -------------------------------------------------

                            palletNo = pallet.PalletNo,

                            fifoPalletNo = line.FifoPalletNo,

                            // -------------------------------------------------
                            // REAL GRN NUMBER
                            // -------------------------------------------------

                            grnNo = line.Header.GrnNumber,

                            // -------------------------------------------------
                            // FIFO MOVEMENT DATE
                            // -------------------------------------------------

                            movementDate =
                                g.Min(m => m.MovementDate),

                            // -------------------------------------------------
                            // CURRENT STORE QUANTITY
                            // -------------------------------------------------

                            quantity =
                                g.Sum(m => m.Quantity),

                            // -------------------------------------------------
                            // GRN TYPE
                            // -------------------------------------------------

                            grnType = line.GrnType,

                            type =
                                string.Equals(
                                    line.GrnType,
                                    "Regular",
                                    StringComparison.OrdinalIgnoreCase)
                                    ? "REGULAR"
                                    : string.Equals(
                                        line.GrnType,
                                        "Sample",
                                        StringComparison.OrdinalIgnoreCase)
                                        ? "SAMPLE"
                                        : "UNKNOWN"
                        };
                    })

                    // Only pallets having physical stock.
                    .Where(r => r.quantity > 0)

                    // FIFO - oldest movement first.
                    .OrderBy(r => r.movementDate)

                    .ToList();

                return Ok(grouped);
            }
            catch (Exception ex)
            {
                var detail =
                    ex.InnerException?.Message ??
                    ex.Message;

                return StatusCode(
                    500,
                    new
                    {
                        message =
                            $"Failed to load available pallets: {detail}"
                    });
            }
        }


        // =========================================================
        // GET: api/StoreMovement/grn/{grnId}/pallets
        // =========================================================
        //
        // Used by Store Movement screen.
        //
        // RULES:
        //
        // 1. Only posted GRN lines.
        //
        // 2. Create GRN_PALLET if missing.
        //
        // 3. Already issued pallets are NOT returned.
        //
        // 4. Newly posted pallets ARE returned even when
        //    StuffedQty = 0.
        //
        // 5. Frontend decides whether the complete GRN is finished
        //    by comparing:
        //
        //        Quantity > StuffedQty
        //
        // =========================================================

        [HttpGet("grn/{grnId}/pallets")]
        public async Task<IActionResult> GetPalletsForGrn(int grnId)
        {
            try
            {
                // =====================================================
                // 1. GET POSTED GRN LINES
                // =====================================================

                var lines = await _context.GrnLines
                    .Include(x => x.Item)
                    .Where(x =>
                        x.GrnHeaderId == grnId &&
                        x.IsPosted)
                    .ToListAsync();

                if (lines.Count == 0)
                {
                    return NotFound(
                        new
                        {
                            message =
                                "No posted pallets found for this GRN"
                        });
                }


                // =====================================================
                // 2. CREATE GRN_PALLET IF IT DOES NOT EXIST
                // =====================================================

                foreach (var line in lines)
                {
                    var exists =
                        await _context.GrnPallets
                            .AnyAsync(
                                p => p.GrnLineId == line.Id);

                    if (!exists)
                    {
                        var palletNo = line.PalletNo;

                        if (string.IsNullOrWhiteSpace(palletNo))
                        {
                            return BadRequest(
                                new
                                {
                                    message =
                                        $"Pallet number is not available for GRN Line {line.Id}"
                                });
                        }

                        _context.GrnPallets.Add(
                            new GrnPallet
                            {
                                GrnLineId = line.Id,

                                PalletNo = palletNo,

                                Quantity = line.Quantity,

                                Rate = line.Rate,

                                CreatedDate = DateTime.Now
                            });
                    }
                }

                await _context.SaveChangesAsync();


                // =====================================================
                // 3. GET ALREADY ISSUED PALLET IDs
                // =====================================================

                var issuedPalletIds =
                    await _context.MaterialIssues
                        .AsNoTracking()
                        .Where(x =>
                            x.GrnPalletId.HasValue &&
                            x.GrnPalletId.Value > 0)
                        .Select(
                            x => x.GrnPalletId!.Value)
                        .Distinct()
                        .ToListAsync();


                // =====================================================
                // 4. GET PALLETS FOR THIS GRN
                // =====================================================
                //
                // IMPORTANT:
                //
                // DO NOT filter:
                //
                //     StuffedQty > 0
                //
                // here.
                //
                // A newly posted GRN has:
                //
                //     Quantity  = 20
                //     StuffedQty = 0
                //
                // It still needs to appear in Store Movement so
                // the user can select it and stuff it.
                //
                // =====================================================

                var pallets =
                    await _context.GrnPallets

                        .Include(x => x.GrnLine)
                            .ThenInclude(l => l.Item)

                        .Where(x =>
                            x.GrnLine.GrnHeaderId == grnId &&

                            x.GrnLine.IsPosted &&

                            // -----------------------------------------
                            // ALREADY ISSUED PALLET
                            // -----------------------------------------
                            //
                            // If Material Issue has this GrnPalletId,
                            // don't display it again.
                            //
                            !issuedPalletIds.Contains(x.Id)
                        )

                        .OrderBy(x => x.PalletNo)

                        .Select(x => new
                        {
                            // =================================================
                            // PALLET
                            // =================================================

                            x.Id,

                            x.PalletNo,

                            x.Quantity,

                            x.Rate,


                            // =================================================
                            // ITEM
                            // =================================================

                            PartNumber =
                                x.GrnLine.Item.ItemNumber,

                            PartName =
                                x.GrnLine.Item.ItemName,


                            // =================================================
                            // CURRENT STUFFED QUANTITY
                            // =================================================

                            StuffedQty =
                                _context.StoreMovements
                                    .Where(m =>
                                        m.GrnPalletId == x.Id &&
                                        m.Quantity > 0)
                                    .Sum(
                                        m => (decimal?)m.Quantity
                                    ) ?? 0,


                            // =================================================
                            // STORE ASSIGNMENTS
                            // =================================================

                            Assignments =
                                _context.StoreMovements
                                    .Where(m =>
                                        m.GrnPalletId == x.Id &&
                                        m.StorePositionId != null &&
                                        m.Quantity > 0)

                                    .Select(m => new
                                    {
                                        m.Id,

                                        StoreLocation =
                                            m.StorePosition
                                                .Store
                                                .StoreLocation,

                                        PositionCode =
                                            m.StorePosition
                                                .PositionCode,

                                        m.Side,

                                        m.Quantity
                                    })

                                    .ToList()
                        })

                        .ToListAsync();


                // =====================================================
                // IMPORTANT FIX
                // =====================================================
                //
                // DO NOT DO THIS:
                //
                // var availablePallets = pallets
                //     .Where(x => x.StuffedQty > 0)
                //     .ToList();
                //
                // That condition hides newly posted GRNs.
                //
                // Instead return ALL non-issued pallets.
                //
                // The frontend already checks:
                //
                //     Quantity > StuffedQty
                //
                // and hides the GRN when fully stuffed.
                // =====================================================

                return Ok(pallets);
            }
            catch (Exception ex)
            {
                var detail =
                    ex.InnerException?.Message ??
                    ex.Message;

                return StatusCode(
                    500,
                    new
                    {
                        message =
                            $"Failed to load pallets: {detail}"
                    });
            }
        }


        // =========================================================
        // GET: api/StoreMovement/positions
        // =========================================================

        [HttpGet("positions")]
        public async Task<IActionResult> GetPositions()
        {
            try
            {
                var stores =
                    await _context.StoreMasters
                        .ToListAsync();

                foreach (var store in stores)
                {
                    var hasPositions =
                        await _context.StorePositions
                            .AnyAsync(
                                p => p.StoreMasterId == store.Id);

                    if (!hasPositions)
                    {
                        _context.StorePositions.Add(
                            new StorePosition
                            {
                                StoreMasterId = store.Id,
                                PositionCode = "P1",
                                Capacity = 5000
                            });

                        _context.StorePositions.Add(
                            new StorePosition
                            {
                                StoreMasterId = store.Id,
                                PositionCode = "P2",
                                Capacity = 5000
                            });

                        await _context.SaveChangesAsync();
                    }
                }


                var result =
                    await _context.StoreMasters

                        .Select(s => new
                        {
                            s.Id,

                            s.StoreLocation,

                            Positions =
                                _context.StorePositions

                                    .Where(
                                        p =>
                                            p.StoreMasterId ==
                                            s.Id)

                                    .OrderBy(
                                        p =>
                                            p.PositionCode)

                                    .Select(p => new
                                    {
                                        p.Id,

                                        p.PositionCode,

                                        p.Capacity,

                                        Stuffed =
                                            _context.StoreMovements
                                                .Where(
                                                    m =>
                                                        m.StorePositionId ==
                                                        p.Id)
                                                .Sum(
                                                    m =>
                                                        (decimal?)m.Quantity)
                                                ?? 0
                                    })

                                    .ToList()
                        })

                        .ToListAsync();

                return Ok(result);
            }
            catch (Exception ex)
            {
                var detail =
                    ex.InnerException?.Message ??
                    ex.Message;

                return StatusCode(
                    500,
                    new
                    {
                        message =
                            $"Failed to load store positions: {detail}"
                    });
            }
        }


        // =========================================================
        // GET: api/StoreMovement/rack-slots?itemId=5
        // =========================================================

        [HttpGet("rack-slots")]
        public async Task<IActionResult> GetRackSlots(
            [FromQuery] int? itemId)
        {
            try
            {
                var query =
                    _context.LocationMasters

                        .Include(x => x.StoreMaster)

                        .Include(x => x.Racks)
                            .ThenInclude(r => r.Columns)
                                .ThenInclude(c => c.Rows)

                        .AsQueryable();


                // -----------------------------------------------------
                // Only show store configured for selected part.
                // -----------------------------------------------------

                if (itemId.HasValue)
                {
                    query =
                        query.Where(
                            x =>
                                x.StoreMaster.PartNumberId ==
                                itemId.Value);
                }


                var stores =
                    await query

                        .Select(store => new
                        {
                            store.Id,

                            store.StoreCode,

                            StoreLocation =
                                store.StoreMaster.StoreLocation,

                            Racks =
                                store.Racks.Select(rack => new
                                {
                                    rack.Id,

                                    rack.RackNo,

                                    Columns =
                                        rack.Columns.Select(col => new
                                        {
                                            col.Id,

                                            col.ColumnNo,

                                            Rows =
                                                col.Rows.Select(row => new
                                                {
                                                    row.Id,

                                                    row.RowNo,

                                                    row.HasFront,

                                                    row.HasRear,

                                                    row.Fixture,

                                                    OccupiedSlots =
                                                        _context.StoreMovements
                                                            .Where(
                                                                m =>
                                                                    m.RackRowId ==
                                                                    row.Id)

                                                            .Select(m => new
                                                            {
                                                                m.SlotNumber,

                                                                m.Side,

                                                                m.Quantity,

                                                                m.Id,

                                                                m.GrnPalletId,

                                                                PalletNo =
                                                                    m.GrnPallet !=
                                                                    null
                                                                        ? m.GrnPallet.PalletNo
                                                                        : null
                                                            })

                                                            .ToList()
                                                })

                                                .ToList()
                                        })

                                        .ToList()
                                })

                                .ToList()
                        })

                        .ToListAsync();

                return Ok(stores);
            }
            catch (Exception ex)
            {
                var detail =
                    ex.InnerException?.Message ??
                    ex.Message;

                return StatusCode(
                    500,
                    new
                    {
                        message =
                            $"Failed to load rack slots: {detail}"
                    });
            }
        }


        // =========================================================
        // POST: api/StoreMovement/stuff-rack-slot
        // =========================================================

        public class StuffRackSlotRequest
        {
            public int GrnPalletId { get; set; }

            public int RackRowId { get; set; }

            public int SlotNumber { get; set; }

            public string Side { get; set; } = "Front";

            public decimal Quantity { get; set; }

            public string? CreatedBy { get; set; }
        }


        [HttpPost("stuff-rack-slot")]
        public async Task<IActionResult> StuffRackSlot(
            [FromBody] StuffRackSlotRequest req)
        {
            try
            {
                // -----------------------------------------------------
                // VALIDATION
                // -----------------------------------------------------

                if (
                    req == null ||
                    req.GrnPalletId <= 0 ||
                    req.RackRowId <= 0 ||
                    req.SlotNumber <= 0)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "Pallet, Rack Row and Slot Number are required"
                        });
                }


                if (
                    req.Side != "Front" &&
                    req.Side != "Rear")
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "Side must be 'Front' or 'Rear'"
                        });
                }


                if (req.Quantity <= 0)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "Quantity must be greater than 0"
                        });
                }


                // -----------------------------------------------------
                // GET PALLET
                // -----------------------------------------------------

                var pallet =
                    await _context.GrnPallets

                        .Include(p => p.GrnLine)

                        .FirstOrDefaultAsync(
                            p =>
                                p.Id ==
                                req.GrnPalletId);


                if (pallet == null)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "Pallet not found"
                        });
                }


                // -----------------------------------------------------
                // BLOCK ALREADY ISSUED PALLET
                // -----------------------------------------------------

                var alreadyIssued =
                    await _context.MaterialIssues
                        .AnyAsync(
                            x =>
                                x.GrnPalletId ==
                                req.GrnPalletId);


                if (alreadyIssued)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                $"Pallet {pallet.PalletNo} has already been issued and cannot be stuffed again."
                        });
                }


                // -----------------------------------------------------
                // GET RACK ROW
                // -----------------------------------------------------

                var row =
                    await _context.RackRows

                        .Include(r => r.Column)

                            .ThenInclude(c => c.Rack)

                                .ThenInclude(rk => rk.Store)

                                    .ThenInclude(lm => lm.StoreMaster)

                        .FirstOrDefaultAsync(
                            r =>
                                r.Id ==
                                req.RackRowId);


                if (row == null)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "Rack Row not found"
                        });
                }


                // -----------------------------------------------------
                // CHECK STORE PART CONFIGURATION
                // -----------------------------------------------------

                var storeConfiguredPartId =
                    row.Column?
                        .Rack?
                        .Store?
                        .StoreMaster?
                        .PartNumberId;


                if (
                    storeConfiguredPartId.HasValue &&
                    storeConfiguredPartId.Value !=
                    pallet.GrnLine.ItemId)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "This store location is configured for a different Part Number. Choose a slot in the correct store."
                        });
                }


                // -----------------------------------------------------
                // CURRENT STUFFED QUANTITY
                // -----------------------------------------------------

                var alreadyStuffed =
                    await _context.StoreMovements

                        .Where(
                            m =>
                                m.GrnPalletId ==
                                req.GrnPalletId)

                        .SumAsync(
                            m =>
                                (decimal?)m.Quantity)
                        ?? 0;


                var remaining =
                    pallet.Quantity -
                    alreadyStuffed;


                if (req.Quantity > remaining)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                $"Only {remaining} remaining on this pallet"
                        });
                }


                // -----------------------------------------------------
                // CHECK SLOT
                // -----------------------------------------------------

                var slotOccupied =
                    await _context.StoreMovements

                        .AnyAsync(
                            m =>
                                m.RackRowId ==
                                    req.RackRowId &&

                                m.SlotNumber ==
                                    req.SlotNumber &&

                                m.Side ==
                                    req.Side);


                if (slotOccupied)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "That slot is already occupied"
                        });
                }


                // -----------------------------------------------------
                // CREATE MOVEMENT
                // -----------------------------------------------------

                var movement =
                    new StoreMovement
                    {
                        GrnPalletId =
                            req.GrnPalletId,

                        RackRowId =
                            req.RackRowId,

                        SlotNumber =
                            req.SlotNumber,

                        Side =
                            req.Side,

                        Quantity =
                            req.Quantity,

                        MovementDate =
                            DateTime.Now,

                        CreatedBy =
                            req.CreatedBy?.Trim()
                    };


                _context.StoreMovements.Add(
                    movement);

                await _context.SaveChangesAsync();


                return Ok(
                    new
                    {
                        movement.Id,

                        message =
                            "Stuffed Successfully"
                    });
            }
            catch (Exception ex)
            {
                var detail =
                    ex.InnerException?.Message ??
                    ex.Message;

                return StatusCode(
                    500,
                    new
                    {
                        message =
                            $"Stuff failed: {detail}"
                    });
            }
        }


        // =========================================================
        // POST: api/StoreMovement/stuff
        // =========================================================

        public class StuffRequest
        {
            public int GrnPalletId { get; set; }

            public int StorePositionId { get; set; }

            public string Side { get; set; } = "Front";

            public decimal Quantity { get; set; }

            public string? CreatedBy { get; set; }
        }


        [HttpPost("stuff")]
        public async Task<IActionResult> Stuff(
            [FromBody] StuffRequest req)
        {
            try
            {
                // -----------------------------------------------------
                // VALIDATION
                // -----------------------------------------------------

                if (
                    req == null ||
                    req.GrnPalletId <= 0 ||
                    req.StorePositionId <= 0)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "Pallet and Store Position are required"
                        });
                }


                if (
                    req.Side != "Front" &&
                    req.Side != "Rear")
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "Side must be 'Front' or 'Rear'"
                        });
                }


                if (req.Quantity <= 0)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "Quantity must be greater than 0"
                        });
                }


                // -----------------------------------------------------
                // PALLET
                // -----------------------------------------------------

                var pallet =
                    await _context.GrnPallets
                        .FindAsync(
                            req.GrnPalletId);


                if (pallet == null)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "Pallet not found"
                        });
                }


                // -----------------------------------------------------
                // STORE POSITION
                // -----------------------------------------------------

                var position =
                    await _context.StorePositions
                        .FindAsync(
                            req.StorePositionId);


                if (position == null)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                "Store Position not found"
                        });
                }


                // -----------------------------------------------------
                // CURRENT STUFFED QUANTITY
                // -----------------------------------------------------

                var alreadyStuffed =
                    await _context.StoreMovements

                        .Where(
                            m =>
                                m.GrnPalletId ==
                                req.GrnPalletId)

                        .SumAsync(
                            m =>
                                (decimal?)m.Quantity)
                        ?? 0;


                var remaining =
                    pallet.Quantity -
                    alreadyStuffed;


                if (req.Quantity > remaining)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                $"Only {remaining} remaining on this pallet"
                        });
                }


                // -----------------------------------------------------
                // POSITION CAPACITY
                // -----------------------------------------------------

                var positionStuffed =
                    await _context.StoreMovements

                        .Where(
                            m =>
                                m.StorePositionId ==
                                req.StorePositionId)

                        .SumAsync(
                            m =>
                                (decimal?)m.Quantity)
                        ?? 0;


                var available =
                    position.Capacity -
                    positionStuffed;


                if (req.Quantity > available)
                {
                    return BadRequest(
                        new
                        {
                            message =
                                $"Only {available} available space in {position.PositionCode}"
                        });
                }


                // -----------------------------------------------------
                // CREATE MOVEMENT
                // -----------------------------------------------------

                var movement =
                    new StoreMovement
                    {
                        GrnPalletId =
                            req.GrnPalletId,

                        StorePositionId =
                            req.StorePositionId,

                        Side =
                            req.Side,

                        Quantity =
                            req.Quantity,

                        MovementDate =
                            DateTime.Now,

                        CreatedBy =
                            req.CreatedBy?.Trim()
                    };


                _context.StoreMovements.Add(
                    movement);

                await _context.SaveChangesAsync();


                return Ok(
                    new
                    {
                        movement.Id,

                        message =
                            "Stuffed Successfully"
                    });
            }
            catch (Exception ex)
            {
                var detail =
                    ex.InnerException?.Message ??
                    ex.Message;

                return StatusCode(
                    500,
                    new
                    {
                        message =
                            $"Stuff failed: {detail}"
                    });
            }
        }


        // =========================================================
        // DELETE: api/StoreMovement/{id}
        // =========================================================

        [HttpDelete("{id}")]
        public async Task<IActionResult> Undo(int id)
        {
            try
            {
                var movement =
                    await _context.StoreMovements
                        .FindAsync(id);


                if (movement == null)
                {
                    return NotFound(
                        new
                        {
                            message =
                                "Movement not found"
                        });
                }


                _context.StoreMovements.Remove(
                    movement);

                await _context.SaveChangesAsync();


                return Ok(
                    new
                    {
                        message =
                            "Movement Undone"
                    });
            }
            catch (Exception ex)
            {
                var detail =
                    ex.InnerException?.Message ??
                    ex.Message;

                return StatusCode(
                    500,
                    new
                    {
                        message =
                            $"Undo failed: {detail}"
                    });
            }
        }


        // =========================================================
        // GET: api/StoreMovement/all-pallets-status
        // =========================================================
        //
        // Used by Store Verification.
        //
        // Returns:
        //
        // IN_STOCK
        // ISSUED
        //
        // =========================================================

        [HttpGet("all-pallets-status")]
        public async Task<IActionResult> GetAllPalletsWithStatus()
        {
            try
            {
                // -----------------------------------------------------
                // STUFFED PALLETS
                // -----------------------------------------------------

                var stuffedIds =
                    await _context.StoreMovements

                        .Where(
                            m =>
                                m.GrnPalletId != null)

                        .Select(
                            m =>
                                m.GrnPalletId!.Value)

                        .Distinct()

                        .ToListAsync();


                // -----------------------------------------------------
                // ISSUED PALLETS
                // -----------------------------------------------------

                var issuedIds =
                    await _context.MaterialIssues

                        .Where(
                            x =>
                                x.GrnPalletId.HasValue)

                        .Select(
                            x =>
                                x.GrnPalletId!.Value)

                        .Distinct()

                        .ToListAsync();


                var issuedSet =
                    new HashSet<int>(
                        issuedIds);


                var relevantIds =
                    stuffedIds
                        .Union(issuedIds)
                        .Distinct()
                        .ToList();


                // -----------------------------------------------------
                // PALLET DATA
                // -----------------------------------------------------

                var pallets =
                    await _context.GrnPallets

                        .Where(
                            p =>
                                relevantIds.Contains(
                                    p.Id))

                        .Include(
                            p =>
                                p.GrnLine)
                            .ThenInclude(
                                l =>
                                    l.Item)

                        .Include(
                            p =>
                                p.GrnLine)
                            .ThenInclude(
                                l =>
                                    l.Header)

                        .Select(
                            p => new
                            {
                                id =
                                    p.Id,

                                itemId =
                                    p.GrnLine.ItemId,

                                partLabel =
                                    p.GrnLine.Item.ItemNumber +
                                    " - " +
                                    p.GrnLine.Item.ItemName,

                                palletNo =
                                    p.PalletNo,

                                fifoPalletNo =
                                    p.GrnLine.FifoPalletNo,

                                grnNo =
                                    p.GrnLine.Header.GrnNumber,

                                quantity =
                                    p.Quantity
                            })

                        .ToListAsync();


                // -----------------------------------------------------
                // STATUS
                // -----------------------------------------------------

                var result =
                    pallets.Select(
                        p => new
                        {
                            p.id,

                            p.itemId,

                            p.partLabel,

                            p.palletNo,

                            p.fifoPalletNo,

                            p.grnNo,

                            p.quantity,

                            status =
                                issuedSet.Contains(
                                    p.id)
                                    ? "ISSUED"
                                    : "IN_STOCK"
                        });


                return Ok(result);
            }
            catch (Exception ex)
            {
                var detail =
                    ex.InnerException?.Message ??
                    ex.Message;

                return StatusCode(
                    500,
                    new
                    {
                        message =
                            $"Failed to load pallet status: {detail}"
                    });
            }
        }
    }
}
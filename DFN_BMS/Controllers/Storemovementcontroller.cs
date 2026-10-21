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
    // =============================================================
    // REQUIRED MODEL CHANGE (StoreMovement.cs)
    //
    //     public bool IsGround { get; set; } = false;
    //
    // REQUIRED DB CHANGE (or add an EF migration):
    //
    //     ALTER TABLE STORE_MOVEMENT
    //         ADD IsGround bit NOT NULL CONSTRAINT DF_STORE_MOVEMENT_IsGround DEFAULT 0;
    //
    // A ground movement is a normal StoreMovement row with
    //     IsGround = true, RackRowId = null, Side = "Ground".
    // Quantity is counted in stock exactly like a rack movement, so
    // Material Issue / Store Verification keep working.
    // =============================================================

    [ApiController]
    [Route("api/[controller]")]
    public class StoreMovementController : ControllerBase
    {
        private readonly AppDbContext _context;

        public StoreMovementController(AppDbContext context)
        {
            _context = context;
        }

        private const string GroundSide = "Ground";

        // =========================================================
        // HELPER: free rack slots (Front / Rear) for one part.
        //
        // capacity = sum(Fixture) of every row that has that side
        // occupied = StoreMovement rows already placed in those rows
        //
        // NOTE: a slot is "occupied" while a StoreMovement row exists
        // for it (same rule as StuffRackSlot). When Material Issue frees
        // a slot by deleting the row, the slot becomes free here too.
        // If your Material Issue only sets Quantity = 0 and keeps the
        // row, add `&& m.Quantity > 0` to the occupied filters
        // (here and in StuffRackSlot / GetRackSlots).
        // =========================================================
        private async Task<(int Front, int Rear)> GetFreeSlotCountsAsync(int itemId)
        {
            var rows = await _context.RackRows
                .AsNoTracking()
                .Where(r =>
                    r.Column.Rack.Store.StoreMaster.PartNumberId == itemId)
                .Select(r => new
                {
                    r.Id,
                    r.HasFront,
                    r.HasRear,
                    r.Fixture
                })
                .ToListAsync();

            if (rows.Count == 0)
                return (0, 0);

            var rowIds = rows.Select(r => r.Id).ToList();

            var occupied = await _context.StoreMovements
                .AsNoTracking()
                .Where(m =>
                    m.RackRowId != null &&
                    rowIds.Contains(m.RackRowId.Value))
                .GroupBy(m => m.Side)
                .Select(g => new { Side = g.Key, Count = g.Count() })
                .ToListAsync();

            int capFront = rows
                .Where(r => r.HasFront == true)
                .Sum(r => Math.Max(1, Convert.ToInt32(r.Fixture)));

            int capRear = rows
                .Where(r => r.HasRear == true)
                .Sum(r => Math.Max(1, Convert.ToInt32(r.Fixture)));

            int occFront = occupied.Where(o => o.Side == "Front").Sum(o => o.Count);
            int occRear = occupied.Where(o => o.Side == "Rear").Sum(o => o.Count);

            return (Math.Max(0, capFront - occFront),
                    Math.Max(0, capRear - occRear));
        }

        // =========================================================
        // GET: api/StoreMovement/available-pallets
        // Used by Material Issue.
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

                var grouped = movements
                    .GroupBy(m => m.GrnPalletId)
                    .Select(g =>
                    {
                        var first = g.OrderBy(m => m.MovementDate).First();
                        var pallet = first.GrnPallet;

                        if (pallet == null)
                            return null;

                        var line = pallet.GrnLine;

                        // Resolve store location.
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

                        // Pallet kept on the ground (rack was full).
                        if (location == null && first.IsGround)
                            location = "GROUND";

                        return new
                        {
                            id = g.Key,
                            itemId = line.ItemId,
                            partLabel = $"{line.Item.ItemNumber} - {line.Item.ItemName}",
                            storeLocation = location,
                            palletNo = pallet.PalletNo,
                            fifoPalletNo = line.FifoPalletNo,
                            grnNo = line.Header.GrnNumber,
                            movementDate = g.Min(m => m.MovementDate),
                            quantity = g.Sum(m => m.Quantity),
                            grnType = line.GrnType,
                            type =
                                string.Equals(line.GrnType, "Regular", StringComparison.OrdinalIgnoreCase)
                                    ? "REGULAR"
                                    : string.Equals(line.GrnType, "Sample", StringComparison.OrdinalIgnoreCase)
                                        ? "SAMPLE"
                                        : "UNKNOWN"
                        };
                    })
                    .Where(r => r != null && r.quantity > 0)
                    .OrderBy(r => r.movementDate)
                    .ToList();

                return Ok(grouped);
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Failed to load available pallets: {detail}" });
            }
        }

        // =========================================================
        // GET: api/StoreMovement/grn/{grnId}/pallets
        // Used by Store Movement screen.
        // =========================================================

        [HttpGet("grn/{grnId}/pallets")]
        public async Task<IActionResult> GetPalletsForGrn(int grnId)
        {
            try
            {
                // 1. POSTED GRN LINES
                var lines = await _context.GrnLines
                    .Include(x => x.Item)
                    .Where(x => x.GrnHeaderId == grnId && x.IsPosted)
                    .ToListAsync();

                if (lines.Count == 0)
                {
                    return NotFound(new { message = "No posted pallets found for this GRN" });
                }

                // 2. CREATE GRN_PALLET IF IT DOES NOT EXIST
                foreach (var line in lines)
                {
                    var exists = await _context.GrnPallets.AnyAsync(p => p.GrnLineId == line.Id);

                    if (!exists)
                    {
                        var palletNo = line.PalletNo;

                        if (string.IsNullOrWhiteSpace(palletNo))
                        {
                            return BadRequest(new
                            {
                                message = $"Pallet number is not available for GRN Line {line.Id}"
                            });
                        }

                        _context.GrnPallets.Add(new GrnPallet
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

                // 3. ALREADY ISSUED PALLET IDs
                var issuedPalletIds =
                    await _context.MaterialIssues
                        .AsNoTracking()
                        .Where(x => x.GrnPalletId.HasValue && x.GrnPalletId.Value > 0)
                        .Select(x => x.GrnPalletId!.Value)
                        .Distinct()
                        .ToListAsync();

                // 4. PALLETS FOR THIS GRN
                // (Do NOT filter StuffedQty > 0 - new pallets must show.)
                var pallets =
                    await _context.GrnPallets
                        .Include(x => x.GrnLine)
                            .ThenInclude(l => l.Item)
                        .Where(x =>
                            x.GrnLine.GrnHeaderId == grnId &&
                            x.GrnLine.IsPosted &&
                            !issuedPalletIds.Contains(x.Id))
                        .OrderBy(x => x.PalletNo)
                        .Select(x => new
                        {
                            x.Id,
                            x.PalletNo,
                            x.Quantity,
                            x.Rate,

                            ItemId = x.GrnLine.ItemId,
                            PartNumber = x.GrnLine.Item.ItemNumber,
                            PartName = x.GrnLine.Item.ItemName,

                            // Rack + ground quantity both count as stuffed.
                            StuffedQty =
                                _context.StoreMovements
                                    .Where(m => m.GrnPalletId == x.Id && m.Quantity > 0)
                                    .Sum(m => (decimal?)m.Quantity) ?? 0,

                            Assignments =
                                _context.StoreMovements
                                    .Where(m =>
                                        m.GrnPalletId == x.Id &&
                                        m.StorePositionId != null &&
                                        m.Quantity > 0)
                                    .Select(m => new
                                    {
                                        m.Id,
                                        StoreLocation = m.StorePosition.Store.StoreLocation,
                                        PositionCode = m.StorePosition.PositionCode,
                                        m.Side,
                                        m.Quantity
                                    })
                                    .ToList(),

                            // Quantity of this pallet that is waiting on the ground.
                            GroundAssignments =
                                _context.StoreMovements
                                    .Where(m =>
                                        m.GrnPalletId == x.Id &&
                                        m.IsGround &&
                                        m.Quantity > 0)
                                    .Select(m => new
                                    {
                                        m.Id,
                                        m.Quantity,
                                        m.MovementDate
                                    })
                                    .ToList()
                        })
                        .ToListAsync();

                return Ok(pallets);
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Failed to load pallets: {detail}" });
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
                var stores = await _context.StoreMasters.ToListAsync();

                foreach (var store in stores)
                {
                    var hasPositions =
                        await _context.StorePositions.AnyAsync(p => p.StoreMasterId == store.Id);

                    if (!hasPositions)
                    {
                        _context.StorePositions.Add(new StorePosition
                        {
                            StoreMasterId = store.Id,
                            PositionCode = "P1",
                            Capacity = 5000
                        });

                        _context.StorePositions.Add(new StorePosition
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
                                    .Where(p => p.StoreMasterId == s.Id)
                                    .OrderBy(p => p.PositionCode)
                                    .Select(p => new
                                    {
                                        p.Id,
                                        p.PositionCode,
                                        p.Capacity,
                                        Stuffed =
                                            _context.StoreMovements
                                                .Where(m => m.StorePositionId == p.Id)
                                                .Sum(m => (decimal?)m.Quantity) ?? 0
                                    })
                                    .ToList()
                        })
                        .ToListAsync();

                return Ok(result);
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Failed to load store positions: {detail}" });
            }
        }

        // =========================================================
        // GET: api/StoreMovement/rack-slots?itemId=5
        // =========================================================

        [HttpGet("rack-slots")]
        public async Task<IActionResult> GetRackSlots([FromQuery] int? itemId)
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

                // Only show store configured for selected part.
                if (itemId.HasValue)
                {
                    query = query.Where(x => x.StoreMaster.PartNumberId == itemId.Value);
                }

                var stores =
                    await query
                        .Select(store => new
                        {
                            store.Id,
                            store.StoreCode,
                            PartNumberId = store.StoreMaster.PartNumberId,
                            StoreLocation = store.StoreMaster.StoreLocation,

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
                                                            .Where(m => m.RackRowId == row.Id)
                                                            .Select(m => new
                                                            {
                                                                m.SlotNumber,
                                                                m.Side,
                                                                m.Quantity,
                                                                m.Id,
                                                                m.GrnPalletId,
                                                                PalletNo =
                                                                    m.GrnPallet != null
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
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Failed to load rack slots: {detail}" });
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
        public async Task<IActionResult> StuffRackSlot([FromBody] StuffRackSlotRequest req)
        {
            try
            {
                if (req == null || req.GrnPalletId <= 0 || req.RackRowId <= 0 || req.SlotNumber <= 0)
                {
                    return BadRequest(new { message = "Pallet, Rack Row and Slot Number are required" });
                }

                if (req.Side != "Front" && req.Side != "Rear")
                {
                    return BadRequest(new { message = "Side must be 'Front' or 'Rear'" });
                }

                if (req.Quantity <= 0)
                {
                    return BadRequest(new { message = "Quantity must be greater than 0" });
                }

                var pallet =
                    await _context.GrnPallets
                        .Include(p => p.GrnLine)
                        .FirstOrDefaultAsync(p => p.Id == req.GrnPalletId);

                if (pallet == null)
                {
                    return BadRequest(new { message = "Pallet not found" });
                }

                var alreadyIssued =
                    await _context.MaterialIssues.AnyAsync(x => x.GrnPalletId == req.GrnPalletId);

                if (alreadyIssued)
                {
                    return BadRequest(new
                    {
                        message = $"Pallet {pallet.PalletNo} has already been issued and cannot be stuffed again."
                    });
                }

                var row =
                    await _context.RackRows
                        .Include(r => r.Column)
                            .ThenInclude(c => c.Rack)
                                .ThenInclude(rk => rk.Store)
                                    .ThenInclude(lm => lm.StoreMaster)
                        .FirstOrDefaultAsync(r => r.Id == req.RackRowId);

                if (row == null)
                {
                    return BadRequest(new { message = "Rack Row not found" });
                }

                var storeConfiguredPartId =
                    row.Column?.Rack?.Store?.StoreMaster?.PartNumberId;

                if (storeConfiguredPartId.HasValue &&
                    storeConfiguredPartId.Value != pallet.GrnLine.ItemId)
                {
                    return BadRequest(new
                    {
                        message = "This store location is configured for a different Part Number. Choose a slot in the correct store."
                    });
                }

                var alreadyStuffed =
                    await _context.StoreMovements
                        .Where(m => m.GrnPalletId == req.GrnPalletId)
                        .SumAsync(m => (decimal?)m.Quantity) ?? 0;

                var remaining = pallet.Quantity - alreadyStuffed;

                if (req.Quantity > remaining)
                {
                    return BadRequest(new { message = $"Only {remaining} remaining on this pallet" });
                }

                var slotOccupied =
                    await _context.StoreMovements
                        .AnyAsync(m =>
                            m.RackRowId == req.RackRowId &&
                            m.SlotNumber == req.SlotNumber &&
                            m.Side == req.Side);

                if (slotOccupied)
                {
                    return BadRequest(new { message = "That slot is already occupied" });
                }

                var movement = new StoreMovement
                {
                    GrnPalletId = req.GrnPalletId,
                    RackRowId = req.RackRowId,
                    SlotNumber = req.SlotNumber,
                    Side = req.Side,
                    Quantity = req.Quantity,
                    MovementDate = DateTime.Now,
                    CreatedBy = req.CreatedBy?.Trim()
                };

                _context.StoreMovements.Add(movement);
                await _context.SaveChangesAsync();

                return Ok(new { movement.Id, message = "Stuffed Successfully" });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Stuff failed: {detail}" });
            }
        }

        // =========================================================
        // POST: api/StoreMovement/store-ground
        // =========================================================
        //
        // Used when ALL rack locations of the part are full.
        // The pallet quantity is kept on the ground and waits until
        // a rack slot becomes free (see GET ground-pallets).
        //
        // =========================================================

        public class StoreGroundRequest
        {
            public int GrnPalletId { get; set; }
            public decimal Quantity { get; set; }
            public string? CreatedBy { get; set; }
        }

        [HttpPost("store-ground")]
        public async Task<IActionResult> StoreGround([FromBody] StoreGroundRequest req)
        {
            try
            {
                if (req == null || req.GrnPalletId <= 0)
                {
                    return BadRequest(new { message = "Pallet is required" });
                }

                if (req.Quantity <= 0)
                {
                    return BadRequest(new { message = "Quantity must be greater than 0" });
                }

                var pallet =
                    await _context.GrnPallets
                        .Include(p => p.GrnLine)
                        .FirstOrDefaultAsync(p => p.Id == req.GrnPalletId);

                if (pallet == null)
                {
                    return BadRequest(new { message = "Pallet not found" });
                }

                var alreadyIssued =
                    await _context.MaterialIssues.AnyAsync(x => x.GrnPalletId == req.GrnPalletId);

                if (alreadyIssued)
                {
                    return BadRequest(new
                    {
                        message = $"Pallet {pallet.PalletNo} has already been issued and cannot be stored again."
                    });
                }

                var alreadyStuffed =
                    await _context.StoreMovements
                        .Where(m => m.GrnPalletId == req.GrnPalletId)
                        .SumAsync(m => (decimal?)m.Quantity) ?? 0;

                var remaining = pallet.Quantity - alreadyStuffed;

                if (req.Quantity > remaining)
                {
                    return BadRequest(new { message = $"Only {remaining} remaining on this pallet" });
                }

                // Ground is allowed only when the rack is really full.
                var free = await GetFreeSlotCountsAsync(pallet.GrnLine.ItemId);

                if (free.Front + free.Rear > 0)
                {
                    return BadRequest(new
                    {
                        message = $"Rack locations are available now ({free.Front} Front, {free.Rear} Rear). Store the pallet in a rack location instead of the ground."
                    });
                }

                var movement = new StoreMovement
                {
                    GrnPalletId = req.GrnPalletId,
                    RackRowId = null,
                    Side = GroundSide,
                    IsGround = true,
                    Quantity = req.Quantity,
                    MovementDate = DateTime.Now,
                    CreatedBy = req.CreatedBy?.Trim()
                };

                _context.StoreMovements.Add(movement);
                await _context.SaveChangesAsync();

                return Ok(new { movement.Id, message = "Stored on ground" });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Store on ground failed: {detail}" });
            }
        }

        // =========================================================
        // GET: api/StoreMovement/ground-pallets
        // =========================================================
        //
        // Pallets currently waiting on the ground, in FIFO order
        // (FifoPalletNo, then MovementDate).
        //
        // For every part the number of free rack slots is checked:
        //   canMoveNow = fifoRank <= freeFront + freeRear
        //
        // The frontend shows "location available" when any pallet has
        // canMoveNow = true.
        //
        // =========================================================

        [HttpGet("ground-pallets")]
        public async Task<IActionResult> GetGroundPallets()
        {
            try
            {
                var ground = await _context.StoreMovements
                    .AsNoTracking()
                    .Where(m => m.IsGround && m.GrnPalletId != null && m.Quantity > 0)
                    .Include(m => m.GrnPallet)
                        .ThenInclude(p => p.GrnLine)
                            .ThenInclude(l => l.Item)
                    .Include(m => m.GrnPallet)
                        .ThenInclude(p => p.GrnLine)
                            .ThenInclude(l => l.Header)
                    .ToListAsync();

                var ordered = ground
                    .Where(m => m.GrnPallet?.GrnLine != null)
                    .OrderBy(m => m.GrnPallet.GrnLine.FifoPalletNo ?? "", StringComparer.Ordinal)
                    .ThenBy(m => m.MovementDate)
                    .ToList();

                var freeByItem = new Dictionary<int, (int Front, int Rear)>();
                foreach (var itemId in ordered.Select(m => m.GrnPallet.GrnLine.ItemId).Distinct())
                {
                    freeByItem[itemId] = await GetFreeSlotCountsAsync(itemId);
                }

                var rankByItem = new Dictionary<int, int>();
                var result = new List<object>();

                foreach (var m in ordered)
                {
                    var line = m.GrnPallet.GrnLine;
                    var itemId = line.ItemId;

                    rankByItem.TryGetValue(itemId, out var rank);
                    rank++;
                    rankByItem[itemId] = rank;

                    var free = freeByItem[itemId];

                    result.Add(new
                    {
                        movementId = m.Id,
                        palletId = m.GrnPalletId,
                        itemId,
                        partLabel = $"{line.Item.ItemNumber} - {line.Item.ItemName}",
                        palletNo = m.GrnPallet.PalletNo,
                        fifoPalletNo = line.FifoPalletNo,
                        grnNo = line.Header.GrnNumber,
                        quantity = m.Quantity,
                        movementDate = m.MovementDate,
                        fifoRank = rank,
                        freeFront = free.Front,
                        freeRear = free.Rear,
                        canMoveNow = rank <= (free.Front + free.Rear)
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Failed to load ground pallets: {detail}" });
            }
        }

        // =========================================================
        // POST: api/StoreMovement/move-ground-to-rack
        // =========================================================
        //
        // Moves a ground movement into a free rack slot.
        // FIFO is enforced: only the oldest ground pallet of the part
        // may be moved first.
        //
        // =========================================================

        public class MoveGroundToRackRequest
        {
            public int GroundMovementId { get; set; }
            public int RackRowId { get; set; }
            public int SlotNumber { get; set; }
            public string Side { get; set; } = "Front";
        }

        [HttpPost("move-ground-to-rack")]
        public async Task<IActionResult> MoveGroundToRack([FromBody] MoveGroundToRackRequest req)
        {
            try
            {
                if (req == null || req.GroundMovementId <= 0 || req.RackRowId <= 0 || req.SlotNumber <= 0)
                {
                    return BadRequest(new { message = "Ground movement, Rack Row and Slot Number are required" });
                }

                if (req.Side != "Front" && req.Side != "Rear")
                {
                    return BadRequest(new { message = "Side must be 'Front' or 'Rear'" });
                }

                var movement =
                    await _context.StoreMovements
                        .Include(m => m.GrnPallet)
                            .ThenInclude(p => p.GrnLine)
                        .FirstOrDefaultAsync(m => m.Id == req.GroundMovementId);

                if (movement == null || !movement.IsGround)
                {
                    return BadRequest(new { message = "Ground movement not found (it may already be moved)" });
                }

                var itemId = movement.GrnPallet.GrnLine.ItemId;

                // ---- FIFO check ----
                var others = await _context.StoreMovements
                    .AsNoTracking()
                    .Where(m =>
                        m.IsGround &&
                        m.Id != movement.Id &&
                        m.Quantity > 0 &&
                        m.GrnPallet.GrnLine.ItemId == itemId)
                    .Select(m => new
                    {
                        m.MovementDate,
                        m.GrnPallet.PalletNo,
                        Fifo = m.GrnPallet.GrnLine.FifoPalletNo
                    })
                    .ToListAsync();

                var myFifo = movement.GrnPallet.GrnLine.FifoPalletNo ?? "";

                var older = others
                    .Where(o =>
                    {
                        var c = string.CompareOrdinal(o.Fifo ?? "", myFifo);
                        return c < 0 || (c == 0 && o.MovementDate < movement.MovementDate);
                    })
                    .OrderBy(o => o.Fifo ?? "", StringComparer.Ordinal)
                    .ThenBy(o => o.MovementDate)
                    .FirstOrDefault();

                if (older != null)
                {
                    return BadRequest(new
                    {
                        message = $"FIFO: ground pallet {older.PalletNo} must be moved to the rack first."
                    });
                }

                // ---- Row / store / side checks ----
                var row =
                    await _context.RackRows
                        .Include(r => r.Column)
                            .ThenInclude(c => c.Rack)
                                .ThenInclude(rk => rk.Store)
                                    .ThenInclude(lm => lm.StoreMaster)
                        .FirstOrDefaultAsync(r => r.Id == req.RackRowId);

                if (row == null)
                {
                    return BadRequest(new { message = "Rack Row not found" });
                }

                var storePartId = row.Column?.Rack?.Store?.StoreMaster?.PartNumberId;

                if (storePartId.HasValue && storePartId.Value != itemId)
                {
                    return BadRequest(new
                    {
                        message = "This store location is configured for a different Part Number."
                    });
                }

                var sideEnabled = req.Side == "Front" ? row.HasFront == true : row.HasRear == true;
                if (!sideEnabled)
                {
                    return BadRequest(new { message = $"This row has no {req.Side} side" });
                }

                var slotOccupied =
                    await _context.StoreMovements
                        .AnyAsync(m =>
                            m.RackRowId == req.RackRowId &&
                            m.SlotNumber == req.SlotNumber &&
                            m.Side == req.Side);

                if (slotOccupied)
                {
                    return BadRequest(new { message = "That slot is already occupied" });
                }

                // MovementDate is kept so FIFO order is not lost.
                movement.RackRowId = req.RackRowId;
                movement.SlotNumber = req.SlotNumber;
                movement.Side = req.Side;
                movement.IsGround = false;

                await _context.SaveChangesAsync();

                return Ok(new { movement.Id, message = "Moved from ground to rack" });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Move to rack failed: {detail}" });
            }
        }

        // =========================================================
        // POST: api/StoreMovement/stuff   (legacy store positions)
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
        public async Task<IActionResult> Stuff([FromBody] StuffRequest req)
        {
            try
            {
                if (req == null || req.GrnPalletId <= 0 || req.StorePositionId <= 0)
                {
                    return BadRequest(new { message = "Pallet and Store Position are required" });
                }

                if (req.Side != "Front" && req.Side != "Rear")
                {
                    return BadRequest(new { message = "Side must be 'Front' or 'Rear'" });
                }

                if (req.Quantity <= 0)
                {
                    return BadRequest(new { message = "Quantity must be greater than 0" });
                }

                var pallet = await _context.GrnPallets.FindAsync(req.GrnPalletId);

                if (pallet == null)
                {
                    return BadRequest(new { message = "Pallet not found" });
                }

                var position = await _context.StorePositions.FindAsync(req.StorePositionId);

                if (position == null)
                {
                    return BadRequest(new { message = "Store Position not found" });
                }

                var alreadyStuffed =
                    await _context.StoreMovements
                        .Where(m => m.GrnPalletId == req.GrnPalletId)
                        .SumAsync(m => (decimal?)m.Quantity) ?? 0;

                var remaining = pallet.Quantity - alreadyStuffed;

                if (req.Quantity > remaining)
                {
                    return BadRequest(new { message = $"Only {remaining} remaining on this pallet" });
                }

                var positionStuffed =
                    await _context.StoreMovements
                        .Where(m => m.StorePositionId == req.StorePositionId)
                        .SumAsync(m => (decimal?)m.Quantity) ?? 0;

                var available = position.Capacity - positionStuffed;

                if (req.Quantity > available)
                {
                    return BadRequest(new
                    {
                        message = $"Only {available} available space in {position.PositionCode}"
                    });
                }

                var movement = new StoreMovement
                {
                    GrnPalletId = req.GrnPalletId,
                    StorePositionId = req.StorePositionId,
                    Side = req.Side,
                    Quantity = req.Quantity,
                    MovementDate = DateTime.Now,
                    CreatedBy = req.CreatedBy?.Trim()
                };

                _context.StoreMovements.Add(movement);
                await _context.SaveChangesAsync();

                return Ok(new { movement.Id, message = "Stuffed Successfully" });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Stuff failed: {detail}" });
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
                var movement = await _context.StoreMovements.FindAsync(id);

                if (movement == null)
                {
                    return NotFound(new { message = "Movement not found" });
                }

                _context.StoreMovements.Remove(movement);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Movement Undone" });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Undo failed: {detail}" });
            }
        }

        // =========================================================
        // GET: api/StoreMovement/all-pallets-status
        // Used by Store Verification (IN_STOCK / ISSUED).
        // =========================================================

        [HttpGet("all-pallets-status")]
        public async Task<IActionResult> GetAllPalletsWithStatus()
        {
            try
            {
                var stuffedIds =
                    await _context.StoreMovements
                        .Where(m => m.GrnPalletId != null)
                        .Select(m => m.GrnPalletId!.Value)
                        .Distinct()
                        .ToListAsync();

                var issuedIds =
                    await _context.MaterialIssues
                        .Where(x => x.GrnPalletId.HasValue)
                        .Select(x => x.GrnPalletId!.Value)
                        .Distinct()
                        .ToListAsync();

                var issuedSet = new HashSet<int>(issuedIds);

                var relevantIds = stuffedIds.Union(issuedIds).Distinct().ToList();

                var pallets =
                    await _context.GrnPallets
                        .Where(p => relevantIds.Contains(p.Id))
                        .Include(p => p.GrnLine)
                            .ThenInclude(l => l.Item)
                        .Include(p => p.GrnLine)
                            .ThenInclude(l => l.Header)
                        .Select(p => new
                        {
                            id = p.Id,
                            itemId = p.GrnLine.ItemId,
                            partLabel = p.GrnLine.Item.ItemNumber + " - " + p.GrnLine.Item.ItemName,
                            palletNo = p.PalletNo,
                            fifoPalletNo = p.GrnLine.FifoPalletNo,
                            grnNo = p.GrnLine.Header.GrnNumber,
                            quantity = p.Quantity
                        })
                        .ToListAsync();

                var result =
                    pallets.Select(p => new
                    {
                        p.id,
                        p.itemId,
                        p.partLabel,
                        p.palletNo,
                        p.fifoPalletNo,
                        p.grnNo,
                        p.quantity,
                        status = issuedSet.Contains(p.id) ? "ISSUED" : "IN_STOCK"
                    });

                return Ok(result);
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Failed to load pallet status: {detail}" });
            }
        }
    }
}

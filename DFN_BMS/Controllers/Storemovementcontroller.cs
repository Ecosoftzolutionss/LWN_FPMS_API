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

        // GET: api/StoreMovement/available-pallets
        //
        // Returns every pallet that has actually been stuffed into the store
        // (i.e. has at least one STORE_MOVEMENT record), joined back through
        // GRN_PALLET -> GRN_LINE -> GRN_HEADER, EXCLUDING any pallet that
        // already has a MaterialIssue record.
        //
        // FIX (this version): previously this list never excluded
        // already-issued pallets, so P001 kept coming back as "available"
        // forever, even after being Issued and synced. The server is now
        // the source of truth for what's actually still in stock.
        //
        // FIX (this version, #2): now also returns each pallet's real
        // GrnNumber (from GrnHeader). Without this, the mobile app had no
        // way to verify a scanned label's GRN claim against real data, so
        // a scanned QR encoding a GRN that was never created (or doesn't
        // belong to that pallet) could still show up in the Material
        // Issue grid with a fabricated GRN number. The client now prefers
        // this server-verified grnNo over whatever the raw scan said.
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

                // Do NOT exclude a pallet merely because it has a
                // MaterialIssue record. A pallet can be partially issued
                // multiple times. StoreMovements.Quantity is the physical
                // quantity still present in the store.
                //
                // Group by pallet so Front+Rear (or any multi-row stuffing)
                // collapses into one entry per pallet.
                var grouped = movements
                    .GroupBy(m => m.GrnPalletId)
                    .Select(g =>
                    {
                        var first = g.OrderBy(m => m.MovementDate).First();
                        var pallet = first.GrnPallet;
                        var line = pallet.GrnLine;

                        // Resolve location: StorePosition path OR RackRow path.
                        string location = first.StorePosition?.Store?.StoreLocation;

                        if (location == null && first.RackRow != null)
                        {
                            location = first.RackRow.Column?.Rack?.Store?.StoreMaster?.StoreLocation;
                        }

                        return new
                        {
                            // The pallet's real database primary key.
                            // PalletNo and FifoPalletNo are just
                            // human-readable labels generated from a
                            // sequence that gets reset (see
                            // PALLET_TYPE_MASTER.CurrentSequence) — they
                            // are NOT globally unique and get recycled
                            // across different GRNs. `id` is the only
                            // safe identity to match/dedupe on.
                            id = g.Key,
                            itemId = line.ItemId,
                            partLabel = $"{line.Item.ItemNumber} - {line.Item.ItemName}",
                            storeLocation = location,
                            palletNo = pallet.PalletNo,
                            fifoPalletNo = line.FifoPalletNo,
                            // Real, verified GRN number this pallet belongs
                            // to — used by the client to catch scanned
                            // labels claiming a GRN that doesn't match.
                            grnNo = line.Header.GrnNumber,
                            movementDate = g.Min(m => m.MovementDate),
                            quantity = g.Sum(m => m.Quantity),
                            type = string.Equals(line.Header.GrnType, "Regular", StringComparison.OrdinalIgnoreCase)
                                ? "REGULAR"
                                : "SAMPLE"
                        };
                    })
                    // Only pallets with positive physical stock are available.
                    .Where(r => r.quantity > 0)
                    // TRUE FIFO: earliest movement first.
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

        // GET: api/StoreMovement/grn/5/pallets
        // GET: api/StoreMovement/grn/5/pallets
        [HttpGet("grn/{grnId}/pallets")]
        public async Task<IActionResult> GetPalletsForGrn(int grnId)
        {
            try
            {
                // IMPORTANT:
                // Only POSTED GRN lines are allowed to appear as pallets
                // in Store Movement.
                var lines = await _context.GrnLines
                    .Include(x => x.Item)
                    .Where(x => x.GrnHeaderId == grnId && x.IsPosted)
                    .ToListAsync();

                if (lines.Count == 0)
                {
                    return NotFound(new
                    {
                        message = "No posted pallets found for this GRN"
                    });
                }

                // Create GrnPallet only for POSTED lines
                // Create GrnPallet only for POSTED lines
                foreach (var line in lines)
                {
                    var exists = await _context.GrnPallets
                        .AnyAsync(p => p.GrnLineId == line.Id);

                    if (!exists)
                    {
                        // IMPORTANT:
                        // Do NOT generate P001/P002/P003 here.
                        // GRN POST has already generated the real pallet number.
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

                        await _context.SaveChangesAsync();
                    }
                }

                // Return ONLY pallets belonging to POSTED GRN lines
                var pallets = await _context.GrnPallets
                    .Include(x => x.GrnLine)
                        .ThenInclude(l => l.Item)
                    .Where(x =>
                        x.GrnLine.GrnHeaderId == grnId &&
                        x.GrnLine.IsPosted)
                    .OrderBy(x => x.PalletNo)
                    .Select(x => new
                    {
                        x.Id,
                        x.PalletNo,
                        x.Quantity,
                        x.Rate,

                        PartNumber = x.GrnLine.Item.ItemNumber,
                        PartName = x.GrnLine.Item.ItemName,

                        StuffedQty = _context.StoreMovements
                            .Where(m => m.GrnPalletId == x.Id)
                            .Sum(m => (decimal?)m.Quantity) ?? 0,

                        Assignments = _context.StoreMovements
                            .Where(m =>
                                m.GrnPalletId == x.Id &&
                                m.StorePositionId != null)
                            .Select(m => new
                            {
                                m.Id,
                                StoreLocation = m.StorePosition.Store.StoreLocation,
                                PositionCode = m.StorePosition.PositionCode,
                                m.Side,
                                m.Quantity
                            })
                            .ToList()
                    })
                    .ToListAsync();

                return Ok(pallets);
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;

                return StatusCode(500, new
                {
                    message = $"Failed to load pallets: {detail}"
                });
            }
        }



        // GET: api/StoreMovement/positions
        [HttpGet("positions")]
        public async Task<IActionResult> GetPositions()
        {
            try
            {
                var stores = await _context.StoreMasters.ToListAsync();

                foreach (var store in stores)
                {
                    var hasPositions = await _context.StorePositions.AnyAsync(p => p.StoreMasterId == store.Id);
                    if (!hasPositions)
                    {
                        _context.StorePositions.Add(new StorePosition { StoreMasterId = store.Id, PositionCode = "P1", Capacity = 5000 });
                        _context.StorePositions.Add(new StorePosition { StoreMasterId = store.Id, PositionCode = "P2", Capacity = 5000 });
                        await _context.SaveChangesAsync();
                    }
                }

                var result = await _context.StoreMasters
                    .Select(s => new
                    {
                        s.Id,
                        s.StoreLocation,
                        Positions = _context.StorePositions
                            .Where(p => p.StoreMasterId == s.Id)
                            .OrderBy(p => p.PositionCode)
                            .Select(p => new
                            {
                                p.Id,
                                p.PositionCode,
                                p.Capacity,
                                Stuffed = _context.StoreMovements
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

        // GET: api/StoreMovement/rack-slots?itemId=5
        // Only returns stores whose Store Master configuration is for this
        // exact Part Number — a pallet can only be stuffed into the store it
        // was configured for, not any store in the system.
        [HttpGet("rack-slots")]
        public async Task<IActionResult> GetRackSlots([FromQuery] int? itemId)
        {
            try
            {
                var query = _context.LocationMasters
                    .Include(x => x.StoreMaster)
                    .Include(x => x.Racks)
                        .ThenInclude(r => r.Columns)
                            .ThenInclude(c => c.Rows)
                    .AsQueryable();

                if (itemId.HasValue)
                    query = query.Where(x => x.StoreMaster.PartNumberId == itemId.Value);

                var stores = await query
                    .Select(store => new
                    {
                        store.Id,
                        store.StoreCode,
                        StoreLocation = store.StoreMaster.StoreLocation,
                        Racks = store.Racks.Select(rack => new
                        {
                            rack.Id,
                            rack.RackNo,
                            Columns = rack.Columns.Select(col => new
                            {
                                col.Id,
                                col.ColumnNo,
                                Rows = col.Rows.Select(row => new
                                {
                                    row.Id,
                                    row.RowNo,
                                    row.HasFront,
                                    row.HasRear,
                                    row.Fixture,
                                    OccupiedSlots = _context.StoreMovements
                                        .Where(m => m.RackRowId == row.Id)
                                        .Select(m => new
                                        {
                                            m.SlotNumber,
                                            m.Side,
                                            m.Quantity,
                                            m.Id,
                                            m.GrnPalletId,
                                            PalletNo = m.GrnPallet != null ? m.GrnPallet.PalletNo : null
                                        })
                                        .ToList()
                                }).ToList()
                            }).ToList()
                        }).ToList()
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

        // POST: api/StoreMovement/stuff-rack-slot
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
                    return BadRequest(new { message = "Pallet, Rack Row and Slot Number are required" });

                if (req.Side != "Front" && req.Side != "Rear")
                    return BadRequest(new { message = "Side must be 'Front' or 'Rear'" });

                if (req.Quantity <= 0)
                    return BadRequest(new { message = "Quantity must be greater than 0" });




                var pallet = await _context.GrnPallets
    .Include(p => p.GrnLine)
    .FirstOrDefaultAsync(p => p.Id == req.GrnPalletId);
                if (pallet == null)
                    return BadRequest(new { message = "Pallet not found" });

                // NEW: block re-stuffing a pallet that has already been issued out.
                var alreadyIssued = await _context.MaterialIssues
                    .AnyAsync(x => x.GrnPalletId == req.GrnPalletId);
                if (alreadyIssued)
                {
                    return BadRequest(new
                    {
                        message = $"Pallet {pallet.PalletNo} has already been issued and cannot be stuffed again."
                    });
                }


                var row = await _context.RackRows
                    .Include(r => r.Column)
                        .ThenInclude(c => c.Rack)
                            .ThenInclude(rk => rk.Store)
                                .ThenInclude(lm => lm.StoreMaster)
                    .FirstOrDefaultAsync(r => r.Id == req.RackRowId);
                if (row == null)
                    return BadRequest(new { message = "Rack Row not found" });

                // NEW: block stuffing a pallet into a store configured for a
                // different Part Number.
                var storeConfiguredPartId = row.Column?.Rack?.Store?.StoreMaster?.PartNumberId;
                if (storeConfiguredPartId.HasValue && storeConfiguredPartId.Value != pallet.GrnLine.ItemId)
                {
                    return BadRequest(new
                    {
                        message = "This store location is configured for a different Part Number. Choose a slot in the correct store."
                    });
                }

                var alreadyStuffed = await _context.StoreMovements
                    .Where(m => m.GrnPalletId == req.GrnPalletId)
                    .SumAsync(m => (decimal?)m.Quantity) ?? 0;

                var remaining = pallet.Quantity - alreadyStuffed;

                if (req.Quantity > remaining)
                    return BadRequest(new { message = $"Only {remaining} remaining on this pallet" });

                var slotOccupied = await _context.StoreMovements
                    .AnyAsync(m => m.RackRowId == req.RackRowId && m.SlotNumber == req.SlotNumber && m.Side == req.Side);

                if (slotOccupied)
                    return BadRequest(new { message = "That slot is already occupied" });

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

        // POST: api/StoreMovement/stuff
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
                    return BadRequest(new { message = "Pallet and Store Position are required" });

                if (req.Side != "Front" && req.Side != "Rear")
                    return BadRequest(new { message = "Side must be 'Front' or 'Rear'" });

                if (req.Quantity <= 0)
                    return BadRequest(new { message = "Quantity must be greater than 0" });

                var pallet = await _context.GrnPallets.FindAsync(req.GrnPalletId);
                if (pallet == null)
                    return BadRequest(new { message = "Pallet not found" });

                var position = await _context.StorePositions.FindAsync(req.StorePositionId);
                if (position == null)
                    return BadRequest(new { message = "Store Position not found" });

                var alreadyStuffed = await _context.StoreMovements
                    .Where(m => m.GrnPalletId == req.GrnPalletId)
                    .SumAsync(m => (decimal?)m.Quantity) ?? 0;

                var remaining = pallet.Quantity - alreadyStuffed;

                if (req.Quantity > remaining)
                    return BadRequest(new { message = $"Only {remaining} remaining on this pallet" });

                var positionStuffed = await _context.StoreMovements
                    .Where(m => m.StorePositionId == req.StorePositionId)
                    .SumAsync(m => (decimal?)m.Quantity) ?? 0;

                var available = position.Capacity - positionStuffed;

                if (req.Quantity > available)
                    return BadRequest(new { message = $"Only {available} available space in {position.PositionCode}" });

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

        // DELETE: api/StoreMovement/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Undo(int id)
        {
            try
            {
                var movement = await _context.StoreMovements.FindAsync(id);

                if (movement == null)
                    return NotFound(new { message = "Movement not found" });

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

        // GET: api/StoreMovement/all-pallets-status
        //
        // Broader than available-pallets: includes pallets currently in
        // stock (stuffed) AND pallets that have since been issued out,
        // each tagged with a status. Used by Store Verification, which
        // needs to tell the difference between "never existed / not
        // stuffed yet" and "existed but was already issued" — the two
        // look identical if we only ever look at available-pallets.
        [HttpGet("all-pallets-status")]
        public async Task<IActionResult> GetAllPalletsWithStatus()
        {
            try
            {
                var stuffedIds = await _context.StoreMovements
                    .Where(m => m.GrnPalletId != null)
                    .Select(m => m.GrnPalletId!.Value)
                    .Distinct()
                    .ToListAsync();

                var issuedIds = await _context.MaterialIssues
                    .Where(x => x.GrnPalletId.HasValue)
                    .Select(x => x.GrnPalletId!.Value)
                    .Distinct()
                    .ToListAsync();

                var issuedSet = new HashSet<int>(issuedIds);
                var relevantIds = stuffedIds.Union(issuedIds).Distinct().ToList();

                var pallets = await _context.GrnPallets
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

                var result = pallets.Select(p => new
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
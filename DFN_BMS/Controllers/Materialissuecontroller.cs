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
    public class MaterialIssueController : ControllerBase
    {
        private readonly AppDbContext _context;

        public MaterialIssueController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // GET ALL - ONE ROW PER ISSUE SLIP
        // One slip can contain material from multiple GRNs.
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                // Load the issue rows first and group in memory. This also
                // lets us safely build a distinct list of GRNs per slip.
                var records = await _context.MaterialIssues
                    .AsNoTracking()
                    .Where(x => !string.IsNullOrWhiteSpace(x.IssueNumber))
                    .OrderByDescending(x => x.Id)
                    .ToListAsync();

                var list = records
                    .GroupBy(x => x.IssueNumber)
                    .Select(g => new
                    {
                        Id = g.Max(x => x.Id),
                        IssueNumber = g.Key,

                        GrnNumbers = g
                            .Select(x => x.GrnNumber)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),

                        IssueDate = g
                            .OrderByDescending(x => x.Id)
                            .Select(x => x.IssueDate)
                            .FirstOrDefault(),

                        Quantity = g.Sum(x => x.Quantity),

                        IssuedTo = g
                            .OrderByDescending(x => x.Id)
                            .Select(x => x.IssuedTo)
                            .FirstOrDefault(),

                        IssuedBy = g
                            .OrderByDescending(x => x.Id)
                            .Select(x => x.IssuedBy)
                            .FirstOrDefault(),

                        StoreLocation = g
                            .OrderByDescending(x => x.Id)
                            .Select(x => x.StoreLocation)
                            .FirstOrDefault(),

                        Remarks = g
                            .OrderByDescending(x => x.Id)
                            .Select(x => x.Remarks)
                            .FirstOrDefault()
                    })
                    .OrderByDescending(x => x.Id)
                    .ToList();

                return Ok(list);
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;

                return StatusCode(500, new
                {
                    message = $"Failed to load Material Issue records: {detail}"
                });
            }
        }

        // =========================================================
        // GET SINGLE MATERIAL ISSUE SLIP
        // One IssueNumber = one slip.
        // A slip can contain multiple GRNs and multiple pallets.
        // =========================================================
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                var selected = await _context.MaterialIssues
                    .AsNoTracking()
                    .Where(x => x.Id == id)
                    .Select(x => new
                    {
                        x.Id,
                        x.IssueNumber,
                        x.IssuedTo,
                        x.IssuedBy,
                        x.StoreLocation,
                        x.Remarks,
                        x.IssueDate,
                        x.CreatedDate
                    })
                    .FirstOrDefaultAsync();

                if (selected == null)
                {
                    return NotFound(new
                    {
                        message = "Material Issue record not found"
                    });
                }

                // All rows with the same IssueNumber belong to the same
                // printed Material Issue Slip.
                var issueRows = await _context.MaterialIssues
                    .AsNoTracking()
                    .Where(x => x.IssueNumber == selected.IssueNumber)
                    .OrderBy(x => x.Id)
                    .Select(x => new
                    {
                        x.Id,
                        x.ItemId,
                        PartNumber = x.Item != null
                            ? x.Item.ItemNumber
                            : null,
                        PartName = x.Item != null
                            ? x.Item.ItemName
                            : null,
                        x.Quantity,
                        x.PalletNo,
                        x.GrnNumber,
                        x.GrnPalletId
                    })
                    .ToListAsync();

                var grnNumbers = issueRows
                    .Select(x => x.GrnNumber)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Get FIFO pallet numbers from the GRN line linked to each
                // issued pallet. FIFO-Pallet-No is stored on GrnLine.
                var palletIds = issueRows
                    .Where(x => x.GrnPalletId.HasValue)
                    .Select(x => x.GrnPalletId!.Value)
                    .Distinct()
                    .ToList();

                var palletFifoMap = await _context.GrnPallets
                    .AsNoTracking()
                    .Where(p => palletIds.Contains(p.Id))
                    .Select(p => new
                    {
                        p.Id,
                        FifoPalletNo = p.GrnLine != null
                            ? p.GrnLine.FifoPalletNo
                            : null
                    })
                    .ToDictionaryAsync(
                        x => x.Id,
                        x => x.FifoPalletNo
                    );

                var items = issueRows.Select(x =>
                {
                    string? fifoPalletNo = null;

                    if (x.GrnPalletId.HasValue)
                    {
                        palletFifoMap.TryGetValue(
                            x.GrnPalletId.Value,
                            out fifoPalletNo
                        );
                    }

                    return new
                    {
                        x.Id,
                        x.ItemId,
                        x.PartNumber,
                        x.PartName,
                        x.Quantity,
                        x.PalletNo,
                        x.GrnNumber,
                        x.GrnPalletId,

                        // FIFO-PALLET-NO is the FIFO number generated
                        // against the GRN line.
                        FifoPalletNo = fifoPalletNo,
                        FifoNo = fifoPalletNo,
                        FifoNumber = fifoPalletNo
                    };
                }).ToList();

                // Supplier is not required by the current slip because the
                // FROM/TO section is intentionally hidden. Keep the field
                // available as null for backward compatibility.
                return Ok(new
                {
                    selected.Id,
                    selected.IssueNumber,
                    GrnNumber = grnNumbers.FirstOrDefault(),
                    GrnNumbers = grnNumbers,
                    selected.IssuedTo,
                    selected.IssuedBy,
                    selected.StoreLocation,
                    selected.Remarks,
                    selected.IssueDate,
                    selected.CreatedDate,
                    Supplier = (object?)null,
                    TotalQuantity = items.Sum(x => x.Quantity),
                    Items = items
                });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;

                return StatusCode(500, new
                {
                    message = $"Failed to load Material Issue Slip: {detail}"
                });
            }
        }

        // =========================================================
        // CREATE MATERIAL ISSUE
        //
        // IMPORTANT:
        // - StoreMovements is the source of truth for available stock.
        // - MaterialIssue is the outward transaction history.
        // - A pallet CAN be issued more than once while stock remains.
        // - Only the requested quantity is removed from StoreMovement.
        // - This supports partial pallet issue.
        //
        // Example:
        //     Pallet stock = 10
        //     Issue 3     -> remaining 7
        //     Issue 4     -> remaining 3
        //     Issue 3     -> remaining 0
        //
        // IDEMPOTENCY:
        // - model.IdempotencyKey is generated ONCE on the client when a
        //   pending issue is first queued (device id + pallet id +
        //   timestamp) and stays attached to that offline record for
        //   its whole life. If Data Sync retries the same record
        //   (e.g. the upload succeeded but the response never reached
        //   the client), the SAME key arrives again.
        // - We check for an existing row with that key BEFORE opening
        //   the stock-mutating transaction. If found, we return success
        //   again without touching StoreMovements a second time.
        // - Older/queued records with no key (pre-upgrade clients, or
        //   the generic/no-pallet path if it's ever used without a
        //   key) simply skip this check and fall through to the
        //   normal flow — the StoreMovements quantity check further
        //   down still protects against real over-issuing in that
        //   case, just without single-record replay protection.
        // =========================================================
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] MaterialIssue model)
        {
            // ---------------------------------------------------------
            // BASIC VALIDATION
            // ---------------------------------------------------------
            if (model == null)
            {
                return BadRequest(new
                {
                    message = "Request body is required."
                });
            }

            if (model.ItemId <= 0 ||
                model.Quantity <= 0 ||
                string.IsNullOrWhiteSpace(model.IssuedTo) ||
                string.IsNullOrWhiteSpace(model.IssuedBy))
            {
                return BadRequest(new
                {
                    message =
                        "Part Number, Quantity, Issued To and Issued By are required."
                });
            }

            var itemExists = await _context.ItemMasters
                .AsNoTracking()
                .AnyAsync(x => x.Id == model.ItemId);

            if (!itemExists)
            {
                return BadRequest(new
                {
                    message = "Selected Part Number does not exist."
                });
            }

            // ---------------------------------------------------------
            // IDEMPOTENCY CHECK — outside the transaction, before any
            // stock mutation is even considered. AsNoTracking because
            // we only need to read; nothing here is updated.
            // ---------------------------------------------------------
            var idempotencyKey = string.IsNullOrWhiteSpace(model.IdempotencyKey)
                ? null
                : model.IdempotencyKey.Trim();

            if (idempotencyKey != null)
            {
                var existing = await _context.MaterialIssues
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey);

                if (existing != null)
                {
                    return Ok(new
                    {
                        id = existing.Id,
                        issueNumber = existing.IssueNumber,
                        issuedQuantity = existing.Quantity,
                        palletNo = existing.PalletNo,
                        grnPalletId = existing.GrnPalletId,
                        message = "Material issue already recorded (idempotent replay)."
                    });
                }
            }

            // =========================================================
            // IMPORTANT
            // =========================================================
            // Your SQL Server DbContext uses a retrying execution
            // strategy. Therefore BeginTransactionAsync() MUST be
            // executed inside CreateExecutionStrategy().
            //
            // Do NOT return IActionResult directly from the async
            // strategy lambda. That causes CS8031 in some EF Core
            // versions because the selected ExecuteAsync overload is
            // Task-returning.
            //
            // Instead, store the result in 'result' and return it after
            // ExecuteAsync completes.
            // =========================================================

            IActionResult? result = null;

            var strategy = _context.Database.CreateExecutionStrategy();

            try
            {
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(
                            System.Data.IsolationLevel.Serializable);

                    try
                    {
                        // -------------------------------------------------
                        // SECOND, RACE-SAFE IDEMPOTENCY CHECK
                        //
                        // The AsNoTracking check above runs before the
                        // transaction opens, so two near-simultaneous
                        // retries of the SAME record (rare, but possible
                        // if a sync is somehow triggered twice in quick
                        // succession) could both pass it. Re-checking
                        // here, inside the Serializable transaction,
                        // closes that gap: the unique index on
                        // IdempotencyKey means the second SaveChangesAsync
                        // below will throw a uniqueness violation if a
                        // true race occurs, but checking again here keeps
                        // the common case cheap and avoids surfacing that
                        // as a scary 500 error to the client.
                        // -------------------------------------------------
                        if (idempotencyKey != null)
                        {
                            var existingInTx = await _context.MaterialIssues
                                .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey);

                            if (existingInTx != null)
                            {
                                result = Ok(new
                                {
                                    id = existingInTx.Id,
                                    issueNumber = existingInTx.IssueNumber,
                                    issuedQuantity = existingInTx.Quantity,
                                    palletNo = existingInTx.PalletNo,
                                    grnPalletId = existingInTx.GrnPalletId,
                                    message = "Material issue already recorded (idempotent replay)."
                                });

                                await transaction.RollbackAsync();
                                return;
                            }
                        }

                        // =================================================
                        // PALLET-BASED ISSUE
                        // =================================================
                        if (model.GrnPalletId.HasValue)
                        {
                            var pallet = await _context.GrnPallets
                                .Include(p => p.GrnLine)
                                .FirstOrDefaultAsync(
                                    p => p.Id == model.GrnPalletId.Value);

                            if (pallet == null)
                            {
                                result = BadRequest(new
                                {
                                    message = "Selected pallet does not exist."
                                });

                                await transaction.RollbackAsync();
                                return;
                            }

                            if (pallet.GrnLine == null)
                            {
                                result = BadRequest(new
                                {
                                    message =
                                        "Selected pallet is not linked to a GRN line."
                                });

                                await transaction.RollbackAsync();
                                return;
                            }

                            if (pallet.GrnLine.ItemId != model.ItemId)
                            {
                                result = BadRequest(new
                                {
                                    message =
                                        "Selected pallet does not belong to the selected Part Number."
                                });

                                await transaction.RollbackAsync();
                                return;
                            }

                            // -------------------------------------------------
                            // StoreMovement = CURRENT PALLET STOCK
                            // -------------------------------------------------
                            var movements = await _context.StoreMovements
                                .Where(x =>
                                    x.GrnPalletId == model.GrnPalletId.Value &&
                                    x.Quantity > 0)
                                .OrderBy(x => x.MovementDate)
                                .ThenBy(x => x.Id)
                                .ToListAsync();

                            var availableQty = movements.Sum(x => x.Quantity);

                            if (availableQty <= 0)
                            {
                                result = BadRequest(new
                                {
                                    message =
                                        $"Pallet {pallet.PalletNo} has no stock remaining."
                                });

                                await transaction.RollbackAsync();
                                return;
                            }

                            if (model.Quantity > availableQty)
                            {
                                result = BadRequest(new
                                {
                                    message =
                                        $"Only {availableQty:0.###} quantity is available on pallet {pallet.PalletNo}."
                                });

                                await transaction.RollbackAsync();
                                return;
                            }

                            // -------------------------------------------------
                            // CREATE MATERIAL ISSUE
                            // -------------------------------------------------
                            var entity = new MaterialIssue
                            {
                                IssueNumber =
                                    string.IsNullOrWhiteSpace(model.IssueNumber)
                                        ? await GenerateIssueNumberAsync()
                                        : model.IssueNumber.Trim(),

                                ItemId = model.ItemId,

                                Quantity = model.Quantity,

                                IssuedTo = model.IssuedTo.Trim(),

                                IssuedBy = model.IssuedBy.Trim(),

                                StoreLocation =
                                    string.IsNullOrWhiteSpace(model.StoreLocation)
                                        ? null
                                        : model.StoreLocation.Trim(),

                                PalletNo =
                                    string.IsNullOrWhiteSpace(model.PalletNo)
                                        ? pallet.PalletNo
                                        : model.PalletNo.Trim(),

                                GrnPalletId = model.GrnPalletId,

                                GrnNumber =
                                    string.IsNullOrWhiteSpace(model.GrnNumber)
                                        ? null
                                        : model.GrnNumber.Trim(),

                                Remarks =
                                    string.IsNullOrWhiteSpace(model.Remarks)
                                        ? null
                                        : model.Remarks.Trim(),

                                IdempotencyKey = idempotencyKey,

                                DeviceId =
                                    string.IsNullOrWhiteSpace(model.DeviceId)
                                        ? null
                                        : model.DeviceId.Trim(),

                                IssueDate = DateTime.Now,

                                CreatedDate = DateTime.Now
                            };

                            _context.MaterialIssues.Add(entity);

                            // -------------------------------------------------
                            // REDUCE ONLY THE ISSUED QUANTITY
                            //
                            // Example:
                            // pallet stock = 20
                            // issue = 5
                            // remaining stock = 15
                            // -------------------------------------------------
                            decimal remainingToIssue = model.Quantity;

                            foreach (var movement in movements)
                            {
                                if (remainingToIssue <= 0)
                                    break;

                                if (movement.Quantity <= remainingToIssue)
                                {
                                    remainingToIssue -= movement.Quantity;
                                    _context.StoreMovements.Remove(movement);
                                }
                                else
                                {
                                    movement.Quantity -= remainingToIssue;
                                    remainingToIssue = 0;
                                }
                            }

                            if (remainingToIssue > 0)
                            {
                                result = BadRequest(new
                                {
                                    message =
                                        "Unable to allocate the requested quantity from pallet stock."
                                });

                                await transaction.RollbackAsync();
                                return;
                            }

                            await _context.SaveChangesAsync();
                            await transaction.CommitAsync();

                            result = Ok(new
                            {
                                id = entity.Id,
                                issueNumber = entity.IssueNumber,
                                issuedQuantity = entity.Quantity,
                                palletNo = entity.PalletNo,
                                grnPalletId = entity.GrnPalletId,
                                remainingStock =
                                    availableQty - model.Quantity,
                                message = "Material issued successfully."
                            });

                            return;
                        }

                        // =================================================
                        // GENERIC ISSUE WITHOUT PALLET
                        // =================================================
                        var genericEntity = new MaterialIssue
                        {
                            IssueNumber =
                                string.IsNullOrWhiteSpace(model.IssueNumber)
                                    ? await GenerateIssueNumberAsync()
                                    : model.IssueNumber.Trim(),

                            ItemId = model.ItemId,

                            Quantity = model.Quantity,

                            IssuedTo = model.IssuedTo.Trim(),

                            IssuedBy = model.IssuedBy.Trim(),

                            StoreLocation =
                                string.IsNullOrWhiteSpace(model.StoreLocation)
                                    ? null
                                    : model.StoreLocation.Trim(),

                            PalletNo =
                                string.IsNullOrWhiteSpace(model.PalletNo)
                                    ? null
                                    : model.PalletNo.Trim(),

                            GrnNumber =
                                string.IsNullOrWhiteSpace(model.GrnNumber)
                                    ? null
                                    : model.GrnNumber.Trim(),

                            Remarks =
                                string.IsNullOrWhiteSpace(model.Remarks)
                                    ? null
                                    : model.Remarks.Trim(),

                            IdempotencyKey = idempotencyKey,

                            DeviceId =
                                string.IsNullOrWhiteSpace(model.DeviceId)
                                    ? null
                                    : model.DeviceId.Trim(),

                            IssueDate = DateTime.Now,

                            CreatedDate = DateTime.Now
                        };

                        _context.MaterialIssues.Add(genericEntity);

                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        result = Ok(new
                        {
                            id = genericEntity.Id,
                            issueNumber = genericEntity.IssueNumber,
                            issuedQuantity = genericEntity.Quantity,
                            message = "Material issued successfully."
                        });
                    }
                    catch
                    {
                        throw;
                    }
                });

                return result ?? StatusCode(500, new
                {
                    message = "Material Issue operation did not return a result."
                });
            }
            catch (DbUpdateException ex) when (
                idempotencyKey != null &&
                (ex.InnerException?.Message?.Contains("UX_MaterialIssues_IdempotencyKey") == true))
            {
                // A true concurrent-retry race slipped past both earlier
                // checks and hit the unique index. Look up the row the
                // other request just committed and return it as a
                // successful replay instead of surfacing a raw DB error.
                var winner = await _context.MaterialIssues
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey);

                if (winner != null)
                {
                    return Ok(new
                    {
                        id = winner.Id,
                        issueNumber = winner.IssueNumber,
                        issuedQuantity = winner.Quantity,
                        palletNo = winner.PalletNo,
                        grnPalletId = winner.GrnPalletId,
                        message = "Material issue already recorded (idempotent replay)."
                    });
                }

                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Save failed: {detail}" });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;

                return Conflict(new
                {
                    message =
                        $"Stock was changed by another transaction. Please sync again. {detail}"
                });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;

                return StatusCode(500, new
                {
                    message = $"Save failed: {detail}"
                });
            }
        }


        private async Task<string> GenerateIssueNumberAsync()
        {
            var year = DateTime.Now.Year;
            var prefix = $"MI-{year}-";

            var last = await _context.MaterialIssues
                .Where(x =>
                    x.IssueNumber != null &&
                    x.IssueNumber.StartsWith(prefix))
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            var nextSeq = 1;

            if (last != null)
            {
                var numericPart =
                    last.IssueNumber.Substring(prefix.Length);

                if (int.TryParse(numericPart, out var lastSeq))
                {
                    nextSeq = lastSeq + 1;
                }
            }

            return $"{prefix}{nextSeq:D4}";
        }


        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var entity = await _context.MaterialIssues
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (entity == null)
                {
                    return NotFound(new
                    {
                        message = "Material Issue record not found."
                    });
                }

                _context.MaterialIssues.Remove(entity);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Deleted Successfully"
                });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new
                {
                    message = $"Delete failed: {detail}"
                });
            }
        }
    }
}
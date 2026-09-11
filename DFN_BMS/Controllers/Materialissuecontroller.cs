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
        // GET ALL - ONE ROW PER GRN
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var list = await _context.MaterialIssues
                    .AsNoTracking()
                    .Where(x => !string.IsNullOrWhiteSpace(x.GrnNumber))
                    .GroupBy(x => x.GrnNumber)
                    .Select(g => new
                    {
                        Id = g
                            .OrderByDescending(x => x.Id)
                            .Select(x => x.Id)
                            .FirstOrDefault(),

                        GrnNumber = g.Key,

                        IssueNumber = g
                            .OrderByDescending(x => x.Id)
                            .Select(x => x.IssueNumber)
                            .FirstOrDefault(),

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
                    .ToListAsync();

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
        // ONE GRN = ONE SLIP
        // ALL ITEMS UNDER THAT GRN
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
                        x.GrnNumber,
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

                // -----------------------------------------------------
                // SUPPLIER DETAILS FROM GRN
                // -----------------------------------------------------
                var supplierAddress = await _context.GrnHeaders
                    .AsNoTracking()
                    .Where(x => x.GrnNumber == selected.GrnNumber)
                    .Select(x => x.Supplier == null
                        ? null
                        : new
                        {
                            SupplierName = x.Supplier.SupplierName,
                            GstNo = x.Supplier.GstNo
                        })
                    .FirstOrDefaultAsync();

                // -----------------------------------------------------
                // ALL MATERIAL ISSUE ITEMS BELONGING TO THE SAME GRN
                // -----------------------------------------------------
                var items = await _context.MaterialIssues
                    .Include(x => x.Item)
                    .AsNoTracking()
                    .Where(x => x.GrnNumber == selected.GrnNumber)
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
                        x.GrnNumber
                    })
                    .ToListAsync();

                return Ok(new
                {
                    selected.Id,
                    selected.GrnNumber,
                    selected.IssueNumber,
                    selected.IssuedTo,
                    selected.IssuedBy,
                    selected.StoreLocation,
                    selected.Remarks,
                    selected.IssueDate,
                    selected.CreatedDate,
                    Supplier = supplierAddress,
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
                                    await GenerateIssueNumberAsync(),

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
                                await GenerateIssueNumberAsync(),

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
                        // Important: allow the execution strategy to
                        // catch/retry transient SQL exceptions.
                        throw;
                    }
                });

                return result ?? StatusCode(500, new
                {
                    message = "Material Issue operation did not return a result."
                });
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

        // =========================================================
        // GENERATE ISSUE NUMBER
        // =========================================================
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

        // =========================================================
        // DELETE MATERIAL ISSUE
        // =========================================================
        //
        // NOTE:
        // Delete removes only the issue history record.
        // It does NOT automatically put the quantity back into
        // StoreMovements because the original StoreMovement row(s)
        // may have been partially consumed.
        //
        // If you want "Delete = Restore Stock", that should be
        // implemented as a separate controlled stock-reversal action.
        // =========================================================
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                // A single DELETE statement is already atomic.
                // Avoid BeginTransactionAsync here because the DbContext
                // uses a retrying SQL Server execution strategy.
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
                var detail = ex.InnerException?.Message ?? ex.Message
                return StatusCode(500, new
                {
                    message = $"Delete failed: {detail}"
                });
            }
        }
    }
}

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
        // GET ALL - WEB MATERIAL ISSUE SLIP REPORT
        // =========================================================
        // =========================================================
        // GET ALL - ONE ROW PER GRN
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var list = await _context.MaterialIssues
                    .Include(x => x.Item)
                    .AsNoTracking()
                    .Where(x => !string.IsNullOrWhiteSpace(x.GrnNumber))
                    .GroupBy(x => x.GrnNumber)
                    .Select(g => new
                    {
                        // Use latest record as representative
                        Id = g
                            .OrderByDescending(x => x.Id)
                            .Select(x => x.Id)
                            .FirstOrDefault(),

                        GrnNumber = g.Key,

                        // Latest issue number
                        IssueNumber = g
                            .OrderByDescending(x => x.Id)
                            .Select(x => x.IssueNumber)
                            .FirstOrDefault(),

                        // Latest issue date
                        IssueDate = g
                            .OrderByDescending(x => x.Id)
                            .Select(x => x.IssueDate)
                            .FirstOrDefault(),

                        // Total quantity of all items in this GRN
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
                return StatusCode(500, new
                {
                    message = $"Failed to load Material Issue records: {ex.Message}"
                });
            }
        }
        // =========================================================
        // GET SINGLE MATERIAL ISSUE
        // USED FOR WEB SLIP PREVIEW
        // =========================================================
        // GET MATERIAL ISSUE SLIP
        // Returns one slip with dynamic items
        // =========================================================
        // =========================================================
        // GET MATERIAL ISSUE SLIP
        // ONE GRN = ONE SLIP
        // ALL ITEMS UNDER THAT GRN
        // =========================================================
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                // -----------------------------------------------------
                // Find the selected record
                // -----------------------------------------------------
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
                // GET SUPPLIER BILLING / SHIPPING DETAILS FROM GRN
                // -----------------------------------------------------

                var supplierAddress = await _context.GrnHeaders
                    .AsNoTracking()
                    .Where(x => x.GrnNumber == selected.GrnNumber)
                    .Select(x => x.Supplier == null
                        ? null
                        : new
                        {
                            // Supplier
                            SupplierName = x.Supplier.SupplierName,

                            // BILLING ADDRESS
                            //BillingCompanyName = x.Supplier.BillingCompanyName,
                            //BillingAddressLine1 = x.Supplier.BillingAddressLine1,
                            //BillingAddressLine2 = x.Supplier.BillingAddressLine2,
                            //BillingState = x.Supplier.BillingState,
                            //BillingStateCode = x.Supplier.BillingStateCode,
                            //BillingPinCode = x.Supplier.BillingPinCode,

                            //// SHIPPING ADDRESS
                            //ShippingCompanyName = x.Supplier.ShippingCompanyName,
                            //ShippingAddressLine1 = x.Supplier.ShippingAddressLine1,
                            //ShippingAddressLine2 = x.Supplier.ShippingAddressLine2,
                            //ShippingState = x.Supplier.ShippingState,
                            //ShippingStateCode = x.Supplier.ShippingStateCode,
                            //ShippingPinCode = x.Supplier.ShippingPinCode,

                            // GST
                            GstNo = x.Supplier.GstNo
                        })
                    .FirstOrDefaultAsync();
                // -----------------------------------------------------
                // Get ALL ITEMS belonging to the same GRN
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

                // -----------------------------------------------------
                // Return ONE slip + ALL ITEMS
                // -----------------------------------------------------
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

                    // Supplier Billing / Shipping Address
                    Supplier = supplierAddress,

                    TotalQuantity = items.Sum(x => x.Quantity),

                    Items = items
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = $"Failed to load Material Issue Slip: {ex.Message}"
                });
            }
        }
        // =========================================================
        // MOBILE CREATE
        // KEEP YOUR EXISTING POST
        // =========================================================
        //[HttpPost]
        //public async Task<IActionResult> Create([FromBody] MaterialIssue model)
        //{
        //    try
        //    {
        //        if (model.ItemId <= 0 ||
        //            model.Quantity <= 0 ||
        //            string.IsNullOrWhiteSpace(model.IssuedTo) ||
        //            string.IsNullOrWhiteSpace(model.IssuedBy))
        //        {
        //            return BadRequest(new
        //            {
        //                message =
        //                    "Part Number, Quantity, Issued To and Issued By are required"
        //            });
        //        }

        //        var itemExists = await _context.ItemMasters
        //            .AnyAsync(x => x.Id == model.ItemId);

        //        if (!itemExists)
        //        {
        //            return BadRequest(new
        //            {
        //                message = "Selected Part Number does not exist"
        //            });
        //        }

        //        // Duplicate pallet protection
        //        if (!string.IsNullOrWhiteSpace(model.PalletNo))
        //        {
        //            var alreadyIssued =
        //                await _context.MaterialIssues
        //                    .AnyAsync(x => x.PalletNo == model.PalletNo);

        //            if (alreadyIssued)
        //            {
        //                return BadRequest(new
        //                {
        //                    message =
        //                        $"Pallet {model.PalletNo} has already been issued."
        //                });
        //            }
        //        }

        //        var entity = new MaterialIssue
        //        {
        //            IssueNumber =
        //                await GenerateIssueNumberAsync(),

        //            ItemId = model.ItemId,

        //            Quantity = model.Quantity,

        //            IssuedTo =
        //                model.IssuedTo.Trim(),

        //            IssuedBy =
        //                model.IssuedBy.Trim(),

        //            StoreLocation =
        //                model.StoreLocation?.Trim(),

        //            PalletNo =
        //                model.PalletNo?.Trim(),

        //            GrnNumber =
        //                model.GrnNumber?.Trim(),

        //            Remarks =
        //                model.Remarks?.Trim(),

        //            IssueDate = DateTime.Now,

        //            CreatedDate = DateTime.Now
        //        };

        //        _context.MaterialIssues.Add(entity);

        //        await _context.SaveChangesAsync();

        //        return Ok(new
        //        {
        //            entity.Id,
        //            entity.IssueNumber
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        var detail =
        //            ex.InnerException?.Message ??
        //            ex.Message;

        //        return StatusCode(500, new
        //        {
        //            message =
        //                $"Save failed: {detail}"
        //        });
        //    }
        //}

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] MaterialIssue model)
        {
            try
            {
                if (model.ItemId <= 0 ||
                    model.Quantity <= 0 ||
                    string.IsNullOrWhiteSpace(model.IssuedTo) ||
                    string.IsNullOrWhiteSpace(model.IssuedBy))
                {
                    return BadRequest(new
                    {
                        message =
                            "Part Number, Quantity, Issued To and Issued By are required"
                    });
                }

                var itemExists = await _context.ItemMasters
                    .AnyAsync(x => x.Id == model.ItemId);

                if (!itemExists)
                {
                    return BadRequest(new
                    {
                        message = "Selected Part Number does not exist"
                    });
                }

                // Duplicate pallet protection — match on GrnPalletId (safe/unique),
                // not PalletNo (recyclable label).
                if (model.GrnPalletId.HasValue)
                {
                    var alreadyIssued =
                        await _context.MaterialIssues
                            .AnyAsync(x => x.GrnPalletId == model.GrnPalletId.Value);

                    if (alreadyIssued)
                    {
                        return BadRequest(new
                        {
                            message =
                                $"Pallet {model.PalletNo} has already been issued."
                        });
                    }
                }

                var entity = new MaterialIssue
                {
                    IssueNumber = await GenerateIssueNumberAsync(),
                    ItemId = model.ItemId,
                    Quantity = model.Quantity,
                    IssuedTo = model.IssuedTo.Trim(),
                    IssuedBy = model.IssuedBy.Trim(),
                    StoreLocation = model.StoreLocation?.Trim(),
                    PalletNo = model.PalletNo?.Trim(),
                    GrnPalletId = model.GrnPalletId,
                    GrnNumber = model.GrnNumber?.Trim(),
                    Remarks = model.Remarks?.Trim(),
                    IssueDate = DateTime.Now,
                    CreatedDate = DateTime.Now
                };

                _context.MaterialIssues.Add(entity);
                await _context.SaveChangesAsync();

                // -----------------------------------------------------
                // FREE UP THE RACK SLOT(S) THIS PALLET WAS OCCUPYING
                // -----------------------------------------------------
                if (entity.GrnPalletId.HasValue)
                {
                    var movements = await _context.StoreMovements
                        .Where(x => x.GrnPalletId == entity.GrnPalletId.Value)
                        .ToListAsync();

                    if (movements.Any())
                    {
                        _context.StoreMovements.RemoveRange(movements);
                        await _context.SaveChangesAsync();
                    }
                }

                return Ok(new
                {
                    entity.Id,
                    entity.IssueNumber
                });
            }
            catch (Exception ex)
            {
                var detail =
                    ex.InnerException?.Message ??
                    ex.Message;

                return StatusCode(500, new
                {
                    message =
                        $"Save failed: {detail}"
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
                    x.IssueNumber.StartsWith(prefix))
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            int nextSeq = 1;

            if (last != null)
            {
                var numericPart =
                    last.IssueNumber
                        .Substring(prefix.Length);

                if (int.TryParse(
                    numericPart,
                    out int lastSeq))
                {
                    nextSeq = lastSeq + 1;
                }
            }

            return $"{prefix}{nextSeq:D4}";
        }

        // =========================================================
        // DELETE
        // =========================================================
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity =
                await _context.MaterialIssues
                    .FindAsync(id);

            if (entity == null)
            {
                return NotFound(new
                {
                    message =
                        "Material Issue record not found"
                });
            }

            _context.MaterialIssues.Remove(entity);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message =
                    "Deleted Successfully"
            });
        }
    }
}
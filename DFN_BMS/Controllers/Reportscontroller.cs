//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
//using DFN_BMS.DB;
//using DFN_BMS.Models;

//namespace DFN_BMS.Controllers
//{
//    [ApiController]
//    [Route("api/[controller]")]
//    public class ReportsController : ControllerBase
//    {
//        private readonly AppDbContext _context;

//        public ReportsController(AppDbContext context)
//        {
//            _context = context;
//        }

//        // ============================================================
//        // FULL REPORT
//        //
//        // GET:
//        // /api/Reports/full
//        //
//        // Optional filters:
//        // fromDate
//        // toDate
//        // itemId
//        // itemGroupId
//        // supplierId
//        //
//        // Example:
//        // /api/Reports/full?fromDate=2026-08-01&toDate=2026-08-18
//        //
//        // /api/Reports/full?itemId=1
//        //
//        // /api/Reports/full?itemGroupId=2
//        //
//        // /api/Reports/full?supplierId=3
//        //
//        // Multiple:
//        // /api/Reports/full?fromDate=2026-08-01
//        // &toDate=2026-08-18
//        // &itemId=1
//        // &itemGroupId=2
//        // &supplierId=3
//        // ============================================================

//        [HttpGet("full")]
//        public async Task<IActionResult> GetFullReport(
//            [FromQuery] DateTime? fromDate,
//            [FromQuery] DateTime? toDate,
//            [FromQuery] int? itemId,
//            [FromQuery] int? itemGroupId,
//            [FromQuery] int? supplierId)
//        {
//            try
//            {
//                // ====================================================
//                // VALIDATE DATE
//                // ====================================================

//                if (fromDate.HasValue &&
//                    toDate.HasValue &&
//                    fromDate.Value.Date > toDate.Value.Date)
//                {
//                    return BadRequest(new
//                    {
//                        message = "From Date cannot be after To Date"
//                    });
//                }


//                // ====================================================
//                // BASE QUERY
//                // ====================================================

//                var headerQuery = _context.GrnHeaders
//                    .Include(x => x.Supplier)
//                    .Include(x => x.Lines)
//                        .ThenInclude(l => l.Item)
//                    .AsQueryable();


//                // ====================================================
//                // DATE FILTER
//                // ====================================================

//                if (fromDate.HasValue)
//                {
//                    headerQuery = headerQuery.Where(
//                        x => x.PoDate >= fromDate.Value.Date
//                    );
//                }

//                if (toDate.HasValue)
//                {
//                    headerQuery = headerQuery.Where(
//                        x => x.PoDate <=
//                             toDate.Value.Date
//                                 .AddDays(1)
//                                 .AddTicks(-1)
//                    );
//                }


//                // ====================================================
//                // ITEM FILTER
//                // ====================================================

//                if (itemId.HasValue)
//                {
//                    headerQuery = headerQuery.Where(
//                        x => x.Lines.Any(
//                            l => l.Item != null &&
//                                 l.Item.Id == itemId.Value
//                        )
//                    );
//                }


//                // ====================================================
//                // ITEM GROUP FILTER
//                // ====================================================
//                //
//                // Assumes:
//                // Item.ItemGroupId
//                //
//                // If your Item model uses another property name,
//                // change l.Item.ItemGroupId here.
//                // ====================================================

//                if (itemGroupId.HasValue)
//                {
//                    headerQuery = headerQuery.Where(
//                        x => x.Lines.Any(
//                            l => l.Item != null &&
//                                 l.Item.ItemGroupId == itemGroupId.Value
//                        )
//                    );
//                }


//                // ====================================================
//                // SUPPLIER FILTER
//                // ====================================================

//                if (supplierId.HasValue)
//                {
//                    headerQuery = headerQuery.Where(
//                        x => x.Supplier != null &&
//                             x.Supplier.Id == supplierId.Value
//                    );
//                }


//                // ====================================================
//                // GET GRN HEADERS
//                // ====================================================

//                var headers = await headerQuery
//                    .OrderBy(x => x.PoDate)
//                    .ToListAsync();


//                // ====================================================
//                // GET GRN LINE IDS
//                // ====================================================

//                var lineIds = headers
//                    .SelectMany(h => h.Lines)
//                    .Select(l => l.Id)
//                    .ToList();


//                // ====================================================
//                // GET STORE MOVEMENT / GRN PALLETS
//                // ====================================================

//                var pallets = await _context.GrnPallets
//                    .Where(p => lineIds.Contains(p.GrnLineId))
//                    .ToListAsync();


//                var palletsByLineId = pallets
//                    .GroupBy(p => p.GrnLineId)
//                    .ToDictionary(
//                        g => g.Key,
//                        g => g.First()
//                    );


//                var palletIds = pallets
//                    .Select(p => p.Id)
//                    .ToList();


//                // ====================================================
//                // GET STORE MOVEMENTS
//                // ====================================================

//                var movements = await _context.StoreMovements

//                    .Include(m => m.StorePosition)
//                        .ThenInclude(sp => sp.Store)

//                    .Include(m => m.RackRow)
//                        .ThenInclude(r => r.Column)
//                            .ThenInclude(c => c.Rack)
//                                .ThenInclude(rk => rk.Store)
//                                    .ThenInclude(lm => lm.StoreMaster)

//                    .Where(
//                        m =>
//                            m.GrnPalletId != null &&
//                            palletIds.Contains(m.GrnPalletId.Value)
//                    )

//                    .ToListAsync();


//                // ====================================================
//                // EARLIEST STORE MOVEMENT FOR EACH PALLET
//                // ====================================================

//                var earliestMovementByPalletId = movements
//                    .GroupBy(m => m.GrnPalletId!.Value)
//                    .ToDictionary(
//                        g => g.Key,
//                        g => g.OrderBy(m => m.MovementDate).First()
//                    );


//                // ====================================================
//                // GET MATERIAL ISSUES
//                // ====================================================

//                var storePalletNos = pallets
//                    .Select(p => p.PalletNo)
//                    .Where(p => !string.IsNullOrEmpty(p))
//                    .ToList();


//                var issues = await _context.MaterialIssues
//                    .Where(
//                        mi =>
//                            mi.PalletNo != null &&
//                            storePalletNos.Contains(mi.PalletNo)
//                    )
//                    .ToListAsync();


//                // ====================================================
//                // LATEST MATERIAL ISSUE FOR EACH PALLET
//                // ====================================================

//                var latestIssueByPalletNo = issues
//                    .GroupBy(i => i.PalletNo)
//                    .ToDictionary(
//                        g => g.Key,
//                        g => g.OrderByDescending(i => i.IssueDate).First()
//                    );


//                // ====================================================
//                // BUILD FINAL REPORT
//                // ====================================================

//                var rows = new List<object>();


//                foreach (var h in headers)
//                {
//                    var lines = h.Lines.Any()
//                        ? h.Lines.Cast<GrnLine>().ToList()
//                        : new List<GrnLine> { null };


//                    foreach (var l in lines)
//                    {
//                        // ============================================
//                        // GET PALLET
//                        // ============================================

//                        var pallet =
//                            l != null &&
//                            palletsByLineId.ContainsKey(l.Id)
//                                ? palletsByLineId[l.Id]
//                                : null;


//                        // ============================================
//                        // GET STORE MOVEMENT
//                        // ============================================

//                        var movement =
//                            pallet != null &&
//                            earliestMovementByPalletId.ContainsKey(pallet.Id)
//                                ? earliestMovementByPalletId[pallet.Id]
//                                : null;


//                        // ============================================
//                        // GET MATERIAL ISSUE
//                        // ============================================

//                        var issue =
//                            pallet?.PalletNo != null &&
//                            latestIssueByPalletNo.ContainsKey(pallet.PalletNo)
//                                ? latestIssueByPalletNo[pallet.PalletNo]
//                                : null;


//                        // ============================================
//                        // STORE LOCATION
//                        // ============================================

//                        var storeLocation =
//                            movement?.StorePosition?.Store?.StoreLocation
//                            ??
//                            movement?.RackRow?.Column?.Rack?.Store?
//                                .StoreMaster?.StoreLocation;


//                        // ============================================
//                        // STATUS
//                        // ============================================

//                        var status =
//                            issue != null
//                                ? "Issued"
//                                : movement != null
//                                    ? "In Store"
//                                    : (l != null && l.IsPosted)
//                                        ? "Posted"
//                                        : "Not Posted";


//                        // ============================================
//                        // ADD REPORT ROW
//                        // ============================================

//                        rows.Add(new
//                        {
//                            h.GrnNumber,

//                            SupplierName =
//                                h.Supplier != null
//                                    ? h.Supplier.SupplierName
//                                    : null,

//                            h.PoNumber,

//                            h.PoDate,

//                            h.GrnType,

//                            h.SupplierInvoiceNumber,

//                            h.SupplierInvoiceDate,

//                            PartNumber =
//                                l?.Item?.ItemNumber,

//                            PartName =
//                                l?.Item?.ItemName,

//                            Quantity =
//                                l?.Quantity,

//                            PalletQuantity =
//                                l?.PalletQuantity,

//                            Rate =
//                                l?.Rate,

//                            TotalValue =
//                                l?.TotalValue,

//                            LabelPalletNo =
//                                l?.PalletNo,

//                            FifoPalletNo =
//                                l?.FifoPalletNo,

//                            StorePalletNo =
//                                pallet?.PalletNo,

//                            StoreLocation =
//                                storeLocation,

//                            MovementDate =
//                                movement?.MovementDate,

//                            IssuedTo =
//                                issue?.IssuedTo,

//                            IssuedBy =
//                                issue?.IssuedBy,

//                            IssueDate =
//                                issue?.IssueDate,

//                            Status =
//                                status
//                        });
//                    }
//                }


//                // ====================================================
//                // RETURN RESULT
//                // ====================================================

//                return Ok(rows);
//            }
//            catch (Exception ex)
//            {
//                var detail =
//                    ex.InnerException?.Message ??
//                    ex.Message;

//                return StatusCode(
//                    500,
//                    new
//                    {
//                        message =
//                            $"Failed to load report: {detail}"
//                    }
//                );
//            }
//        }


//        // ============================================================
//        // GRN REPORT
//        // ============================================================

//        [HttpGet("grn")]
//        public async Task<IActionResult> GetGrnReport(
//            [FromQuery] DateTime? fromDate,
//            [FromQuery] DateTime? toDate)
//        {
//            try
//            {
//                if (fromDate.HasValue &&
//                    toDate.HasValue &&
//                    fromDate.Value.Date > toDate.Value.Date)
//                {
//                    return BadRequest(new
//                    {
//                        message = "From Date cannot be after To Date"
//                    });
//                }


//                var query = _context.GrnHeaders

//                    .Include(x => x.Supplier)

//                    .Include(x => x.Lines)
//                        .ThenInclude(l => l.Item)

//                    .AsQueryable();


//                if (fromDate.HasValue)
//                {
//                    query = query.Where(
//                        x => x.PoDate >= fromDate.Value.Date
//                    );
//                }


//                if (toDate.HasValue)
//                {
//                    query = query.Where(
//                        x =>
//                            x.PoDate <=
//                            toDate.Value.Date
//                                .AddDays(1)
//                                .AddTicks(-1)
//                    );
//                }


//                var headers = await query
//                    .OrderBy(x => x.PoDate)
//                    .ToListAsync();


//                var rows = headers

//                    .SelectMany(
//                        h => h.Lines.DefaultIfEmpty(),
//                        (h, l) => new
//                        {
//                            h.GrnNumber,

//                            SupplierName =
//                                h.Supplier != null
//                                    ? h.Supplier.SupplierName
//                                    : null,

//                            h.PoNumber,

//                            h.PoDate,

//                            h.GrnType,

//                            h.SupplierInvoiceNumber,

//                            h.SupplierInvoiceDate,

//                            PartNumber =
//                                l != null
//                                    ? l.Item.ItemNumber
//                                    : null,

//                            PartName =
//                                l != null
//                                    ? l.Item.ItemName
//                                    : null,

//                            Quantity =
//                                l != null
//                                    ? l.Quantity
//                                    : (decimal?)null,

//                            PalletQuantity =
//                                l != null
//                                    ? l.PalletQuantity
//                                    : null,

//                            Rate =
//                                l != null
//                                    ? l.Rate
//                                    : (decimal?)null,

//                            TotalValue =
//                                l != null
//                                    ? l.TotalValue
//                                    : (decimal?)null,

//                            IsPosted =
//                                l != null &&
//                                l.IsPosted,

//                            PalletNo =
//                                l != null
//                                    ? l.PalletNo
//                                    : null,

//                            FifoPalletNo =
//                                l != null
//                                    ? l.FifoPalletNo
//                                    : null
//                        }
//                    )
//                    .ToList();


//                return Ok(rows);
//            }
//            catch (Exception ex)
//            {
//                var detail =
//                    ex.InnerException?.Message ??
//                    ex.Message;

//                return StatusCode(
//                    500,
//                    new
//                    {
//                        message =
//                            $"Failed to load GRN report: {detail}"
//                    }
//                );
//            }
//        }


//        // ============================================================
//        // MATERIAL ISSUE REPORT
//        // ============================================================

//        [HttpGet("material-issue")]
//        public async Task<IActionResult> GetMaterialIssueReport(
//            [FromQuery] DateTime? fromDate,
//            [FromQuery] DateTime? toDate)
//        {
//            try
//            {
//                if (fromDate.HasValue &&
//                    toDate.HasValue &&
//                    fromDate.Value.Date > toDate.Value.Date)
//                {
//                    return BadRequest(new
//                    {
//                        message = "From Date cannot be after To Date"
//                    });
//                }


//                var query = _context.MaterialIssues

//                    .Include(x => x.Item)

//                    .AsQueryable();


//                if (fromDate.HasValue)
//                {
//                    query = query.Where(
//                        x => x.IssueDate >= fromDate.Value.Date
//                    );
//                }


//                if (toDate.HasValue)
//                {
//                    query = query.Where(
//                        x =>
//                            x.IssueDate <=
//                            toDate.Value.Date
//                                .AddDays(1)
//                                .AddTicks(-1)
//                    );
//                }


//                var rows = await query

//                    .OrderBy(x => x.IssueDate)

//                    .Select(x => new
//                    {
//                        x.IssueNumber,

//                        PartNumber =
//                            x.Item != null
//                                ? x.Item.ItemNumber
//                                : null,

//                        PartName =
//                            x.Item != null
//                                ? x.Item.ItemName
//                                : null,

//                        x.Quantity,

//                        x.IssuedTo,

//                        x.IssuedBy,

//                        x.StoreLocation,

//                        x.PalletNo,

//                        x.GrnNumber,

//                        x.Remarks,

//                        x.IssueDate,

//                        x.CreatedDate
//                    })

//                    .ToListAsync();


//                return Ok(rows);
//            }
//            catch (Exception ex)
//            {
//                var detail =
//                    ex.InnerException?.Message ??
//                    ex.Message;

//                return StatusCode(
//                    500,
//                    new
//                    {
//                        message =
//                            $"Failed to load material issue report: {detail}"
//                    }
//                );
//            }
//        }
//    }
//}


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
    public class ReportsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ReportsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/Reports/full?fromDate=2026-01-01&toDate=2026-01-31
        // ★ NEW: single merged report — no tabs. One row per GRN line,
        // enriched left-to-right across the whole pallet lifecycle:
        //   GRN Entry/Post  ->  Store Movement (where it was stuffed)
        //   ->  Material Issue (who/when it was issued out, if at all)
        //
        // Filters by PO Date (same open-ended-range behaviour as before).
        // Note: GrnLine.PalletNo/FifoPalletNo (the "EX-xx" / FIFO label
        // pallet) is a different identifier from GrnPallet.PalletNo (the
        // "Pxxx" pallet Store Movement actually stuffs and Material Issue
        // matches against) — both are surfaced as separate columns so
        // nothing is silently conflated.
        [HttpGet("full")]
        public async Task<IActionResult> GetFullReport([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
        {
            try
            {
                var headerQuery = _context.GrnHeaders
                    .Include(x => x.Supplier)
                    .Include(x => x.Lines)
                        .ThenInclude(l => l.Item)
                    .AsQueryable();

                if (fromDate.HasValue)
                    headerQuery = headerQuery.Where(x => x.PoDate >= fromDate.Value.Date);
                if (toDate.HasValue)
                    headerQuery = headerQuery.Where(x => x.PoDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));

                var headers = await headerQuery.OrderBy(x => x.PoDate).ToListAsync();
                var lineIds = headers.SelectMany(h => h.Lines).Select(l => l.Id).ToList();

                // GrnLineId -> GrnPallet (the Store Movement pallet record,
                // separate from GrnLine.PalletNo/FifoPalletNo above).
                var pallets = await _context.GrnPallets
                    .Where(p => lineIds.Contains(p.GrnLineId))
                    .ToListAsync();
                var palletsByLineId = pallets.ToDictionary(p => p.GrnLineId, p => p);
                var palletIds = pallets.Select(p => p.Id).ToList();

                // Earliest Store Movement per pallet = where/when it was
                // first stuffed into a store position or rack slot.
                var movements = await _context.StoreMovements
                    .Include(m => m.StorePosition)
                        .ThenInclude(sp => sp.Store)
                    .Include(m => m.RackRow)
                        .ThenInclude(r => r.Column)
                            .ThenInclude(c => c.Rack)
                                .ThenInclude(rk => rk.Store)
                                    .ThenInclude(lm => lm.StoreMaster)
                    .Where(m => m.GrnPalletId != null && palletIds.Contains(m.GrnPalletId.Value))
                    .ToListAsync();
                var earliestMovementByPalletId = movements
                    .GroupBy(m => m.GrnPalletId.Value)
                    .ToDictionary(g => g.Key, g => g.OrderBy(m => m.MovementDate).First());

                // Material Issue is matched by GrnPallet.PalletNo ("Pxxx").
                var storePalletNos = pallets.Select(p => p.PalletNo).Where(p => p != null).ToList();
                var issues = await _context.MaterialIssues
                    .Where(mi => mi.PalletNo != null && storePalletNos.Contains(mi.PalletNo))
                    .ToListAsync();
                var latestIssueByPalletNo = issues
                    .GroupBy(i => i.PalletNo)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.IssueDate).First());

                var rows = new List<object>();

                foreach (var h in headers)
                {
                    var lines = h.Lines.Any() ? h.Lines.Cast<GrnLine>().ToList() : new List<GrnLine> { null };

                    foreach (var l in lines)
                    {
                        var pallet = l != null && palletsByLineId.ContainsKey(l.Id) ? palletsByLineId[l.Id] : null;
                        var movement = pallet != null && earliestMovementByPalletId.ContainsKey(pallet.Id)
                            ? earliestMovementByPalletId[pallet.Id]
                            : null;
                        var issue = pallet?.PalletNo != null && latestIssueByPalletNo.ContainsKey(pallet.PalletNo)
                            ? latestIssueByPalletNo[pallet.PalletNo]
                            : null;

                        var storeLocation = movement?.StorePosition?.Store?.StoreLocation
                            ?? movement?.RackRow?.Column?.Rack?.Store?.StoreMaster?.StoreLocation;

                        var status = issue != null ? "Issued"
                            : movement != null ? "In Store"
                            : (l != null && l.IsPosted) ? "Posted"
                            : "Not Posted";

                        rows.Add(new
                        {
                            h.GrnNumber,
                            SupplierName = h.Supplier != null ? h.Supplier.SupplierName : null,
                            h.PoNumber,
                            h.PoDate,
                            h.GrnType,
                            h.SupplierInvoiceNumber,
                            h.SupplierInvoiceDate,
                            PartNumber = l?.Item?.ItemNumber,
                            PartName = l?.Item?.ItemName,
                            Quantity = l?.Quantity,
                            PalletQuantity = l?.PalletQuantity,
                            Rate = l?.Rate,
                            TotalValue = l?.TotalValue,
                            LabelPalletNo = l?.PalletNo,
                            FifoPalletNo = l?.FifoPalletNo,
                            StorePalletNo = pallet?.PalletNo,
                            StoreLocation = storeLocation,
                            MovementDate = movement?.MovementDate,
                            IssuedTo = issue?.IssuedTo,
                            IssuedBy = issue?.IssuedBy,
                            IssueDate = issue?.IssueDate,
                            Status = status
                        });
                    }
                }

                return Ok(rows);
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Failed to load report: {detail}" });
            }
        }

        // GET: api/Reports/grn?fromDate=2026-01-01&toDate=2026-01-31
        // Filters by PO Date. Both dates optional — omit either (or both)
        // to get an open-ended range / everything. Kept alongside /full
        // in case anything else still links directly to a GRN-only export.
        [HttpGet("grn")]
        public async Task<IActionResult> GetGrnReport(
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null,
            [FromQuery] bool exportAll = false)
        {
            try
            {
                // Keep normal grid requests small. This prevents a large report
                // from being loaded into the browser in one request.
                page = Math.Max(page, 1);

                if (!exportAll)
                    pageSize = Math.Clamp(pageSize, 10, 100);
                else
                    pageSize = Math.Clamp(pageSize, 1, 1000000);

                var query = _context.GrnHeaders
                    .AsNoTracking()
                    .AsQueryable();

                if (fromDate.HasValue)
                {
                    query = query.Where(x =>
                        x.PoDate >= fromDate.Value.Date);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(x =>
                        x.PoDate < toDate.Value.Date.AddDays(1));
                }

                var rowQuery = query
                    .SelectMany(
                        h => h.Lines.DefaultIfEmpty(),
                        (h, l) => new
                        {
                            h.GrnNumber,
                            SupplierName = h.Supplier != null
                                ? h.Supplier.SupplierName
                                : null,
                            h.PoNumber,
                            h.PoDate,
                            h.GrnType,
                            h.SupplierInvoiceNumber,
                            h.SupplierInvoiceDate,

                            PartNumber = l != null && l.Item != null
                                ? l.Item.ItemNumber
                                : null,

                            PartName = l != null && l.Item != null
                                ? l.Item.ItemName
                                : null,

                            Quantity = l != null
                                ? l.Quantity
                                : (decimal?)null,

                            PalletQuantity = l != null
                                ? l.PalletQuantity
                                : (decimal?)null,

                            Rate = l != null
                                ? l.Rate
                                : (decimal?)null,

                            TotalValue = l != null
                                ? l.TotalValue
                                : (decimal?)null,

                            IsPosted = l != null && l.IsPosted,

                            PalletNo = l != null
                                ? l.PalletNo
                                : null,

                            FifoPalletNo = l != null
                                ? l.FifoPalletNo
                                : null
                        });

                // Search is also performed in SQL Server, not against the
                // complete dataset in the browser.
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var q = search.Trim();

                    rowQuery = rowQuery.Where(x =>
                        (x.GrnNumber != null &&
                         x.GrnNumber.Contains(q)) ||

                        (x.SupplierName != null &&
                         x.SupplierName.Contains(q)) ||

                        (x.PoNumber != null &&
                         x.PoNumber.Contains(q)) ||

                        (x.PartNumber != null &&
                         x.PartNumber.Contains(q)) ||

                        (x.PartName != null &&
                         x.PartName.Contains(q)) ||

                        (x.SupplierInvoiceNumber != null &&
                         x.SupplierInvoiceNumber.Contains(q)));
                }

                // Count and summary are calculated in SQL Server for the
                // complete filtered result, not just the current page.
                // One aggregate query is used instead of separate COUNT/SUM
                // requests to reduce database round-trips.
                var summary = await rowQuery
                    .GroupBy(x => 1)
                    .Select(g => new
                    {
                        TotalRows = g.Count(),
                        TotalQuantity = g.Sum(x => x.Quantity ?? 0m),
                        TotalValue = g.Sum(x => x.TotalValue ?? 0m),
                        PostedCount = g.Count(x => x.IsPosted)
                    })
                    .FirstOrDefaultAsync();

                var totalRows = summary?.TotalRows ?? 0;
                var totalQuantity = summary?.TotalQuantity ?? 0m;
                var totalValue = summary?.TotalValue ?? 0m;
                var postedCount = summary?.PostedCount ?? 0;

                var orderedQuery = rowQuery
                    .OrderByDescending(x => x.PoDate)
                    .ThenByDescending(x => x.GrnNumber);

                if (exportAll)
                {
                    var exportRows = await orderedQuery.ToListAsync();
                    return Ok(exportRows);
                }

                var rows = await orderedQuery
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return Ok(new
                {
                    data = rows,
                    totalRows,
                    totalQuantity,
                    totalValue,
                    postedCount,
                    page,
                    pageSize
                });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;

                return StatusCode(
                    500,
                    new
                    {
                        message = $"Failed to load report: {detail}"
                    });
            }
        }

        // GET: api/Reports/material-issue?fromDate=2026-01-01&toDate=2026-01-31
        // Filters by IssueDate. Kept alongside /full for the same reason
        // as /grn above.
        [HttpGet("material-issue")]
        public async Task<IActionResult> GetMaterialIssueReport(
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null,
            [FromQuery] bool exportAll = false)
        {
            try
            {
                // ============================================================
                // SERVER-SIDE PAGINATION
                // ============================================================

                page = Math.Max(page, 1);

                if (!exportAll)
                    pageSize = Math.Clamp(pageSize, 10, 100);
                else
                    pageSize = Math.Clamp(pageSize, 1, 1000000);

                // ============================================================
                // BASE QUERY
                // ============================================================

                var query = _context.MaterialIssues
                    .AsNoTracking()
                    .Select(x => new
                    {
                        x.IssueNumber,

                        PartNumber = x.Item != null
                            ? x.Item.ItemNumber
                            : null,

                        PartName = x.Item != null
                            ? x.Item.ItemName
                            : null,

                        x.Quantity,
                        x.IssuedTo,
                        x.IssuedBy,
                        x.StoreLocation,
                        x.PalletNo,
                        x.GrnNumber,
                        x.Remarks,
                        x.IssueDate,
                        x.CreatedDate
                    })
                    .AsQueryable();

                // ============================================================
                // DATE FILTER
                // ============================================================

                if (fromDate.HasValue)
                {
                    query = query.Where(x =>
                        x.IssueDate >= fromDate.Value.Date);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(x =>
                        x.IssueDate < toDate.Value.Date.AddDays(1));
                }

                // ============================================================
                // SERVER-SIDE SEARCH
                // ============================================================

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var q = search.Trim();

                    query = query.Where(x =>
                        (x.IssueNumber != null &&
                         x.IssueNumber.Contains(q)) ||

                        (x.PalletNo != null &&
                         x.PalletNo.Contains(q)) ||

                        (x.IssuedTo != null &&
                         x.IssuedTo.Contains(q)) ||

                        (x.IssuedBy != null &&
                         x.IssuedBy.Contains(q)) ||

                        (x.PartNumber != null &&
                         x.PartNumber.Contains(q)) ||

                        (x.PartName != null &&
                         x.PartName.Contains(q)) ||

                        (x.GrnNumber != null &&
                         x.GrnNumber.Contains(q)) ||

                        (x.StoreLocation != null &&
                         x.StoreLocation.Contains(q))
                    );
                }

                // ============================================================
                // SUMMARY FOR COMPLETE FILTERED DATASET
                // ============================================================

                var summary = await query
                    .GroupBy(x => 1)
                    .Select(g => new
                    {
                        TotalRows = g.Count(),

                        TotalQuantity = g.Sum(x =>
                            x.Quantity),

                        UniquePartsCount = g
                            .Where(x => x.PartNumber != null)
                            .Select(x => x.PartNumber)
                            .Distinct()
                            .Count()
                    })
                    .FirstOrDefaultAsync();

                var totalRows = summary?.TotalRows ?? 0;
                var totalQuantity = summary?.TotalQuantity ?? 0;
                var uniquePartsCount = summary?.UniquePartsCount ?? 0;

                // ============================================================
                // ORDERING
                // ============================================================

                var orderedQuery = query
                    .OrderByDescending(x => x.IssueDate)
                    .ThenByDescending(x => x.IssueNumber);

                // ============================================================
                // EXPORT ALL
                // Export intentionally bypasses normal pagination.
                // ============================================================

                if (exportAll)
                {
                    var exportRows = await orderedQuery.ToListAsync();
                    return Ok(exportRows);
                }

                // ============================================================
                // GET ONLY CURRENT PAGE
                // ============================================================

                var rows = await orderedQuery
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                // ============================================================
                // RESPONSE
                // ============================================================

                return Ok(new
                {
                    data = rows,
                    totalRows,
                    totalQuantity,
                    uniquePartsCount,
                    page,
                    pageSize
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
                            $"Failed to load Material Issue report: {detail}"
                    });
            }
        }
    }
}
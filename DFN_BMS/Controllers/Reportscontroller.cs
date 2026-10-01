
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

        // ============================================================
        // PASTE THIS METHOD INTO YOUR EXISTING ReportsController CLASS
        // (replaces the earlier GetStockReport draft). Same _context,
        // no new DI wiring needed.
        //
        // GET: api/Reports/stock?search=PKG&itemGroupId=3&status=Danger&page=1&pageSize=10
        //
        // CHANGE FROM THE PREVIOUS VERSION:
        //   The response now also includes `itemGroups` — the distinct
        //   list of { id, groupName } used to build ItemMaster rows —
        //   so the frontend's "Item Group" filter dropdown is populated
        //   from THIS endpoint's real data instead of guessing at a
        //   separate /Masters/item-groups route that may not exist.
        //   This mirrors how GRN Report's "Part Group" / "Supplier
        //   Group" dropdowns are just derived from the underlying data.
        //
        // One row PER ITEM (not per pallet/movement, unlike the other
        // reports) — this is a current-snapshot stock position report,
        // so there's no date range filter; it always reflects "right now".
        //
        // Status classification mirrors DashboardController.GetSummary
        // EXACTLY (onHand >= SafetyLevel -> Safety, >= ReorderLevel ->
        // Reorder, else Danger) so the numbers on this report and the
        // dashboard donut can never disagree.
        // ============================================================

        [HttpGet("stock")]
        public async Task<IActionResult> GetStockReport(
            [FromQuery] string? search = null,
            [FromQuery] string? partNumber = null,
            [FromQuery] int? itemGroupId = null,
            [FromQuery] string? status = null,      // "Safety" | "Reorder" | "Danger" | null (= all)
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] bool exportAll = false)
        {
            try
            {
                page = Math.Max(page, 1);

                if (!exportAll)
                    pageSize = Math.Clamp(pageSize, 10, 100);
                else
                    pageSize = Math.Clamp(pageSize, 1, 1000000);

                // ============================================================
                // RECEIVED / ISSUED TOTALS PER ITEM
                // Same two aggregate queries GetSummary already uses for the
                // stock-status donut — kept identical on purpose.
                // ============================================================

                var receivedByItem = await _context.GrnLines
                    .Where(l => l.IsPosted)
                    .GroupBy(l => l.ItemId)
                    .Select(g => new { ItemId = g.Key, Qty = g.Sum(l => l.Quantity) })
                    .ToDictionaryAsync(x => x.ItemId, x => x.Qty);

                var issuedByItem = await _context.MaterialIssues
                    .GroupBy(i => i.ItemId)
                    .Select(g => new { ItemId = g.Key, Qty = g.Sum(i => i.Quantity) })
                    .ToDictionaryAsync(x => x.ItemId, x => x.Qty);

                // ============================================================
                // ITEM GROUP OPTIONS — for the frontend filter dropdown.
                // Computed from the SAME ItemMasters table (not a separate
                // Masters endpoint), same navigation property GRN/Store
                // reports already use (Item.ItemGroup.GroupName).
                // ============================================================

                var itemGroupOptions = await _context.ItemMasters
                    .Where(i => i.ItemGroup != null)
                    .Select(i => new { id = i.ItemGroupId, groupName = i.ItemGroup.GroupName })
                    .Distinct()
                    .OrderBy(g => g.groupName)
                    .ToListAsync();

                var partNumberOptions = await _context.ItemMasters
                    .AsNoTracking()
                    .Where(i => i.ItemNumber != null && i.ItemNumber != "")
                    .Select(i => i.ItemNumber)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync();

                // ============================================================
                // BASE ITEM QUERY + FILTERS
                // ============================================================

                var itemsQuery = _context.ItemMasters
                    .Include(i => i.ItemGroup)
                    .AsNoTracking()
                    .AsQueryable();

                if (itemGroupId.HasValue)
                    itemsQuery = itemsQuery.Where(i => i.ItemGroupId == itemGroupId.Value);

                // PART NUMBER-WISE FILTER
                if (!string.IsNullOrWhiteSpace(partNumber))
                {
                    var pn = partNumber.Trim();
                    itemsQuery = itemsQuery.Where(i => i.ItemNumber == pn);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var q = search.Trim();
                    itemsQuery = itemsQuery.Where(i =>
                        (i.ItemNumber != null && i.ItemNumber.Contains(q)) ||
                        (i.ItemName != null && i.ItemName.Contains(q)));
                }

                var items = await itemsQuery.ToListAsync();

                // ============================================================
                // BUILD ONE ROW PER ITEM (in-memory — SafetyLevel/ReorderLevel
                // comparisons aren't SQL-translatable cleanly alongside the
                // dictionary lookups, and the item count is expected to be
                // small/bounded, same assumption GetSummary already makes).
                // ============================================================

                var allRows = items.Select(item =>
                {
                    var received = receivedByItem.TryGetValue(item.Id, out var r) ? r : 0m;
                    var issued = issuedByItem.TryGetValue(item.Id, out var iss) ? iss : 0m;
                    var onHand = received - issued;
                    if (onHand < 0) onHand = 0;

                    var itemStatus =
                        onHand >= item.SafetyLevel ? "Safety" :
                        onHand >= item.ReorderLevel ? "Reorder" :
                        "Danger";

                    return new
                    {
                        itemId = item.Id,
                        partNumber = item.ItemNumber,
                        partName = item.ItemName,
                        itemGroupId = (int?)item.ItemGroupId,
                        itemGroupName = item.ItemGroup != null ? item.ItemGroup.GroupName : null,
                        receivedQty = received,
                        issuedQty = issued,
                        onHandQty = onHand,
                        safetyLevel = item.SafetyLevel,
                        reorderLevel = item.ReorderLevel,
                        unitPrice = item.UnitPrice,
                        stockValue = onHand * item.UnitPrice,
                        status = itemStatus
                    };
                }).ToList();

                if (!string.IsNullOrWhiteSpace(status))
                {
                    allRows = allRows
                        .Where(r => string.Equals(r.status, status.Trim(), StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                // ============================================================
                // SUMMARY OVER THE FULL FILTERED SET (not just current page)
                // ============================================================

                var totalRows = allRows.Count;
                var totalOnHandQty = allRows.Sum(r => r.onHandQty);
                var totalStockValue = allRows.Sum(r => r.stockValue);
                var safetyCount = allRows.Count(r => r.status == "Safety");
                var reorderCount = allRows.Count(r => r.status == "Reorder");
                var dangerCount = allRows.Count(r => r.status == "Danger");

                // ============================================================
                // ORDER: most urgent first (Danger -> Reorder -> Safety),
                // then by lowest on-hand qty within each bucket. Matches the
                // "worst stock first" ordering that's most useful operationally.
                // ============================================================

                int StatusRank(string s) => s == "Danger" ? 0 : s == "Reorder" ? 1 : 2;

                var ordered = allRows
                    .OrderBy(r => StatusRank(r.status))
                    .ThenBy(r => r.onHandQty)
                    .ToList();

                if (exportAll)
                {
                    return Ok(new
                    {
                        data = ordered,
                        totalRows,
                        totalOnHandQty,
                        totalStockValue,
                        safetyCount,
                        reorderCount,
                        dangerCount,
                        itemGroups = itemGroupOptions,
                        partNumbers = partNumberOptions
                    });
                }

                var pageRows = ordered
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Ok(new
                {
                    data = pageRows,
                    totalRows,
                    totalOnHandQty,
                    totalStockValue,
                    safetyCount,
                    reorderCount,
                    dangerCount,
                    page,
                    pageSize,
                    itemGroups = itemGroupOptions,
                    partNumbers = partNumberOptions
                });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Failed to load Stock report: {detail}" });
            }
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
        public async Task<IActionResult> GetFullReport(
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] string? partNumber = null,
            [FromQuery] int? itemGroupId = null,
            [FromQuery] int? supplierGroupId = null)
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

                if (itemGroupId.HasValue)
                    headerQuery = headerQuery.Where(x =>
                        x.Lines.Any(l => l.Item != null && l.Item.ItemGroupId == itemGroupId.Value));

                if (supplierGroupId.HasValue)
                    headerQuery = headerQuery.Where(x =>
                        x.Supplier != null && x.Supplier.SupplierGroupId == supplierGroupId.Value);

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
                        if (!string.IsNullOrWhiteSpace(partNumber) &&
                            (l?.Item?.ItemNumber == null ||
                             !string.Equals(l.Item.ItemNumber, partNumber.Trim(), StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

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
            [FromQuery] string? partNumber = null,
            [FromQuery] int? itemGroupId = null,
            [FromQuery] int? supplierGroupId = null,
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

              SupplierGroupId = h.Supplier != null
                  ? (int?)h.Supplier.SupplierGroupId
                  : null,

              SupplierGroupName = h.Supplier != null &&
                                  h.Supplier.SupplierGroup != null
                  ? h.Supplier.SupplierGroup.SupplierGroupType
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

              ItemGroupId = l != null && l.Item != null
                  ? (int?)l.Item.ItemGroupId
                  : null,

              ItemGroupName = l != null &&
                              l.Item != null &&
                              l.Item.ItemGroup != null
                  ? l.Item.ItemGroup.GroupName
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

                // ============================================================
                // ITEM GROUP / SUPPLIER GROUP FILTERS
                // ============================================================

                if (itemGroupId.HasValue)
                {
                    rowQuery = rowQuery.Where(x =>
                        x.ItemGroupId == itemGroupId.Value);
                }

                if (supplierGroupId.HasValue)
                {
                    rowQuery = rowQuery.Where(x =>
                        x.SupplierGroupId == supplierGroupId.Value);
                }

                if (!string.IsNullOrWhiteSpace(partNumber))
                {
                    var pn = partNumber.Trim();
                    rowQuery = rowQuery.Where(x => x.PartNumber == pn);
                }

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
            [FromQuery] string? partNumber = null,
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

                if (!string.IsNullOrWhiteSpace(partNumber))
                {
                    var pn = partNumber.Trim();
                    query = query.Where(x => x.PartNumber == pn);
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


        [HttpGet("store")]
        public async Task<IActionResult> GetStoreReport(
          [FromQuery] DateTime? fromDate,
          [FromQuery] DateTime? toDate,
          [FromQuery] string? storeLocation = null,
          [FromQuery] string? partNumber = null,
          [FromQuery] int? itemGroupId = null,
          [FromQuery] int page = 1,
          [FromQuery] int pageSize = 10,
          [FromQuery] string? search = null,
          [FromQuery] bool exportAll = false)
        {
            try
            {
                page = Math.Max(page, 1);

                if (!exportAll)
                    pageSize = Math.Clamp(pageSize, 10, 100);
                else
                    pageSize = Math.Clamp(pageSize, 1, 1000000);

                var query = _context.StoreMovements
                    .AsNoTracking()
                    .Where(m => m.GrnPalletId != null)
                    .Select(m => new
                    {
                        movementId = m.Id,
                        movementDate = m.MovementDate,
                        createdBy = m.CreatedBy,
                        side = m.Side,
                        slotNumber = m.SlotNumber,
                        quantity = m.Quantity,

                        palletNo = m.GrnPallet != null
                            ? m.GrnPallet.PalletNo
                            : null,

                        fifoPalletNo = m.GrnPallet != null &&
                                       m.GrnPallet.GrnLine != null
                            ? m.GrnPallet.GrnLine.FifoPalletNo
                            : null,

                        grnNumber = m.GrnPallet != null &&
                                    m.GrnPallet.GrnLine != null &&
                                    m.GrnPallet.GrnLine.Header != null
                            ? m.GrnPallet.GrnLine.Header.GrnNumber
                            : null,

                        grnType = m.GrnPallet != null &&
                                  m.GrnPallet.GrnLine != null &&
                                  m.GrnPallet.GrnLine.Header != null
                            ? m.GrnPallet.GrnLine.Header.GrnType
                            : null,

                        partNumber = m.GrnPallet != null &&
                                     m.GrnPallet.GrnLine != null &&
                                     m.GrnPallet.GrnLine.Item != null
                            ? m.GrnPallet.GrnLine.Item.ItemNumber
                            : null,

                        partName = m.GrnPallet != null &&
                                   m.GrnPallet.GrnLine != null &&
                                   m.GrnPallet.GrnLine.Item != null
                            ? m.GrnPallet.GrnLine.Item.ItemName
                            : null,

                        itemGroupIdValue = m.GrnPallet != null &&
                                           m.GrnPallet.GrnLine != null &&
                                           m.GrnPallet.GrnLine.Item != null
                            ? (int?)m.GrnPallet.GrnLine.Item.ItemGroupId
                            : null,

                        itemGroupName = m.GrnPallet != null &&
                                        m.GrnPallet.GrnLine != null &&
                                        m.GrnPallet.GrnLine.Item != null &&
                                        m.GrnPallet.GrnLine.Item.ItemGroup != null
                            ? m.GrnPallet.GrnLine.Item.ItemGroup.GroupName
                            : null,

                        storeLocation = m.StorePosition != null &&
                                        m.StorePosition.Store != null
                            ? m.StorePosition.Store.StoreLocation
                            : m.RackRow != null &&
                              m.RackRow.Column != null &&
                              m.RackRow.Column.Rack != null &&
                              m.RackRow.Column.Rack.Store != null &&
                              m.RackRow.Column.Rack.Store.StoreMaster != null
                                ? m.RackRow.Column.Rack.Store.StoreMaster.StoreLocation
                                : null,

                        positionCode = m.StorePosition != null
                            ? m.StorePosition.PositionCode
                            : null,

                        rackNo = m.RackRow != null &&
                                 m.RackRow.Column != null &&
                                 m.RackRow.Column.Rack != null
                            ? m.RackRow.Column.Rack.RackNo
                            : null,

                        columnNo = m.RackRow != null &&
                                   m.RackRow.Column != null
                            ? m.RackRow.Column.ColumnNo
                            : null,

                        rowNo = m.RackRow != null
                            ? m.RackRow.RowNo
                            : null,

                        status = m.GrnPallet != null &&
                                 m.GrnPallet.PalletNo != null &&
                                 _context.MaterialIssues.Any(mi =>
                                     mi.PalletNo != null &&
                                     mi.PalletNo == m.GrnPallet.PalletNo)
                            ? "Issued"
                            : "In Store"
                    })
                    .AsQueryable();

                if (fromDate.HasValue)
                {
                    query = query.Where(x =>
                        x.movementDate >= fromDate.Value.Date);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(x =>
                        x.movementDate < toDate.Value.Date.AddDays(1));
                }

                if (!string.IsNullOrWhiteSpace(storeLocation))
                {
                    var store = storeLocation.Trim();
                    query = query.Where(x => x.storeLocation == store);
                }

                if (itemGroupId.HasValue)
                {
                    query = query.Where(x =>
                        x.itemGroupIdValue == itemGroupId.Value);
                }

                if (!string.IsNullOrWhiteSpace(partNumber))
                {
                    var pn = partNumber.Trim();
                    query = query.Where(x => x.partNumber == pn);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var q = search.Trim();

                    query = query.Where(x =>
                        (x.palletNo != null && x.palletNo.Contains(q)) ||
                        (x.fifoPalletNo != null && x.fifoPalletNo.Contains(q)) ||
                        (x.grnNumber != null && x.grnNumber.Contains(q)) ||
                        (x.partNumber != null && x.partNumber.Contains(q)) ||
                        (x.partName != null && x.partName.Contains(q)) ||
                        (x.storeLocation != null && x.storeLocation.Contains(q)) ||
                        (x.positionCode != null && x.positionCode.Contains(q)) ||
                        (x.rackNo != null && x.rackNo.Contains(q)) ||
                        (x.columnNo != null && x.columnNo.Contains(q)) ||
                        (x.rowNo != null && x.rowNo.Contains(q)) ||
                        (x.createdBy != null && x.createdBy.Contains(q))
                    );
                }

                var summary = await query
                    .GroupBy(x => 1)
                    .Select(g => new
                    {
                        totalRows = g.Count(),
                        totalQuantity = g.Sum(x => x.quantity),
                        uniquePallets = g
                            .Where(x => x.palletNo != null)
                            .Select(x => x.palletNo)
                            .Distinct()
                            .Count(),
                        uniqueStores = g
                            .Where(x => x.storeLocation != null)
                            .Select(x => x.storeLocation)
                            .Distinct()
                            .Count()
                    })
                    .FirstOrDefaultAsync();

                var totalRows = summary?.totalRows ?? 0;
                var totalQuantity = summary?.totalQuantity ?? 0m;
                var uniquePallets = summary?.uniquePallets ?? 0;
                var uniqueStores = summary?.uniqueStores ?? 0;

                var orderedQuery = query
                    .OrderByDescending(x => x.movementDate)
                    .ThenByDescending(x => x.movementId);

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
                    uniquePallets,
                    uniqueStores,
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
                        message = $"Failed to load Store Report: {detail}"
                    });
            }
        }


        // ============================================================
        // OVERALL REPORT
        //
        // Combines INWARD (GRN) and OUTWARD (Material Issue) movements.
        // Supported filters:
        //   Year, Month, Day, Part Number, Supplier, Supplier Group,
        //   Part Group, Inward/Outward, Value and Rate.
        //
        // Customer / Customer Group are returned as unavailable because
        // the supplied GRN/MaterialIssue entities do not contain a
        // CustomerId relationship. They must not be guessed or joined
        // to unrelated customer transactions.
        // ============================================================
        [HttpGet("overall")]
        public async Task<IActionResult> GetOverallReport(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int? year = null,
            [FromQuery] int? month = null,
            [FromQuery] int? day = null,
            [FromQuery] string? partNumber = null,
            [FromQuery] int? supplierId = null,
            [FromQuery] int? supplierGroupId = null,
            [FromQuery] int? itemGroupId = null,
            [FromQuery] string? direction = null,
            [FromQuery] decimal? minValue = null,
            [FromQuery] decimal? maxValue = null,
            [FromQuery] decimal? minRate = null,
            [FromQuery] decimal? maxRate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            [FromQuery] bool exportAll = false)
        {
            try
            {
                page = Math.Max(page, 1);
                pageSize = exportAll
                    ? Math.Clamp(pageSize, 1, 1000000)
                    : Math.Clamp(pageSize, 10, 200);

                if (month.HasValue && (month < 1 || month > 12))
                    return BadRequest(new { message = "Month must be between 1 and 12." });

                if (day.HasValue && (day < 1 || day > 31))
                    return BadRequest(new { message = "Day must be between 1 and 31." });

                var inwardQuery = _context.GrnLines
                    .AsNoTracking()
                    .Where(l => l.IsPosted)
                    .Select(l => new
                    {
                        Date = l.Header != null ? l.Header.CreatedDate : DateTime.MinValue,
                        PartNumber = l.Item != null ? l.Item.ItemNumber : null,
                        PartName = l.Item != null ? l.Item.ItemName : null,
                        UOM = l.Item != null
    ? l.Item.Uom
    : null,
                        PartGroupId = l.Item != null ? (int?)l.Item.ItemGroupId : null,
                        PartGroupName = l.Item != null && l.Item.ItemGroup != null
                            ? l.Item.ItemGroup.GroupName : null,
                        SupplierId = l.Header != null && l.Header.Supplier != null
                            ? (int?)l.Header.Supplier.Id : null,
                        SupplierName = l.Header != null && l.Header.Supplier != null
                            ? l.Header.Supplier.SupplierName : null,
                        SupplierGroupId = l.Header != null && l.Header.Supplier != null
                            ? (int?)l.Header.Supplier.SupplierGroupId : null,
                        SupplierGroupName = l.Header != null &&
                                            l.Header.Supplier != null &&
                                            l.Header.Supplier.SupplierGroup != null
                            ? l.Header.Supplier.SupplierGroup.SupplierGroupType : null,
                        Quantity = l.Quantity,
                        Rate = l.Rate,
                        Value = l.TotalValue,
                        Direction = "Inward",
                        Reference = l.Header != null ? l.Header.GrnNumber : null
                    });

                var outwardQuery = _context.MaterialIssues
                    .AsNoTracking()
                    .Select(i => new
                    {
                        Date = i.IssueDate,
                        PartNumber = i.Item != null ? i.Item.ItemNumber : null,
                        PartName = i.Item != null ? i.Item.ItemName : null,
                        UOM = i.Item != null
    ? i.Item.Uom
    : null,
                        PartGroupId = i.Item != null ? (int?)i.Item.ItemGroupId : null,
                        PartGroupName = i.Item != null && i.Item.ItemGroup != null
                            ? i.Item.ItemGroup.GroupName : null,
                        SupplierId = (int?)null,
                        SupplierName = (string?)null,
                        SupplierGroupId = (int?)null,
                        SupplierGroupName = (string?)null,
                        Quantity = i.Quantity,
                        Rate = i.Item != null ? (decimal?)i.Item.UnitPrice : null,
                        Value = i.Item != null ? (decimal?)(i.Quantity * i.Item.UnitPrice) : null,
                        Direction = "Outward",
                        Reference = i.IssueNumber
                    });

                var inward = await inwardQuery.ToListAsync();
                var outward = await outwardQuery.ToListAsync();

                var rows = new List<OverallReportRow>();

                rows.AddRange(inward.Select(x => new OverallReportRow
                {
                    Date = x.Date,
                    Year = x.Date.Year,
                    Month = x.Date.Month,
                    Day = x.Date.Day,
                    PartNumber = x.PartNumber,
                    PartName = x.PartName,
                    UOM = x.UOM,
                    PartGroupId = x.PartGroupId,
                    PartGroupName = x.PartGroupName,
                    SupplierId = x.SupplierId,
                    SupplierName = x.SupplierName,
                    SupplierGroupId = x.SupplierGroupId,
                    SupplierGroupName = x.SupplierGroupName,
                    Quantity = x.Quantity,
                    Inward = x.Quantity,
                    Outward = 0m,
                    Rate = x.Rate,
                    Value = x.Value,
                    Direction = x.Direction,
                    Reference = x.Reference
                }));

                rows.AddRange(outward.Select(x => new OverallReportRow
                {
                    Date = x.Date,
                    Year = x.Date.Year,
                    Month = x.Date.Month,
                    Day = x.Date.Day,
                    PartNumber = x.PartNumber,
                    PartName = x.PartName,
                    UOM = x.UOM,
                    PartGroupId = x.PartGroupId,
                    PartGroupName = x.PartGroupName,
                    SupplierId = null,
                    SupplierName = null,
                    SupplierGroupId = null,
                    SupplierGroupName = null,
                    Quantity = x.Quantity,
                    Inward = 0m,
                    Outward = x.Quantity,
                    Rate = x.Rate ?? 0m,
                    Value = x.Value ?? 0m,
                    Direction = x.Direction,
                    Reference = x.Reference
                }));

                IEnumerable<OverallReportRow> filtered = rows;

                // DATE RANGE FILTER
                // The UI now uses From Date / To Date instead of separate
                // Year / Month / Day selectors. Keep the old Year/Month/Day
                // parameters for backward compatibility with older clients.
                if (fromDate.HasValue)
                {
                    var from = fromDate.Value.Date;
                    filtered = filtered.Where(x => x.Date.Date >= from);
                }

                if (toDate.HasValue)
                {
                    var toExclusive = toDate.Value.Date.AddDays(1);
                    filtered = filtered.Where(x => x.Date < toExclusive);
                }

                if (year.HasValue)
                    filtered = filtered.Where(x => x.Year == year.Value);
                if (month.HasValue)
                    filtered = filtered.Where(x => x.Month == month.Value);
                if (day.HasValue)
                    filtered = filtered.Where(x => x.Day == day.Value);

                if (!string.IsNullOrWhiteSpace(partNumber))
                    filtered = filtered.Where(x =>
                        string.Equals(x.PartNumber, partNumber.Trim(), StringComparison.OrdinalIgnoreCase));

                if (supplierId.HasValue)
                    filtered = filtered.Where(x => x.SupplierId == supplierId.Value);

                if (supplierGroupId.HasValue)
                    filtered = filtered.Where(x => x.SupplierGroupId == supplierGroupId.Value);

                if (itemGroupId.HasValue)
                    filtered = filtered.Where(x => x.PartGroupId == itemGroupId.Value);

                if (!string.IsNullOrWhiteSpace(direction) &&
                    !string.Equals(direction, "All", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = filtered.Where(x =>
                        string.Equals(x.Direction, direction.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                if (minValue.HasValue)
                    filtered = filtered.Where(x => x.Value >= minValue.Value);
                if (maxValue.HasValue)
                    filtered = filtered.Where(x => x.Value <= maxValue.Value);
                if (minRate.HasValue)
                    filtered = filtered.Where(x => x.Rate >= minRate.Value);
                if (maxRate.HasValue)
                    filtered = filtered.Where(x => x.Rate <= maxRate.Value);

                var ordered = filtered
                    .OrderByDescending(x => x.Date)
                    .ThenBy(x => x.PartNumber)
                    .ToList();

                var result = new
                {
                    data = exportAll
                        ? ordered
                        : ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                    totalRows = ordered.Count,
                    totalInward = ordered.Sum(x => x.Inward),
                    totalOutward = ordered.Sum(x => x.Outward),
                    totalValue = ordered.Sum(x => x.Value),
                    availableFilters = new
                    {
                        years = rows.Select(x => x.Year).Distinct().OrderByDescending(x => x).ToList(),
                        months = rows.Select(x => x.Month).Distinct().OrderBy(x => x).ToList(),
                        days = rows.Select(x => x.Day).Distinct().OrderBy(x => x).ToList(),
                        partNumbers = rows.Where(x => x.PartNumber != null)
                            .Select(x => x.PartNumber).Distinct().OrderBy(x => x).ToList(),
                        suppliers = rows.Where(x => x.SupplierId.HasValue)
                            .GroupBy(x => new { x.SupplierId, x.SupplierName })
                            .Select(g => new { id = g.Key.SupplierId, name = g.Key.SupplierName })
                            .OrderBy(x => x.name).ToList(),
                        supplierGroups = rows.Where(x => x.SupplierGroupId.HasValue)
                            .GroupBy(x => new { x.SupplierGroupId, x.SupplierGroupName })
                            .Select(g => new { id = g.Key.SupplierGroupId, name = g.Key.SupplierGroupName })
                            .OrderBy(x => x.name).ToList(),
                        partGroups = rows.Where(x => x.PartGroupId.HasValue)
                            .GroupBy(x => new { x.PartGroupId, x.PartGroupName })
                            .Select(g => new { id = g.Key.PartGroupId, name = g.Key.PartGroupName })
                            .OrderBy(x => x.name).ToList(),
                        customers = Array.Empty<object>(),
                        customerGroups = Array.Empty<object>()
                    },
                    customerFiltersAvailable = false,
                    customerFilterMessage =
                        "Customer and Customer Group are not linked to the GRN/Material Issue entities in the supplied schema."
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new
                {
                    message = $"Failed to load Overall Report: {detail}"
                });
            }
        }

        private sealed class OverallReportRow
        {
            public DateTime Date { get; set; }
            public int Year { get; set; }
            public int Month { get; set; }
            public int Day { get; set; }
            public string? PartNumber { get; set; }
            public string? PartName { get; set; }
            public string? UOM { get; set; }
            public int? PartGroupId { get; set; }
            public string? PartGroupName { get; set; }
            public int? SupplierId { get; set; }
            public string? SupplierName { get; set; }
            public int? SupplierGroupId { get; set; }
            public string? SupplierGroupName { get; set; }
            public decimal Quantity { get; set; }
            public decimal Inward { get; set; }
            public decimal Outward { get; set; }
            public decimal Rate { get; set; }
            public decimal Value { get; set; }
            public string Direction { get; set; } = "";
            public string? Reference { get; set; }
        }

    }
}
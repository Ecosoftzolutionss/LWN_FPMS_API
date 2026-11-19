using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DFN_BMS.DB;

namespace DFN_BMS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly AppDbContext _context;

        public DashboardController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // SINGLE SOURCE OF TRUTH FOR STOCK
        // ---------------------------------------------------------
        //   On-hand (per item) = Posted GRN qty (all dates)
        //                        - Material Issue qty (all dates)
        //                        (never below 0)
        //
        // The "Available Parts" KPI, the available value and the
        // Stock Status donut ALL use this same number, so they can
        // never disagree with each other.
        //
        // The previous code computed "Available" from pallet rows
        // (GrnPallets - issues linked to a pallet) while Stock
        // Status used GRN lines - all issues. Whenever
        //   * a posted GRN line has pallets that do not add up to
        //     the line quantity, or
        //   * an issue is not linked to a pallet (GrnPalletId empty),
        //   * or a pallet was over-issued (that pallet was dropped
        //     instead of being netted),
        // the two figures drifted apart. Use /stock-reconciliation
        // below to list exactly which items have that data problem.
        // =========================================================

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary(
            [FromQuery] int? year,
            [FromQuery] int? month,
            [FromQuery] string? partNo)
        {
            try
            {
                var today = DateTime.Today;
                var filterYear = year ?? today.Year;
                var filterMonth = month ?? today.Month;

                if (filterMonth < 1 || filterMonth > 12)
                    return BadRequest(new
                    {
                        message = "Month must be between 1 and 12."
                    });

                var monthStart = new DateTime(filterYear, filterMonth, 1);
                var monthEndExclusive = monthStart.AddMonths(1);

                // =====================================================
                // NORMALIZE PART NUMBER SEARCH
                // =====================================================

                var partNoFilter = string.IsNullOrWhiteSpace(partNo)
                    ? null
                    : partNo.Trim().ToLower();

                // Resolve matching ItemIds once.
                var matchingItemIds = partNoFilter == null
                    ? null
                    : await _context.ItemMasters
                        .Where(i =>
                            i.ItemNumber != null &&
                            i.ItemNumber.ToLower() == partNoFilter)
                        .Select(i => i.Id)
                        .ToListAsync();

                // =====================================================
                // INWARD - SELECTED MONTH (posted GRN lines)
                // =====================================================

                var inwardQuery = _context.GrnLines
                    .AsNoTracking()
                    .Where(l =>
                        l.IsPosted &&
                        l.Header != null &&
                        (l.PostedDate ??
                         l.Header.PostedDate ??
                         l.Header.CreatedDate) >= monthStart &&
                        (l.PostedDate ??
                         l.Header.PostedDate ??
                         l.Header.CreatedDate) < monthEndExclusive);

                if (matchingItemIds != null)
                {
                    inwardQuery = inwardQuery
                        .Where(l => matchingItemIds.Contains(l.ItemId));
                }

                var inwardLines = await inwardQuery.ToListAsync();

                var inwardQty = inwardLines.Sum(l => l.Quantity);
                var inwardPartCount = inwardQty;

                var inwardValue = inwardLines.Sum(
                    l => l.Quantity * l.Rate);

                // =====================================================
                // OUTWARD - SELECTED MONTH (material issues)
                // =====================================================

                var issuesQuery = _context.MaterialIssues
                    .AsNoTracking()
                    .Where(i =>
                        i.IssueDate >= monthStart &&
                        i.IssueDate < monthEndExclusive);

                if (matchingItemIds != null)
                {
                    issuesQuery = issuesQuery
                        .Where(i => matchingItemIds.Contains(i.ItemId));
                }

                var issuesThisMonth = await issuesQuery
                    .Include(i => i.Item)
                    .ToListAsync();

                var outwardQty = issuesThisMonth.Sum(i => i.Quantity);
                var outwardPartCount = outwardQty;

                var outwardValue = issuesThisMonth.Sum(
                    i => i.Quantity *
                    (i.Item != null ? i.Item.UnitPrice : 0m));

                // =====================================================
                // CURRENT STOCK  (ONE LEDGER FOR KPI + DONUT)
                // =====================================================

                var itemsQuery = _context.ItemMasters
                    .AsNoTracking()
                    .AsQueryable();

                if (matchingItemIds != null)
                {
                    itemsQuery = itemsQuery.Where(
                        i => matchingItemIds.Contains(i.Id));
                }

                var items = await itemsQuery.ToListAsync();

                var receivedQuery = _context.GrnLines
                    .AsNoTracking()
                    .Where(l => l.IsPosted);

                var issuedQuery = _context.MaterialIssues
                    .AsNoTracking()
                    .AsQueryable();

                if (matchingItemIds != null)
                {
                    receivedQuery = receivedQuery
                        .Where(l => matchingItemIds.Contains(l.ItemId));

                    issuedQuery = issuedQuery
                        .Where(i => matchingItemIds.Contains(i.ItemId));
                }

                var stockReceivedByItem = await receivedQuery
                    .GroupBy(l => l.ItemId)
                    .Select(g => new
                    {
                        ItemId = g.Key,
                        Qty = g.Sum(l => l.Quantity)
                    })
                    .ToDictionaryAsync(
                        x => x.ItemId,
                        x => x.Qty);

                var stockIssuedByItem = await issuedQuery
                    .GroupBy(i => i.ItemId)
                    .Select(g => new
                    {
                        ItemId = g.Key,
                        Qty = g.Sum(i => i.Quantity)
                    })
                    .ToDictionaryAsync(
                        x => x.ItemId,
                        x => x.Qty);

                int safetyCount = 0;
                int reorderCount = 0;
                int dangerCount = 0;

                decimal availableQty = 0m;
                decimal availableValue = 0m;

                foreach (var item in items)
                {
                    var received =
                        stockReceivedByItem.TryGetValue(
                            item.Id, out var r)
                            ? r
                            : 0m;

                    var issued =
                        stockIssuedByItem.TryGetValue(
                            item.Id, out var iss)
                            ? iss
                            : 0m;

                    var onHand = received - issued;

                    if (onHand < 0)
                        onHand = 0;

                    // KPI totals (same number the donut uses)
                    availableQty += onHand;
                    availableValue += onHand * item.UnitPrice;

                    // Stock status buckets
                    if (onHand >= item.SafetyLevel)
                        safetyCount++;

                    else if (onHand >= item.ReorderLevel)
                        reorderCount++;

                    else
                        dangerCount++;
                }

                var availablePartCount = availableQty;

                var totalItems = items.Count;

                int Pct(int count) =>
                    totalItems == 0
                        ? 0
                        : (int)Math.Round(
                            count * 100m / totalItems);

                var stockAlertCount =
                    reorderCount + dangerCount;

                // =====================================================
                // CHART - FIXED FISCAL YEAR (APRIL -> MARCH)
                // =====================================================

                var fiscalStartYear =
                    filterMonth >= 4
                        ? filterYear
                        : filterYear - 1;

                var fiscalStart = new DateTime(
                    fiscalStartYear, 4, 1);

                var chartMonths =
                    new List<(int Year, int Month,
                        DateTime Start, DateTime EndExclusive)>();

                for (var i = 0; i < 12; i++)
                {
                    var d = fiscalStart.AddMonths(i);

                    chartMonths.Add((
                        d.Year,
                        d.Month,
                        d,
                        d.AddMonths(1)
                    ));
                }

                var chartRangeStart = chartMonths.First().Start;
                var chartRangeEnd = chartMonths.Last().EndExclusive;

                var chartGrnQuery = _context.GrnLines
                    .AsNoTracking()
                    .Where(l =>
                        l.IsPosted &&
                        l.Header != null &&
                        (l.PostedDate ??
                         l.Header.PostedDate ??
                         l.Header.CreatedDate) >= chartRangeStart &&
                        (l.PostedDate ??
                         l.Header.PostedDate ??
                         l.Header.CreatedDate) < chartRangeEnd);

                if (matchingItemIds != null)
                {
                    chartGrnQuery = chartGrnQuery
                        .Where(l => matchingItemIds.Contains(l.ItemId));
                }

                var chartGrnLines = await chartGrnQuery
                    .Include(l => l.Header)
                    .ToListAsync();

                var chartIssuesQuery = _context.MaterialIssues
                    .AsNoTracking()
                    .Where(i =>
                        i.IssueDate >= chartRangeStart &&
                        i.IssueDate < chartRangeEnd);

                if (matchingItemIds != null)
                {
                    chartIssuesQuery = chartIssuesQuery
                        .Where(i => matchingItemIds.Contains(i.ItemId));
                }

                var chartIssues = await chartIssuesQuery
                    .Include(i => i.Item)
                    .ToListAsync();

                var chart = chartMonths.Select(cm =>
                {
                    var monthGrnLines = chartGrnLines
                        .Where(l =>
                            l.Header != null &&
                            (l.PostedDate ??
                             l.Header.PostedDate ??
                             l.Header.CreatedDate) >= cm.Start &&
                            (l.PostedDate ??
                             l.Header.PostedDate ??
                             l.Header.CreatedDate) < cm.EndExclusive)
                        .ToList();

                    var monthIssues = chartIssues
                        .Where(i =>
                            i.IssueDate >= cm.Start &&
                            i.IssueDate < cm.EndExclusive)
                        .ToList();

                    return new
                    {
                        // Numeric keys so the frontend never depends on
                        // server culture ("Sep" vs "Sept").
                        year = cm.Year,
                        month = cm.Month,

                        label = cm.Start.ToString(
                            "MMM yyyy",
                            CultureInfo.InvariantCulture),

                        inwardQty = monthGrnLines.Sum(
                            l => l.Quantity),

                        inwardValue = monthGrnLines.Sum(
                            l => l.Quantity * l.Rate),

                        outwardQty = monthIssues.Sum(
                            i => i.Quantity),

                        outwardValue = monthIssues.Sum(
                            i => i.Quantity *
                            (i.Item != null ? i.Item.UnitPrice : 0m))
                    };
                }).ToList();

                // =====================================================
                // RECENT ACTIVITIES
                // =====================================================

                var grnHeadersThisMonth = await _context.GrnHeaders
                    .Include(h => h.Supplier)
                    .Include(h => h.Lines)
                        .ThenInclude(l => l.Item)
                    .Where(h =>
                        h.CreatedDate >= monthStart &&
                        h.CreatedDate < monthEndExclusive)
                    .ToListAsync();

                if (matchingItemIds != null)
                {
                    grnHeadersThisMonth = grnHeadersThisMonth
                        .Where(h =>
                            h.Lines.Any(l =>
                                matchingItemIds.Contains(l.ItemId)))
                        .ToList();
                }

                var grnNumbers = issuesThisMonth
                    .Where(i =>
                        !string.IsNullOrWhiteSpace(i.GrnNumber))
                    .Select(i => i.GrnNumber)
                    .Distinct()
                    .ToList();

                var grnHeadersByNumber = await _context.GrnHeaders
                    .Include(h => h.Supplier)
                    .Where(h => grnNumbers.Contains(h.GrnNumber))
                    .ToDictionaryAsync(
                        h => h.GrnNumber,
                        h => h);

                var movementsThisMonth = await _context.StoreMovements
                    .Include(m => m.StorePosition)
                        .ThenInclude(sp => sp.Store)

                    .Include(m => m.RackRow)
                        .ThenInclude(r => r.Column)
                        .ThenInclude(c => c.Rack)
                        .ThenInclude(rk => rk.Store)
                        .ThenInclude(s => s.StoreMaster)

                    .Include(m => m.GrnPallet)
                        .ThenInclude(p => p!.GrnLine)
                        .ThenInclude(l => l!.Item)

                    .Include(m => m.GrnPallet)
                        .ThenInclude(p => p!.GrnLine)
                        .ThenInclude(l => l!.Header)
                        .ThenInclude(h => h!.Supplier)

                    .Where(m =>
                        m.GrnPalletId != null &&
                        m.MovementDate >= monthStart &&
                        m.MovementDate < monthEndExclusive)

                    .ToListAsync();

                if (matchingItemIds != null)
                {
                    movementsThisMonth = movementsThisMonth
                        .Where(m =>
                            m.GrnPallet?.GrnLine != null &&
                            matchingItemIds.Contains(
                                m.GrnPallet.GrnLine.ItemId))
                        .ToList();
                }

                // =====================================================
                // RECENT GRN ACTIVITIES
                // =====================================================

                var recentGrnActivities = grnHeadersThisMonth
                    .OrderByDescending(h => h.CreatedDate)
                    .Take(10)

                    .SelectMany(h =>
                        (matchingItemIds != null
                            ? h.Lines.Where(l =>
                                matchingItemIds.Contains(l.ItemId))
                            : h.Lines)

                        .Select(l => new
                        {
                            Type = "GRN Entry",
                            Date = h.CreatedDate,
                            RefNo = h.GrnNumber,

                            Supplier =
                                h.Supplier?.SupplierName ?? "—",

                            PartName =
                                l.Item?.ItemName ?? "—",

                            PartNumber =
                                l.Item?.ItemNumber ?? "—",

                            Quantity = l.Quantity,

                            UserName = h.CreatedBy ?? "—"
                        }))
                    .ToList();

                // =====================================================
                // RECENT MATERIAL ISSUE ACTIVITIES
                // =====================================================

                var recentIssueActivities = issuesThisMonth
                    .OrderByDescending(i => i.IssueDate)
                    .Take(10)

                    .Select(i => new
                    {
                        Type = "Material Issue",

                        Date = i.IssueDate,

                        RefNo = i.IssueNumber,

                        Supplier =
                            i.GrnNumber != null &&
                            grnHeadersByNumber.TryGetValue(
                                i.GrnNumber,
                                out var grnH)
                                ? (grnH.Supplier?.SupplierName ?? "—")
                                : "—",

                        PartName =
                            i.Item?.ItemName ?? "—",

                        PartNumber =
                            i.Item?.ItemNumber ?? "—",

                        Quantity = i.Quantity,

                        UserName = i.IssuedTo ?? "—"
                    })
                    .ToList();

                // =====================================================
                // RECENT STORE MOVEMENT ACTIVITIES
                // =====================================================

                var recentMovementActivities = movementsThisMonth
                    .OrderByDescending(m => m.MovementDate)
                    .Take(10)

                    .Select(m =>
                    {
                        var line = m.GrnPallet?.GrnLine;

                        return new
                        {
                            Type = "Store Movement",

                            Date = m.MovementDate,

                            RefNo =
                                line?.Header?.GrnNumber ?? "—",

                            Supplier =
                                line?.Header?.Supplier?.SupplierName ?? "—",

                            PartName =
                                line?.Item?.ItemName ?? "—",

                            PartNumber =
                                line?.Item?.ItemNumber ?? "—",

                            Quantity = m.Quantity,

                            UserName = m.CreatedBy ?? "—"
                        };
                    })
                    .ToList();

                // =====================================================
                // COMBINE RECENT ACTIVITIES
                // =====================================================

                var recentActivities = recentGrnActivities
                    .Concat(recentIssueActivities)
                    .Concat(recentMovementActivities)
                    .OrderByDescending(x => x.Date)
                    .Take(20)
                    .ToList();

                // =====================================================
                // FINAL RESPONSE
                // =====================================================

                return Ok(new
                {
                    filter = new
                    {
                        year = filterYear,
                        month = filterMonth,
                        partNo = partNoFilter
                    },

                    kpi = new
                    {
                        inwardPartCount,
                        outwardPartCount,
                        availablePartCount,

                        inwardQty,
                        outwardQty,
                        availableQty,

                        inwardValue,
                        outwardValue,
                        availableValue
                    },

                    chart,

                    stockStatus = new
                    {
                        totalItems,

                        safety = new
                        {
                            count = safetyCount,
                            pct = Pct(safetyCount)
                        },

                        reorder = new
                        {
                            count = reorderCount,
                            pct = Pct(reorderCount)
                        },

                        danger = new
                        {
                            count = dangerCount,
                            pct = Pct(dangerCount)
                        }
                    },

                    stockAlerts = stockAlertCount,

                    recentActivities
                });
            }
            catch (Exception ex)
            {
                var detail =
                    ex.InnerException?.Message ?? ex.Message;

                return StatusCode(500, new
                {
                    message = $"Failed to load dashboard: {detail}"
                });
            }
        }

        // =========================================================
        // STOCK RECONCILIATION (diagnostic)
        // ---------------------------------------------------------
        // GET api/dashboard/stock-reconciliation
        // GET api/dashboard/stock-reconciliation?partNo=2421003050
        // GET api/dashboard/stock-reconciliation?onlyMismatch=false
        //
        // Compares the two ways stock can be calculated and tells
        // you WHY they differ for each item:
        //
        //  ledgerOnHand        = posted GRN qty - all issues
        //  palletOnHand        = pallet qty     - issues linked to pallets
        //  receivedNotInPallets= GRN line qty that has no pallet qty
        //  issuedWithoutPallet = issues with no / unknown pallet link
        //
        //  difference = receivedNotInPallets - issuedWithoutPallet
        // =========================================================

        [HttpGet("stock-reconciliation")]
        public async Task<IActionResult> GetStockReconciliation(
            [FromQuery] string? partNo,
            [FromQuery] bool onlyMismatch = true)
        {
            try
            {
                var partNoFilter = string.IsNullOrWhiteSpace(partNo)
                    ? null
                    : partNo.Trim().ToLower();

                var matchingItemIds = partNoFilter == null
                    ? null
                    : await _context.ItemMasters
                        .Where(i =>
                            i.ItemNumber != null &&
                            i.ItemNumber.ToLower() == partNoFilter)
                        .Select(i => i.Id)
                        .ToListAsync();

                // ---- ledger (GRN line based) ----

                var receivedQuery = _context.GrnLines
                    .AsNoTracking()
                    .Where(l => l.IsPosted);

                var issuedQuery = _context.MaterialIssues
                    .AsNoTracking()
                    .AsQueryable();

                if (matchingItemIds != null)
                {
                    receivedQuery = receivedQuery
                        .Where(l => matchingItemIds.Contains(l.ItemId));

                    issuedQuery = issuedQuery
                        .Where(i => matchingItemIds.Contains(i.ItemId));
                }

                var receivedByItem = await receivedQuery
                    .GroupBy(l => l.ItemId)
                    .Select(g => new
                    {
                        ItemId = g.Key,
                        Qty = g.Sum(l => l.Quantity)
                    })
                    .ToDictionaryAsync(x => x.ItemId, x => x.Qty);

                var issuedByItem = await issuedQuery
                    .GroupBy(i => i.ItemId)
                    .Select(g => new
                    {
                        ItemId = g.Key,
                        Qty = g.Sum(i => i.Quantity)
                    })
                    .ToDictionaryAsync(x => x.ItemId, x => x.Qty);

                // ---- pallet based ----

                var palletQuery =
                    from p in _context.GrnPallets.AsNoTracking()
                    join l in _context.GrnLines.AsNoTracking()
                        on p.GrnLineId equals l.Id
                    where l.IsPosted
                    select new
                    {
                        l.ItemId,
                        PalletQty = p.Quantity,
                        IssuedQuantity =
                            _context.MaterialIssues
                                .Where(m => m.GrnPalletId == p.Id)
                                .Sum(m => (decimal?)m.Quantity) ?? 0m
                    };

                if (matchingItemIds != null)
                {
                    palletQuery = palletQuery
                        .Where(p => matchingItemIds.Contains(p.ItemId));
                }

                var palletRows = await palletQuery.ToListAsync();

                var palletByItem = palletRows
                    .GroupBy(x => x.ItemId)
                    .ToDictionary(
                        g => g.Key,
                        g => new
                        {
                            Qty = g.Sum(x => x.PalletQty),
                            Issued = g.Sum(x => x.IssuedQuantity),
                            OverIssued = g
                                .Where(x => x.IssuedQuantity > x.PalletQty)
                                .Sum(x => x.IssuedQuantity - x.PalletQty)
                        });

                // ---- combine ----

                var itemIds = receivedByItem.Keys
                    .Union(issuedByItem.Keys)
                    .Union(palletByItem.Keys)
                    .ToList();

                var itemInfo = await _context.ItemMasters
                    .AsNoTracking()
                    .Where(i => itemIds.Contains(i.Id))
                    .ToDictionaryAsync(i => i.Id, i => i);

                var rows = itemIds.Select(id =>
                {
                    var received = receivedByItem.TryGetValue(id, out var rc) ? rc : 0m;
                    var issued = issuedByItem.TryGetValue(id, out var isd) ? isd : 0m;

                    decimal palletQty = 0m, issuedOnPallets = 0m, overIssued = 0m;

                    if (palletByItem.TryGetValue(id, out var pb))
                    {
                        palletQty = pb.Qty;
                        issuedOnPallets = pb.Issued;
                        overIssued = pb.OverIssued;
                    }

                    var ledgerOnHand = received - issued;
                    var palletOnHand = palletQty - issuedOnPallets;
                    var receivedNotInPallets = received - palletQty;
                    var issuedWithoutPallet = issued - issuedOnPallets;

                    var reasons = new List<string>();

                    if (receivedNotInPallets != 0)
                        reasons.Add("Posted GRN line qty does not match its pallet qty");

                    if (issuedWithoutPallet != 0)
                        reasons.Add("Material issue is not linked to a pallet");

                    if (overIssued > 0)
                        reasons.Add("A pallet was issued more than its quantity");

                    if (ledgerOnHand < 0)
                        reasons.Add("Issued more than received (stock would be negative)");

                    itemInfo.TryGetValue(id, out var info);

                    return new
                    {
                        itemId = id,
                        itemNumber = info?.ItemNumber,
                        itemName = info?.ItemName,

                        received,
                        issued,
                        ledgerOnHand,

                        palletQty,
                        issuedOnPallets,
                        palletOnHand,

                        receivedNotInPallets,
                        issuedWithoutPallet,
                        overIssuedPalletQty = overIssued,

                        difference = ledgerOnHand - palletOnHand,
                        reasons
                    };
                })
                .Where(r => !onlyMismatch || r.reasons.Count > 0)
                .OrderByDescending(r => Math.Abs(r.difference))
                .ToList();

                return Ok(new
                {
                    totalItemsChecked = itemIds.Count,
                    mismatchCount = rows.Count(r => r.reasons.Count > 0),
                    rows
                });
            }
            catch (Exception ex)
            {
                var detail =
                    ex.InnerException?.Message ?? ex.Message;

                return StatusCode(500, new
                {
                    message = $"Failed to build reconciliation: {detail}"
                });
            }
        }
    }
}
using System;
using System.Collections.Generic;
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
                    return BadRequest(new { message = "Month must be between 1 and 12." });

                var monthStart = new DateTime(filterYear, filterMonth, 1);
                var monthEndExclusive = monthStart.AddMonths(1);

                // Normalize search term once
                var partNoFilter = string.IsNullOrWhiteSpace(partNo)
                    ? null
                    : partNo.Trim().ToLower();

                // Resolve matching ItemIds once — reused everywhere below
                var matchingItemIds = partNoFilter == null
                    ? null
                    : await _context.ItemMasters
                        .Where(i => i.ItemNumber != null &&
                                    i.ItemNumber.ToLower().Contains(partNoFilter))
                        .Select(i => i.Id)
                        .ToListAsync();

                // =====================================================
                // INWARD - SELECTED MONTH
                // =====================================================
                var inwardLines = await _context.GrnLines
                    .Where(l => l.IsPosted && l.Header != null &&
                        (l.PostedDate ?? l.Header.PostedDate ?? l.Header.CreatedDate) >= monthStart &&
                        (l.PostedDate ?? l.Header.PostedDate ?? l.Header.CreatedDate) < monthEndExclusive)
                    .ToListAsync();

                if (matchingItemIds != null)
                    inwardLines = inwardLines.Where(l => matchingItemIds.Contains(l.ItemId)).ToList();

                var inwardQty = inwardLines.Sum(l => l.Quantity);
                var inwardPartCount = inwardQty;
                var inwardValue = inwardLines.Sum(l => l.Quantity * l.Rate);

                // =====================================================
                // OUTWARD - SELECTED MONTH
                // =====================================================
                var issuesThisMonth = await _context.MaterialIssues
                    .Include(i => i.Item)
                    .Where(i => i.IssueDate >= monthStart && i.IssueDate < monthEndExclusive)
                    .ToListAsync();

                if (matchingItemIds != null)
                    issuesThisMonth = issuesThisMonth.Where(i => matchingItemIds.Contains(i.ItemId)).ToList();

                var outwardQty = issuesThisMonth.Sum(i => i.Quantity);
                var outwardPartCount = outwardQty;
                var outwardValue = issuesThisMonth.Sum(i => i.Quantity * (i.Item != null ? i.Item.UnitPrice : 0m));

                // =====================================================
                // CURRENT AVAILABLE STOCK (filtered by part)
                // =====================================================
                var receivedQuery = _context.GrnLines.Where(l => l.IsPosted);
                var issuedQuery = _context.MaterialIssues.AsQueryable();

                if (matchingItemIds != null)
                {
                    receivedQuery = receivedQuery.Where(l => matchingItemIds.Contains(l.ItemId));
                    issuedQuery = issuedQuery.Where(i => matchingItemIds.Contains(i.ItemId));
                }

                var totalReceivedQty = await receivedQuery.SumAsync(l => l.Quantity);
                var totalIssuedQty = await issuedQuery.SumAsync(i => i.Quantity);

                var availableQty = totalReceivedQty - totalIssuedQty;
                if (availableQty < 0) availableQty = 0;
                var availablePartCount = availableQty;

                var receivedByItem = await receivedQuery
                    .GroupBy(l => l.ItemId)
                    .Select(g => new { ItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
                    .ToListAsync();

                var issuedByItem = await issuedQuery
                    .GroupBy(i => i.ItemId)
                    .Select(g => new { ItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
                    .ToListAsync();

                var issuedDictionary = issuedByItem.ToDictionary(x => x.ItemId, x => x.Quantity);

                var itemPriceDictionary = await _context.ItemMasters
                    .ToDictionaryAsync(x => x.Id, x => x.UnitPrice);

                decimal availableValue = 0m;
                foreach (var received in receivedByItem)
                {
                    var issuedQty = issuedDictionary.TryGetValue(received.ItemId, out var issued) ? issued : 0m;
                    var itemAvailableQty = received.Quantity - issuedQty;
                    if (itemAvailableQty <= 0) continue;
                    var unitPrice = itemPriceDictionary.TryGetValue(received.ItemId, out var price) ? price : 0m;
                    availableValue += itemAvailableQty * unitPrice;
                }

                // =====================================================
                // CHART - FIXED FISCAL YEAR (April -> March)
                // =====================================================
                // Always exactly 12 months, Apr -> Mar of the fiscal
                // year containing the selected month/year.
                //
                // partNo filter ONLY changes each month's totals below
                // (via matchingItemIds) — it NEVER changes which
                // months appear on the chart. Selecting any month
                // inside a fiscal year always renders the SAME 12
                // month axis for that fiscal year.
                // =====================================================

                var fiscalStartYear = filterMonth >= 4 ? filterYear : filterYear - 1;
                var fiscalStart = new DateTime(fiscalStartYear, 4, 1);

                var chartMonths =
                    new List<(int Year, int Month, DateTime Start, DateTime EndExclusive)>();

                for (var i = 0; i < 12; i++)
                {
                    var d = fiscalStart.AddMonths(i);
                    chartMonths.Add((d.Year, d.Month, d, d.AddMonths(1)));
                }

                var chartRangeStart = chartMonths.First().Start;
                var chartRangeEnd = chartMonths.Last().EndExclusive;

                var chartGrnLines = await _context.GrnLines
                    .Include(l => l.Header)
                    .Where(l => l.IsPosted && l.Header != null &&
                        (l.PostedDate ?? l.Header.PostedDate ?? l.Header.CreatedDate) >= chartRangeStart &&
                        (l.PostedDate ?? l.Header.PostedDate ?? l.Header.CreatedDate) < chartRangeEnd)
                    .ToListAsync();

                if (matchingItemIds != null)
                    chartGrnLines = chartGrnLines.Where(l => matchingItemIds.Contains(l.ItemId)).ToList();

                var chartIssues = await _context.MaterialIssues
                    .Include(i => i.Item)
                    .Where(i => i.IssueDate >= chartRangeStart && i.IssueDate < chartRangeEnd)
                    .ToListAsync();

                if (matchingItemIds != null)
                    chartIssues = chartIssues.Where(i => matchingItemIds.Contains(i.ItemId)).ToList();

                var chart = chartMonths.Select(cm =>
                {
                    var monthGrnLines = chartGrnLines.Where(l => l.Header != null &&
                        (l.PostedDate ?? l.Header.PostedDate ?? l.Header.CreatedDate) >= cm.Start &&
                        (l.PostedDate ?? l.Header.PostedDate ?? l.Header.CreatedDate) < cm.EndExclusive).ToList();

                    var monthIssues = chartIssues.Where(i => i.IssueDate >= cm.Start && i.IssueDate < cm.EndExclusive).ToList();

                    return new
                    {
                        label = cm.Start.ToString("MMM yyyy"),
                        inwardQty = monthGrnLines.Sum(l => l.Quantity),
                        inwardValue = monthGrnLines.Sum(l => l.Quantity * l.Rate),
                        outwardQty = monthIssues.Sum(i => i.Quantity),
                        outwardValue = monthIssues.Sum(i => i.Quantity * (i.Item != null ? i.Item.UnitPrice : 0m))
                    };
                }).ToList();

                // =====================================================
                // STOCK STATUS (restricted to matching items)
                // =====================================================
                var itemsQuery = _context.ItemMasters.AsQueryable();
                if (matchingItemIds != null)
                    itemsQuery = itemsQuery.Where(i => matchingItemIds.Contains(i.Id));

                var items = await itemsQuery.ToListAsync();

                var stockReceivedByItem = await _context.GrnLines
                    .Where(l => l.IsPosted)
                    .GroupBy(l => l.ItemId)
                    .Select(g => new { ItemId = g.Key, Qty = g.Sum(l => l.Quantity) })
                    .ToDictionaryAsync(x => x.ItemId, x => x.Qty);

                var stockIssuedByItem = await _context.MaterialIssues
                    .GroupBy(i => i.ItemId)
                    .Select(g => new { ItemId = g.Key, Qty = g.Sum(i => i.Quantity) })
                    .ToDictionaryAsync(x => x.ItemId, x => x.Qty);

                int safetyCount = 0, reorderCount = 0, dangerCount = 0;
                foreach (var item in items)
                {
                    var received = stockReceivedByItem.TryGetValue(item.Id, out var r) ? r : 0m;
                    var issued = stockIssuedByItem.TryGetValue(item.Id, out var iss) ? iss : 0m;
                    var onHand = received - issued;
                    if (onHand < 0) onHand = 0;

                    if (onHand >= item.SafetyLevel) safetyCount++;
                    else if (onHand >= item.ReorderLevel) reorderCount++;
                    else dangerCount++;
                }

                var totalItems = items.Count;

                int Pct(int count) => totalItems == 0 ? 0 : (int)Math.Round(count * 100m / totalItems);

                var stockAlertCount = reorderCount + dangerCount;

                // =====================================================
                // RECENT ACTIVITIES (filtered by part, + PartNumber added)
                // =====================================================
                var grnHeadersThisMonth = await _context.GrnHeaders
                    .Include(h => h.Supplier)
                    .Include(h => h.Lines).ThenInclude(l => l.Item)
                    .Where(h => h.CreatedDate >= monthStart && h.CreatedDate < monthEndExclusive)
                    .ToListAsync();

                if (matchingItemIds != null)
                    grnHeadersThisMonth = grnHeadersThisMonth
                        .Where(h => h.Lines.Any(l => matchingItemIds.Contains(l.ItemId)))
                        .ToList();

                var grnNumbers = issuesThisMonth
                    .Where(i => !string.IsNullOrWhiteSpace(i.GrnNumber))
                    .Select(i => i.GrnNumber)
                    .Distinct()
                    .ToList();

                var grnHeadersByNumber = await _context.GrnHeaders
                    .Include(h => h.Supplier)
                    .Where(h => grnNumbers.Contains(h.GrnNumber))
                    .ToDictionaryAsync(h => h.GrnNumber, h => h);

                var movementsThisMonth = await _context.StoreMovements
                    .Include(m => m.StorePosition).ThenInclude(sp => sp.Store)
                    .Include(m => m.RackRow).ThenInclude(r => r.Column).ThenInclude(c => c.Rack).ThenInclude(rk => rk.Store).ThenInclude(s => s.StoreMaster)
                    .Include(m => m.GrnPallet).ThenInclude(p => p!.GrnLine).ThenInclude(l => l!.Item)
                    .Include(m => m.GrnPallet).ThenInclude(p => p!.GrnLine).ThenInclude(l => l!.Header).ThenInclude(h => h!.Supplier)
                    .Where(m => m.GrnPalletId != null && m.MovementDate >= monthStart && m.MovementDate < monthEndExclusive)
                    .ToListAsync();

                if (matchingItemIds != null)
                    movementsThisMonth = movementsThisMonth
                        .Where(m => m.GrnPallet?.GrnLine != null && matchingItemIds.Contains(m.GrnPallet.GrnLine.ItemId))
                        .ToList();

                var recentGrnActivities = grnHeadersThisMonth
                    .OrderByDescending(h => h.CreatedDate)
                    .Take(10)
                    .SelectMany(h => (matchingItemIds != null
                            ? h.Lines.Where(l => matchingItemIds.Contains(l.ItemId))
                            : h.Lines)
                        .Select(l => new
                        {
                            Type = "GRN Entry",
                            Date = h.CreatedDate,
                            RefNo = h.GrnNumber,
                            Supplier = h.Supplier?.SupplierName ?? "—",
                            PartName = l.Item?.ItemName ?? "—",
                            PartNumber = l.Item?.ItemNumber ?? "—",
                            Quantity = l.Quantity,
                            UserName = h.CreatedBy ?? "—"
                        }))
                    .ToList();

                var recentIssueActivities = issuesThisMonth
                    .OrderByDescending(i => i.IssueDate)
                    .Take(10)
                    .Select(i => new
                    {
                        Type = "Material Issue",
                        Date = i.IssueDate,
                        RefNo = i.IssueNumber,
                        Supplier = i.GrnNumber != null && grnHeadersByNumber.TryGetValue(i.GrnNumber, out var grnH)
                            ? (grnH.Supplier?.SupplierName ?? "—")
                            : "—",
                        PartName = i.Item?.ItemName ?? "—",
                        PartNumber = i.Item?.ItemNumber ?? "—",
                        Quantity = i.Quantity,
                        UserName = i.IssuedTo ?? "—"
                    })
                    .ToList();

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
                            RefNo = line?.Header?.GrnNumber ?? "—",
                            Supplier = line?.Header?.Supplier?.SupplierName ?? "—",
                            PartName = line?.Item?.ItemName ?? "—",
                            PartNumber = line?.Item?.ItemNumber ?? "—",
                            Quantity = m.Quantity,
                            UserName = m.CreatedBy ?? "—"
                        };
                    })
                    .ToList();

                var recentActivities = recentGrnActivities
                    .Concat(recentIssueActivities)
                    .Concat(recentMovementActivities)
                    .OrderByDescending(x => x.Date)
                    .Take(20)
                    .ToList();

                return Ok(new
                {
                    filter = new { year = filterYear, month = filterMonth, partNo = partNoFilter },
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
                        safety = new { count = safetyCount, pct = Pct(safetyCount) },
                        reorder = new { count = reorderCount, pct = Pct(reorderCount) },
                        danger = new { count = dangerCount, pct = Pct(dangerCount) }
                    },
                    stockAlerts = stockAlertCount,
                    recentActivities
                });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Failed to load dashboard: {detail}" });
            }
        }
    }
}
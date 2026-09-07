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

        // GET:
        // api/Dashboard/summary?year=2026&month=9
        //
        // NOTE ON SCOPE (read this before changing the numbers below):
        //   - Inward / Outward (KPI cards + chart)  -> scoped to the
        //     selected Year + Month. "Inward" = pallets created in that
        //     month. "Outward" = distinct pallets issued in that month
        //     (the pallet itself may have been created in an earlier
        //     month — that's fine, it's still an outward EVENT this month).
        //   - Available (KPI card) and Stock Status donut -> NOT scoped
        //     to the filter. Both represent the live, current state of
        //     the warehouse (as of "now"), because "how much is on hand
        //     right now" isn't a property of a calendar month.
        //   - Recent Activities -> scoped to the selected Year + Month,
        //     same as before.
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary(
            [FromQuery] int? year,
            [FromQuery] int? month)
        {
            try
            {
                var today = DateTime.Today;
                var filterYear = year ?? today.Year;
                var filterMonth = month ?? today.Month;

                if (filterMonth < 1 || filterMonth > 12)
                {
                    return BadRequest(new { message = "Month must be between 1 and 12." });
                }

                var monthStart = new DateTime(filterYear, filterMonth, 1);
                var monthEndExclusive = monthStart.AddMonths(1);

                // =========================================================
                // INWARD (pallets created this month)
                // =========================================================

                var inwardPallets = await _context.GrnPallets
                    .Where(p => p.CreatedDate >= monthStart && p.CreatedDate < monthEndExclusive)
                    .ToListAsync();

                var inwardQty = inwardPallets.Count;
                var inwardValue = inwardPallets.Sum(p => p.Quantity * p.Rate);

                // =========================================================
                // OUTWARD (pallets issued this month — issue EVENTS, not
                // tied to when the pallet was created)
                // =========================================================

                var issuesThisMonth = await _context.MaterialIssues
                    .Include(i => i.Item)
                    .Where(i => i.IssueDate >= monthStart && i.IssueDate < monthEndExclusive)
                    .ToListAsync();

                var outwardQty = issuesThisMonth
                    .Where(i => i.PalletNo != null)
                    .Select(i => i.PalletNo)
                    .Distinct()
                    .Count();

                var outwardValue = issuesThisMonth.Sum(
                    i => i.Quantity * (i.Item != null ? i.Item.UnitPrice : 0m));

                // =========================================================
                // AVAILABLE (all-time current snapshot — stuffed & not
                // issued, as of right now)
                // =========================================================

                var allPallets = await _context.GrnPallets.ToListAsync();

                var allIssuedPalletNos = await _context.MaterialIssues
                    .Where(i => i.PalletNo != null)
                    .Select(i => i.PalletNo)
                    .Distinct()
                    .ToListAsync();
                var issuedSet = allIssuedPalletNos.ToHashSet();

                var stuffedPalletIdsAllTime = await _context.StoreMovements
                    .Where(m => m.GrnPalletId != null)
                    .Select(m => m.GrnPalletId!.Value)
                    .Distinct()
                    .ToListAsync();
                var stuffedSet = stuffedPalletIdsAllTime.ToHashSet();

                var availablePalletsNow = allPallets
                    .Where(p => stuffedSet.Contains(p.Id)
                                && !(p.PalletNo != null && issuedSet.Contains(p.PalletNo)))
                    .ToList();

                var availableQty = availablePalletsNow.Count;
                var availableValue = availablePalletsNow.Sum(p => p.Quantity * p.Rate);

                // =========================================================
                // INWARD vs OUTWARD CHART — trailing 8 months, ending at
                // the selected month (inclusive), crossing year boundaries
                // if needed.
                // =========================================================

                var chartMonths = new List<(int Year, int Month, DateTime Start, DateTime EndExclusive)>();
                for (var i = 7; i >= 0; i--)
                {
                    var d = monthStart.AddMonths(-i);
                    chartMonths.Add((d.Year, d.Month, d, d.AddMonths(1)));
                }

                var chartRangeStart = chartMonths.First().Start;
                var chartRangeEnd = chartMonths.Last().EndExclusive;

                var chartPallets = await _context.GrnPallets
                    .Where(p => p.CreatedDate >= chartRangeStart && p.CreatedDate < chartRangeEnd)
                    .ToListAsync();

                var chartIssues = await _context.MaterialIssues
                    .Include(i => i.Item)
                    .Where(i => i.IssueDate >= chartRangeStart && i.IssueDate < chartRangeEnd)
                    .ToListAsync();

                var chart = chartMonths.Select(cm =>
                {
                    var monthPallets = chartPallets
                        .Where(p => p.CreatedDate >= cm.Start && p.CreatedDate < cm.EndExclusive)
                        .ToList();

                    var monthIssues = chartIssues
                        .Where(i => i.IssueDate >= cm.Start && i.IssueDate < cm.EndExclusive)
                        .ToList();

                    var monthOutwardQty = monthIssues
                        .Where(i => i.PalletNo != null)
                        .Select(i => i.PalletNo)
                        .Distinct()
                        .Count();

                    return new
                    {
                        label = cm.Start.ToString("MMM yyyy"),
                        inwardQty = monthPallets.Count,
                        inwardValue = monthPallets.Sum(p => p.Quantity * p.Rate),
                        outwardQty = monthOutwardQty,
                        outwardValue = monthIssues.Sum(
                            i => i.Quantity * (i.Item != null ? i.Item.UnitPrice : 0m)),
                    };
                }).ToList();

                // =========================================================
                // STOCK STATUS DONUT — all-time, per ItemMaster, bucketed
                // by on-hand qty vs SafetyLevel / ReorderLevel.
                //   on-hand >= SafetyLevel               -> Safety   (green)
                //   ReorderLevel <= on-hand < SafetyLevel -> Reorder  (orange)
                //   on-hand < ReorderLevel                -> Danger   (red)
                // =========================================================

                var items = await _context.ItemMasters.ToListAsync();

                var receivedByItem = await _context.GrnLines
                    .Where(l => l.IsPosted)
                    .GroupBy(l => l.ItemId)
                    .Select(g => new { ItemId = g.Key, Qty = g.Sum(l => l.Quantity) })
                    .ToDictionaryAsync(x => x.ItemId, x => x.Qty);

                var issuedByItem = await _context.MaterialIssues
                    .GroupBy(i => i.ItemId)
                    .Select(g => new { ItemId = g.Key, Qty = g.Sum(i => i.Quantity) })
                    .ToDictionaryAsync(x => x.ItemId, x => x.Qty);

                int safetyCount = 0, reorderCount = 0, dangerCount = 0;

                foreach (var item in items)
                {
                    var received = receivedByItem.TryGetValue(item.Id, out var r) ? r : 0m;
                    var issued = issuedByItem.TryGetValue(item.Id, out var iss) ? iss : 0m;
                    var onHand = received - issued;

                    if (onHand >= item.SafetyLevel)
                    {
                        safetyCount++;
                    }
                    else if (onHand >= item.ReorderLevel)
                    {
                        reorderCount++;
                    }
                    else
                    {
                        dangerCount++;
                    }
                }

                var totalItems = items.Count;

                int Pct(int part) => totalItems > 0 ? (int)Math.Round(part * 100m / totalItems) : 0;

                // =========================================================
                // RECENT ACTIVITIES — scoped to selected month, with
                // Supplier + Part Name resolved per activity type.
                // =========================================================

                var grnHeadersThisMonth = await _context.GrnHeaders
                    .Include(h => h.Supplier)
                    .Include(h => h.Lines)
                        .ThenInclude(l => l.Item)
                    .Where(h => h.CreatedDate >= monthStart && h.CreatedDate < monthEndExclusive)
                    .ToListAsync();

                // Needed so Material Issue rows can resolve a supplier via
                // the GRN number recorded on the issue (Material Issue has
                // no supplier of its own).
                var grnNumbers = issuesThisMonth
                    .Where(i => i.GrnNumber != null)
                    .Select(i => i.GrnNumber)
                    .Distinct()
                    .ToList();

                var grnHeadersByNumber = await _context.GrnHeaders
                    .Include(h => h.Supplier)
                    .Where(h => grnNumbers.Contains(h.GrnNumber))
                    .ToDictionaryAsync(h => h.GrnNumber, h => h);

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
                    .Where(m => m.GrnPalletId != null
                                && m.MovementDate >= monthStart
                                && m.MovementDate < monthEndExclusive)
                    .ToListAsync();

                var recentGrnActivities = grnHeadersThisMonth
                    .OrderByDescending(h => h.CreatedDate)
                    .Take(10)
                    .Select(h => new
                    {
                        Type = "GRN Entry",
                        Date = h.CreatedDate,
                        RefNo = h.GrnNumber,
                        Supplier = h.Supplier?.SupplierName ?? "—",
                        PartName = h.Lines.Count == 1
                            ? (h.Lines.First().Item?.ItemName ?? "—")
                            : $"Multiple Items ({h.Lines.Count})",
                        Quantity = h.Lines.Sum(l => (decimal?)l.Quantity) ?? 0,
                        UserName = h.CreatedBy ?? "—",
                    })
                    .ToList();

                var recentIssueActivities = issuesThisMonth
                    .OrderByDescending(i => i.IssueDate)
                    .Take(10)
                    .Select(i => new
                    {
                        Type = "Material Issue",
                        Date = i.IssueDate,
                        RefNo = i.IssueNumber,
                        Supplier = (i.GrnNumber != null
                                    && grnHeadersByNumber.TryGetValue(i.GrnNumber, out var grnH))
                            ? (grnH.Supplier?.SupplierName ?? "—")
                            : "—",
                        PartName = i.Item?.ItemName ?? "—",
                        Quantity = i.Quantity,
                        UserName = i.IssuedTo ?? "—",
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
                            Quantity = m.Quantity,
                            UserName = m.CreatedBy ?? "—",
                        };
                    })
                    .ToList();

                var recentActivities = recentGrnActivities
                    .Concat(recentIssueActivities)
                    .Concat(recentMovementActivities)
                    .OrderByDescending(a => a.Date)
                    .Take(20)
                    .ToList();

                // =========================================================
                // RESPONSE
                // =========================================================

                return Ok(new
                {
                    filter = new { year = filterYear, month = filterMonth },

                    kpi = new
                    {
                        inwardQty,
                        inwardValue,
                        outwardQty,
                        outwardValue,
                        availableQty,
                        availableValue,
                    },

                    chart,

                    stockStatus = new
                    {
                        totalItems,
                        safety = new { count = safetyCount, pct = Pct(safetyCount) },
                        reorder = new { count = reorderCount, pct = Pct(reorderCount) },
                        danger = new { count = dangerCount, pct = Pct(dangerCount) },
                    },

                    recentActivities,
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

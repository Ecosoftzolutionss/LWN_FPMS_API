using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DFN_BMS.DB;
using DFN_BMS.Models;

namespace DFN_BMS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GrnEntryController : ControllerBase
    {
        private readonly AppDbContext _context;

        private static readonly string[] ValidGrnTypes = { "Regular", "Sample" };
        private static readonly System.Text.RegularExpressions.Regex InvoiceNumberRegex =
            new System.Text.RegularExpressions.Regex(@"^[0-9]+$");

        public GrnEntryController(AppDbContext context)
        {
            _context = context;
        }
        // the UI to override an already-running auto-sequence mid-way.
        // the UI to override an already-running auto-sequence mid-way.
        public class GrnLineRequest
        {
            public int ItemId { get; set; }
            public string Uom { get; set; }
            public decimal? PalletQuantity { get; set; }
            public decimal Rate { get; set; }
            public decimal Quantity { get; set; }
        }

        public class GrnCreateRequest
        {
            public int SupplierId { get; set; }
            public string PoNumber { get; set; }
            public DateTime PoDate { get; set; }
            public string GrnType { get; set; }
            public string SupplierInvoiceNumber { get; set; }
            public DateTime SupplierInvoiceDate { get; set; }
            public string? GrnNo { get; set; }   // manually-typed GRN No (seed OR override)

            // ★ NEW: true when the user explicitly ticked "Edit GRN No" in
            // the UI to override an already-running auto-sequence mid-way.
            // false/omitted means normal auto-generation (or first-ever seed).
            public string? CreatedBy { get; set; }
            public bool OverrideGrnNo { get; set; } = false;

            public List<GrnLineRequest> Lines { get; set; } = new List<GrnLineRequest>();

        }

        // GET: api/StoreMaster/configured-parts
        // Returns only Part Numbers that have a Store Master configuration
        // (a StoreMaster row with PartNumberId set). GRN posting requires this
        // configuration to generate a Pallet No, so GRN Entry's Part Number
        // dropdown should only offer parts that are actually postable.

        // GET: api/GrnEntry?posted=false
        // posted omitted -> everything. posted=false -> GRNs that still
        // have at least one un-posted line ("Show All GRN Post" tab).
        // posted=true -> GRNs with at least one posted line ("GRN
        // Reprint" tab). Posting now happens per-line, not per-header.
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] bool? posted)
        {
            var query = _context.GrnHeaders
                .Include(x => x.Supplier)
                .Include(x => x.Lines)
                .AsQueryable();

            if (posted.HasValue)
            {
                query = posted.Value
                    ? query.Where(x => x.Lines.Any(l => l.IsPosted))
                    : query.Where(x => x.Lines.Any(l => !l.IsPosted));
            }

            var list = await query
                .OrderByDescending(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.GrnNumber,
                    SupplierName = x.Supplier.SupplierName,
                    x.SupplierInvoiceNumber,
                    x.SupplierInvoiceDate,
                    x.PoNumber,
                    x.PoDate,
                    x.GrnType,
                    LineCount = x.Lines.Count,
                    PostedLineCount = x.Lines.Count(l => l.IsPosted),
                    TotalQuantity = x.Lines.Sum(l => l.Quantity),
                    TotalValue = x.Lines.Sum(l => l.TotalValue)
                })
                .ToListAsync();

            return Ok(list);
        }

        // GET: api/GrnEntry/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var header = await _context.GrnHeaders
                .Include(x => x.Supplier)
                .Include(x => x.Lines)
                    .ThenInclude(l => l.Item)
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,
                    x.GrnNumber,
                    x.SupplierId,
                    SupplierName = x.Supplier.SupplierName,
                    x.PoNumber,
                    x.PoDate,
                    x.GrnType,
                    x.SupplierInvoiceNumber,
                    x.SupplierInvoiceDate,
                    x.IsPosted,
                    x.PostedDate,
                    x.PalletNo,
                    x.FifoPalletNo,
                    Lines = x.Lines.Select(l => new
                    {
                        l.Id,
                        l.ItemId,
                        PartNumber = l.Item.ItemNumber,
                        PartName = l.Item.ItemName,
                        l.Uom,
                        l.PalletQuantity,
                        l.Rate,
                        l.Quantity,
                        l.TotalValue,
                        l.IsPosted,
                        l.PostedDate,
                        l.PalletNo,
                        l.FifoPalletNo
                    })
                })
                .FirstOrDefaultAsync();

            if (header == null)
                return NotFound(new { message = "GRN not found" });

            return Ok(header);
        }

        // GET: api/GrnEntry/next-number
        // Returns either the auto-continued next number (if a counter
        // already exists) or an empty string with isManual=true (meaning:
        // no GRN has ever been entered yet, so the frontend should let
        // the user type the starting number themselves — any format).
        [HttpGet("next-number")]
        public async Task<IActionResult> GetNextNumber()
        {
            var counter = await _context.GrnCounters.OrderByDescending(x => x.Id).FirstOrDefaultAsync();

            if (counter == null)
                return Ok(new { grnNumber = "", isManual = true });

            var nextSeq = counter.LastSequence + 1;
            var grnNumber = $"{counter.Prefix}{nextSeq.ToString().PadLeft(counter.PadWidth, '0')}";
            return Ok(new { grnNumber, isManual = false });
        }

        // Splits a manually-typed value like "260001" into a text
        // prefix ("26") and a trailing numeric part ("0001", width 4).
        // No format is enforced beyond "ends in at least one digit".
        private static (string prefix, int number, int padWidth) SplitPrefixAndNumber(string value)
        {
            var match = Regex.Match(value, @"^(.*?)(\d+)$");
            if (!match.Success)
                return (value, 0, 0);

            var digits = match.Groups[2].Value;
            return (match.Groups[1].Value, int.Parse(digits), digits.Length);
        }

        // Resolves the GRN number to actually use for this Create() call:
        //  - If a counter already exists AND the client is NOT overriding
        //    it, ignore whatever the client sent and auto-increment
        //    server-side (avoids races / accidental overrides once the
        //    sequence is live).
        //  - If a counter already exists AND the client IS overriding it
        //    (overrideGrnNo = true, from the "Edit GRN No" checkbox), take
        //    the client's typed value as-is, and reseed the counter's
        //    Prefix/PadWidth/LastSequence from it so every subsequent GRN
        //    auto-continues from this new value.
        //  - If no counter exists yet, this is the very first GRN ever —
        //    require the client's manually-typed GrnNo (any format, as
        //    long as it ends in digits) and seed the counter from it.
        private async Task<(string grnNumber, string error)> ResolveGrnNumberAsync(string clientGrnNo, bool overrideGrnNo)
        {
            var counter = await _context.GrnCounters.OrderByDescending(x => x.Id).FirstOrDefaultAsync();

            if (counter != null && !overrideGrnNo)
            {
                var nextSeq = counter.LastSequence + 1;
                counter.LastSequence = nextSeq;
                await _context.SaveChangesAsync();
                return ($"{counter.Prefix}{nextSeq.ToString().PadLeft(counter.PadWidth, '0')}", null);
            }

            // Either the very first GRN ever, or a mid-sequence override —
            // both paths require a manually-typed value.
            if (string.IsNullOrWhiteSpace(clientGrnNo))
            {
                return (null, counter == null
                    ? "Enter a starting GRN No (e.g. 260001) — this is the first GRN ever entered"
                    : "Enter a GRN No to override the auto-generated value");
            }

            var grnNo = clientGrnNo.Trim();
            var (prefix, number, padWidth) = SplitPrefixAndNumber(grnNo);

            if (padWidth == 0)
                return (null, "GRN No must end with at least one digit (e.g. 260001)");

            if (counter == null)
            {
                // First-ever GRN: create the counter.
                var newCounter = new GrnCounter { Prefix = prefix, PadWidth = padWidth, LastSequence = number };
                _context.GrnCounters.Add(newCounter);
            }
            else
            {
                // Mid-sequence override: reseed the existing counter so
                // future auto-generated numbers continue from this value.
                counter.Prefix = prefix;
                counter.PadWidth = padWidth;
                counter.LastSequence = number;
            }

            await _context.SaveChangesAsync();

            return (grnNo, null);
        }

        // POST: api/GrnEntry
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] GrnCreateRequest req)
        {
            try
            {
                if (req == null || req.SupplierId <= 0 ||
                    string.IsNullOrWhiteSpace(req.PoNumber) ||
                    req.PoDate == default ||
                    string.IsNullOrWhiteSpace(req.GrnType) ||
                    string.IsNullOrWhiteSpace(req.SupplierInvoiceNumber) ||
                    req.SupplierInvoiceDate == default)
                {
                    return BadRequest(new { message = "Please fill all required header fields" });
                }

                if (!ValidGrnTypes.Contains(req.GrnType))
                    return BadRequest(new { message = "GRN Type must be 'Regular' or 'Sample'" });

                if (!InvoiceNumberRegex.IsMatch(req.SupplierInvoiceNumber.Trim()))
                    return BadRequest(new { message = "Supplier Invoice Number must be numbers only" });

                var supplierExists = await _context.SupplierMasters.AnyAsync(x => x.Id == req.SupplierId);
                if (!supplierExists)
                    return BadRequest(new { message = "Selected Supplier does not exist" });

                if (req.Lines == null || req.Lines.Count == 0)
                    return BadRequest(new { message = "Add at least one part before saving" });

                foreach (var line in req.Lines)
                {
                    if (line.ItemId <= 0)
                        return BadRequest(new { message = "Each line needs a valid Part Number" });
                    if (line.Rate <= 0)
                        return BadRequest(new { message = "Rate must be greater than 0" });
                    if (line.Quantity <= 0)
                        return BadRequest(new { message = "Quantity must be greater than 0" });
                    if (line.PalletQuantity.HasValue && line.PalletQuantity.Value > line.Quantity)
                        return BadRequest(new { message = $"Pallet Quantity cannot be greater than Quantity for Part Number (Item Id {line.ItemId})" });

                    var itemExists = await _context.ItemMasters.AnyAsync(x => x.Id == line.ItemId);
                    if (!itemExists)
                        return BadRequest(new { message = $"Part Number (Item Id {line.ItemId}) does not exist" });
                }

                var (grnNumber, grnNoError) = await ResolveGrnNumberAsync(req.GrnNo, req.OverrideGrnNo);
                if (grnNoError != null)
                    return BadRequest(new { message = grnNoError });

                var header = new GrnHeader
                {
                    GrnNumber = grnNumber,
                    SupplierId = req.SupplierId,
                    PoNumber = req.PoNumber.Trim(),
                    PoDate = req.PoDate,
                    GrnType = req.GrnType,
                    SupplierInvoiceNumber = req.SupplierInvoiceNumber.Trim(),
                    SupplierInvoiceDate = req.SupplierInvoiceDate,
                    CreatedDate = DateTime.Now,
                    CreatedBy = req.CreatedBy?.Trim()
                };

                foreach (var line in req.Lines)
                {
                    header.Lines.Add(new GrnLine
                    {
                        ItemId = line.ItemId,
                        Uom = line.Uom?.Trim(),
                        PalletQuantity = line.PalletQuantity,
                        Rate = line.Rate,
                        Quantity = line.Quantity,
                        TotalValue = Math.Round(line.Rate * line.Quantity, 2),
                        CreatedDate = DateTime.Now
                    });
                }

                _context.GrnHeaders.Add(header);
                await _context.SaveChangesAsync();

                return Ok(new { header.Id, header.GrnNumber });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Save failed: {detail}" });
            }
        }

        // PUT: api/GrnEntry/5
        // Replaces header fields + lines wholesale (delete-and-recreate the
        // lines, simplest correct way to handle add/remove/edit in one call).
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] GrnCreateRequest req)
        {
            var header = await _context.GrnHeaders
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (header == null)
                return NotFound(new { message = "GRN not found" });

            if (header.IsPosted)
                return BadRequest(new { message = "A posted GRN cannot be edited" });

            if (header.Lines.Any(x => x.IsPosted))
            {
                return BadRequest(new
                {
                    message = "A GRN with posted items cannot be edited"
                });
            }

            if (req == null || req.SupplierId <= 0 ||
                string.IsNullOrWhiteSpace(req.PoNumber) ||
                req.PoDate == default ||
                string.IsNullOrWhiteSpace(req.GrnType) ||
                string.IsNullOrWhiteSpace(req.SupplierInvoiceNumber) ||
                req.SupplierInvoiceDate == default)
            {
                return BadRequest(new { message = "Please fill all required header fields" });
            }

            if (!ValidGrnTypes.Contains(req.GrnType))
                return BadRequest(new { message = "GRN Type must be 'Regular' or 'Sample'" });

            if (!InvoiceNumberRegex.IsMatch(req.SupplierInvoiceNumber.Trim()))
                return BadRequest(new { message = "Supplier Invoice Number must be numbers only" });

            if (req.Lines == null || req.Lines.Count == 0)
                return BadRequest(new { message = "Add at least one part before saving" });

            foreach (var line in req.Lines)
            {
                if (line.ItemId <= 0)
                    return BadRequest(new { message = "Each line needs a valid Part Number" });
                if (line.Rate <= 0)
                    return BadRequest(new { message = "Rate must be greater than 0" });
                if (line.Quantity <= 0)
                    return BadRequest(new { message = "Quantity must be greater than 0" });
                if (line.PalletQuantity.HasValue && line.PalletQuantity.Value > line.Quantity)
                    return BadRequest(new { message = $"Pallet Quantity cannot be greater than Quantity for Part Number (Item Id {line.ItemId})" });
            }

            header.SupplierId = req.SupplierId;
            header.PoNumber = req.PoNumber.Trim();
            header.PoDate = req.PoDate;
            header.GrnType = req.GrnType;
            header.SupplierInvoiceNumber = req.SupplierInvoiceNumber.Trim();
            header.SupplierInvoiceDate = req.SupplierInvoiceDate;

            _context.GrnLines.RemoveRange(header.Lines);

            foreach (var line in req.Lines)
            {
                header.Lines.Add(new GrnLine
                {
                    ItemId = line.ItemId,
                    Uom = line.Uom?.Trim(),
                    PalletQuantity = line.PalletQuantity,
                    Rate = line.Rate,
                    Quantity = line.Quantity,
                    TotalValue = Math.Round(line.Rate * line.Quantity, 2),
                    CreatedDate = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            return Ok(new { header.Id, header.GrnNumber });
        }
        public class PostGrnLineRequest
        {
            public string? PostedBy { get; set; }
        }

        // ★ NEW: request/response shapes for posting several lines in one call
        // (Bulk GRN Post). LineIds can span a single GRN or, since posting
        // is per-line, even multiple GRNs at once — the frontend currently
        // only ever sends lines from the GRN the user has open.
        public class PostBulkLinesRequest
        {
            public List<int> LineIds { get; set; } = new List<int>();
            public string? PostedBy { get; set; }
        }

        // Internal result for a single line's post attempt, used by both
        // the single-line and bulk-post endpoints so the pallet/FIFO
        // generation logic only lives in one place.
        private class PostLineResult
        {
            public int LineId { get; set; }
            public bool Success { get; set; }
            public string PalletNo { get; set; }
            public string FifoPalletNo { get; set; }
            public string Error { get; set; }
        }

        // Shared worker: posts exactly one GRN line (pallet no + FIFO no +
        // posted flag/date/by), without any HTTP-specific error handling.
        // Both PostLine and PostBulkLines call this so the two endpoints
        // can never drift out of sync with each other.
        private async Task<PostLineResult> PostSingleLineAsync(int lineId, string postedBy)
        {
            var line = await _context.GrnLines
                .Include(l => l.Item)
                .Include(l => l.Header)
                    .ThenInclude(h => h.Supplier)
                .FirstOrDefaultAsync(l => l.Id == lineId);

            if (line == null)
                return new PostLineResult { LineId = lineId, Success = false, Error = "GRN line not found" };

            if (line.IsPosted)
                return new PostLineResult { LineId = lineId, Success = false, Error = "This item is already posted" };

            // ==========================================
            // GENERATE PALLET NUMBER
            // FROM STORE MASTER -> PALLET TYPE
            // ==========================================

            var palletNumber = await GenerateGrnPalletNumberAsync(line.ItemId);

            if (palletNumber == null)
            {
                return new PostLineResult
                {
                    LineId = lineId,
                    Success = false,
                    Error = $"No pallet configuration found in Store Master for Part Number " +
                            $"{line.Item?.ItemNumber ?? line.ItemId.ToString()}."
                };
            }

            // ==========================================
            // POST GRN LINE
            // ==========================================

            line.IsPosted = true;
            line.PostedDate = DateTime.Now;
            line.PostedBy = postedBy?.Trim();

            // Example: GI-01, GI-02 ... GI-70
            line.PalletNo = palletNumber;

            // FIFO number
            line.FifoPalletNo = await GenerateLineFifoPalletNoAsync();

            await _context.SaveChangesAsync();

            return new PostLineResult
            {
                LineId = lineId,
                Success = true,
                PalletNo = line.PalletNo,
                FifoPalletNo = line.FifoPalletNo
            };
        }

        // PUT: api/GrnEntry/line/5/post
        // Marks a single GRN line ("item-wise" posting) as posted and
        // assigns it its own Pallet No / FIFO Pallet No, independent of
        [HttpPut("line/{lineId}/post")]
        public async Task<IActionResult> PostLine(
    int lineId,
    [FromBody] PostGrnLineRequest req)
        {
            try
            {
                var result = await PostSingleLineAsync(lineId, req?.PostedBy);

                if (!result.Success)
                {
                    return result.Error == "GRN line not found"
                        ? NotFound(new { message = result.Error })
                        : BadRequest(new { message = result.Error });
                }

                return Ok(new
                {
                    Id = result.LineId,
                    PalletNo = result.PalletNo,
                    FifoPalletNo = result.FifoPalletNo
                });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;

                return StatusCode(500, new
                {
                    message = $"Post failed: {detail}"
                });
            }
        }

        // PUT: api/GrnEntry/lines/post-bulk
        // ★ NEW: Bulk GRN Post. Posts every line id supplied, one at a
        // time (same pallet/FIFO generation as single-line posting), and
        // reports back which lines succeeded and which failed instead of
        // failing the whole batch on the first error — so, e.g., one line
        // missing a Store Master pallet config doesn't block posting the
        // rest of the selected items.
        [HttpPut("lines/post-bulk")]
        public async Task<IActionResult> PostBulkLines([FromBody] PostBulkLinesRequest req)
        {
            if (req == null || req.LineIds == null || req.LineIds.Count == 0)
                return BadRequest(new { message = "Select at least one item to post" });

            var distinctLineIds = req.LineIds.Distinct().ToList();

            var posted = new List<object>();
            var errors = new List<object>();

            foreach (var lineId in distinctLineIds)
            {
                try
                {
                    var result = await PostSingleLineAsync(lineId, req.PostedBy);

                    if (result.Success)
                    {
                        posted.Add(new
                        {
                            id = result.LineId,
                            palletNo = result.PalletNo,
                            fifoPalletNo = result.FifoPalletNo
                        });
                    }
                    else
                    {
                        errors.Add(new { lineId, message = result.Error });
                    }
                }
                catch (Exception ex)
                {
                    var detail = ex.InnerException?.Message ?? ex.Message;
                    errors.Add(new { lineId, message = $"Post failed: {detail}" });
                }
            }

            return Ok(new { posted, errors });
        }

        private async Task<string> GenerateGrnPalletNumberAsync(int itemId)
        {
            // Find Store Master configuration for this Part Number
            var storeMaster = await _context.StoreMasters
                .FirstOrDefaultAsync(x => x.PartNumberId == itemId);

            if (storeMaster == null)
                return null;

            // Get pallet type
            var palletType = await _context.PalletTypeMasters
                .FirstOrDefaultAsync(x => x.Id == storeMaster.PalletTypeId);

            if (palletType == null)
                return null;

            // ==========================================
            // NEXT SEQUENCE
            // ==========================================

            int nextSequence = palletType.CurrentSequence + 1;

            // ==========================================
            // 70 -> 1
            // ==========================================

            if (nextSequence > palletType.RangeTo)
            {
                nextSequence = palletType.RangeFrom;
            }

            // ==========================================
            // UPDATE CURRENT SEQUENCE
            // ==========================================

            palletType.CurrentSequence = nextSequence;

            // ==========================================
            // PALLET PREFIX
            // ==========================================

            var prefix = palletType.PalletName.Length >= 2
                ? palletType.PalletName.Substring(0, 2).ToUpper()
                : palletType.PalletName.ToUpper();

            // ==========================================
            // GENERATE PALLET NUMBER
            // Example:
            // GI-01
            // GI-02
            // ...
            // GI-70
            // ==========================================

            var palletNumber = $"{prefix}-{nextSequence:D2}";

            return palletNumber;
        }
        // DELETE: api/GrnEntry/line/5
        // Deletes a single GRN line. Blocked once that line is posted —
        // same reasoning as blocking edits on posted data elsewhere.
        [HttpDelete("line/{lineId}")]
        public async Task<IActionResult> DeleteLine(int lineId)
        {
            try
            {
                var line = await _context.GrnLines.FindAsync(lineId);

                if (line == null)
                    return NotFound(new { message = "GRN line not found" });

                if (line.IsPosted)
                    return BadRequest(new { message = "A posted item cannot be deleted" });

                _context.GrnLines.Remove(line);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Deleted Successfully" });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Delete failed: {detail}" });
            }
        }



        private async Task<string> GenerateLineFifoPalletNoAsync()
        {
            // e.g. F25070001 -> F + YY + MM + 4-digit sequence for that month
            var now = DateTime.Now;
            var prefix = $"F{now:yyMM}";

            var last = await _context.GrnLines
                .Where(l => l.FifoPalletNo != null && l.FifoPalletNo.StartsWith(prefix))
                .OrderByDescending(l => l.Id)
                .FirstOrDefaultAsync();

            int nextSeq = 1;

            if (last?.FifoPalletNo != null)
            {
                if (int.TryParse(last.FifoPalletNo.Substring(prefix.Length), out int lastSeq))
                    nextSeq = lastSeq + 1;
            }

            return $"{prefix}{nextSeq:D4}";
        }

        // DELETE: api/GrnEntry/5
        // Deleting cascades (at the DB level) through GRN_LINE ->
        // GRN_PALLET -> STORE_MOVEMENT, so this also removes any pallets
        // and store-stuffing records tied to this GRN. Requires the FK
        // cascade fix in AlterStoreMovement_CascadeDeletes.sql — without
        // it, deleting a posted GRN whose pallets have been stuffed
        // somewhere will fail with a SQL FK conflict.
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var entity = await _context.GrnHeaders.FindAsync(id);

                if (entity == null)
                    return NotFound(new { message = "GRN not found" });

                _context.GrnHeaders.Remove(entity);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Deleted Successfully" });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { message = $"Delete failed: {detail}" });
            }
        }
    }
}
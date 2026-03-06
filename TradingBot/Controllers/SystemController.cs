using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Enums;
using TradingBot.Persistence;
using TradingBot.API.Middleware;

namespace TradingBot.API.Controllers
{
    /// <summary>
    /// System and operational endpoints for the admin dashboard.
    ///
    ///   GET  /api/system/health           — liveness + DB + key component check
    ///   GET  /api/system/logs             — paginated SystemLog entries
    ///   GET  /api/system/logs/errors      — only ERROR level entries
    ///   GET  /api/system/worker-status    — last activity of each background worker
    ///   GET  /api/system/database-stats   — row counts for every table
    /// </summary>
    [ApiController]
    [Route("api/system")]
    [Authorize]
    public class SystemController : ControllerBase
    {
        private readonly TradingBotDbContext _db;
        private readonly ILogger<SystemController> _logger;

        // Track app start time for uptime calculation
        private static readonly DateTime _startTime = DateTime.UtcNow;

        public SystemController(TradingBotDbContext db, ILogger<SystemController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // GET /api/system/health
        [HttpGet("health")]
        public async Task<IActionResult> GetHealth()
        {
            bool dbOk = false;
            string dbStatus = "unknown";

            try
            {
                dbOk = await _db.Database.CanConnectAsync();
                dbStatus = dbOk ? "connected" : "disconnected";
            }
            catch (Exception ex)
            {
                dbStatus = $"error: {ex.Message}";
            }

            // Check recent worker activity via SystemLogs
            var fiveMinAgo = DateTime.UtcNow.AddMinutes(-5);
            var signalWorkerActive = await _db.SystemLogs!
                .AnyAsync(l => l.Message != null
                    && l.Message.Contains("SignalGenerationWorker")
                    && l.CreatedAt >= fiveMinAgo);

            var tradeMonitorActive = await _db.SystemLogs!
                .AnyAsync(l => l.Message != null
                    && (l.Message.Contains("TradeMonitoringWorker")
                        || l.Message.Contains("TP_HIT")
                        || l.Message.Contains("SL_HIT"))
                    && l.CreatedAt >= fiveMinAgo);

            var uptime = DateTime.UtcNow - _startTime;

            var overallStatus = dbOk ? "healthy" : "degraded";

            return Ok(new
            {
                status = overallStatus,
                timestamp = DateTime.UtcNow,
                uptimeSeconds = (int)uptime.TotalSeconds,
                uptimeFormatted = $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s",
                components = new
                {
                    database = new { status = dbStatus },
                    signalWorker = new { status = signalWorkerActive ? "active (last 5 min)" : "idle" },
                    tradeMonitor = new { status = tradeMonitorActive ? "active (last 5 min)" : "idle" }
                }
            });
        }

        // GET /api/system/logs?level=ERROR&search=BTCUSDT&page=1&pageSize=50
        [HttpGet("logs")]
        public async Task<IActionResult> GetLogs(
            [FromQuery] string? level = null,
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            if (pageSize < 1 || pageSize > 200) pageSize = 50;

            var query = _db.SystemLogs!.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(level))
                query = query.Where(l => l.Level == level.ToUpperInvariant());

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(l => l.Message != null
                    && l.Message.Contains(search));

            query = query.OrderByDescending(l => l.CreatedAt);

            var totalCount = await query.CountAsync();
            var logs = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(l => new
                {
                    l.ID,
                    l.Level,
                    l.Message,
                    hasStackTrace = l.StackTrace != null,
                    l.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                totalCount,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                data = logs
            });
        }

        // GET /api/system/logs/{id}  -- full entry including StackTrace
        [HttpGet("logs/{id:int}")]
        public async Task<IActionResult> GetLogEntry(int id)
        {
            var entry = await _db.SystemLogs!.AsNoTracking()
                .FirstOrDefaultAsync(l => l.ID == id);

            if (entry == null)
                return NotFound(new { error = $"Log entry {id} not found." });

            return Ok(entry);
        }

        // GET /api/system/logs/errors  -- latest 100 error-level logs
        [HttpGet("logs/errors")]
        public async Task<IActionResult> GetErrorLogs([FromQuery] int limit = 100)
        {
            if (limit < 1 || limit > 500) limit = 100;

            var errors = await _db.SystemLogs!
                .AsNoTracking()
                .Where(l => l.Level == "ERROR")
                .OrderByDescending(l => l.CreatedAt)
                .Take(limit)
                .ToListAsync();

            return Ok(new { count = errors.Count, data = errors });
        }

        // GET /api/system/worker-status  -- last seen timestamps of background workers
        [HttpGet("worker-status")]
        public async Task<IActionResult> GetWorkerStatus()
        {
            // Look for the most recent log entry from each worker
            var signalLog = await _db.SystemLogs!
                .AsNoTracking()
                .Where(l => l.Message != null && l.Message.Contains("SignalGenerationWorker"))
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefaultAsync();

            var monitorLog = await _db.SystemLogs!
                .AsNoTracking()
                .Where(l => l.Message != null
                         && (l.Message.Contains("TradeMonitoringWorker")
                             || l.Message.Contains("TP_HIT")
                             || l.Message.Contains("SL_HIT")))
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefaultAsync();

            var now = DateTime.UtcNow;

            return Ok(new
            {
                workers = new[]
                {
                    new
                    {
                        name        = "SignalGenerationWorker",
                        description = "Scans market, evaluates strategy, opens trades every 5 min",
                        interval    = "5 minutes",
                        lastSeen    = signalLog?.CreatedAt,
                        secondsSinceLastSeen = signalLog != null
                            ? (int)(now - signalLog.CreatedAt).TotalSeconds
                            : (int?)null,
                        lastMessage = signalLog?.Message,
                        status      = signalLog != null && (now - signalLog.CreatedAt).TotalMinutes < 6
                            ? "running" : "unknown"
                    },
                    new
                    {
                        name        = "TradeMonitoringWorker",
                        description = "Checks open trades for SL/TP hits every 10 seconds",
                        interval    = "10 seconds",
                        lastSeen    = monitorLog?.CreatedAt,
                        secondsSinceLastSeen = monitorLog != null
                            ? (int)(now - monitorLog.CreatedAt).TotalSeconds
                            : (int?)null,
                        lastMessage = monitorLog?.Message,
                        status      = monitorLog != null && (now - monitorLog.CreatedAt).TotalSeconds < 30
                            ? "running" : "unknown"
                    }
                },
                checkedAt = now
            });
        }

        // GET /api/system/database-stats  -- row counts for dashboard "System" page
        [HttpGet("database-stats")]
        public async Task<IActionResult> GetDatabaseStats()
        {
            var stats = new
            {
                trades = await _db.Trades!.CountAsync(),
                openTrades = await _db.Trades!.CountAsync(t => t.Status == TradeStatus.Open),
                closedTrades = await _db.Trades!.CountAsync(t => t.Status == TradeStatus.Closed),
                orders = await _db.Orders!.CountAsync(),
                tradeSignals = await _db.TradeSignals!.CountAsync(),
                indicatorSnapshots = await _db.IndicatorSnapshots!.CountAsync(),
                portfolioSnapshots = await _db.PortfolioSnapshots!.CountAsync(),
                dailyPerformances = await _db.DailyPerformances!.CountAsync(),
                systemLogs = await _db.SystemLogs!.CountAsync(),
                systemErrors = await _db.SystemLogs!.CountAsync(l => l.Level == "ERROR"),
                tradingPairs = await _db.TradingPairs!.CountAsync(),
                activePairs = await _db.TradingPairs!.CountAsync(p => p.IsActive),
                calculatedAt = DateTime.UtcNow
            };

            return Ok(stats);
        }
    }
}


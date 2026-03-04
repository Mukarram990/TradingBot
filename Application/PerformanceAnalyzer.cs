using TradingBot.Domain.Entities;

namespace TradingBot.Application
{
    /// <summary>
    /// Calculates trading performance metrics from closed trades.
    /// Supports configurable date ranges for analysis.
    /// 
    /// Metrics:
    /// - Win Rate: % of winning trades
    /// - Average R:R: Risk-Reward ratio
    /// - Max Drawdown: Peak-to-trough decline
    /// - Profit Factor: Gross wins / Gross losses
    /// - Sharpe Ratio: Risk-adjusted returns
    /// </summary>
    public class PerformanceAnalyzer
    {
        public class PerformanceMetrics
        {
            public int TotalTrades { get; set; }
            public int Wins { get; set; }
            public int Losses { get; set; }
            public decimal WinRate { get; set; }
            public decimal NetPnL { get; set; }
            public decimal AvgPnLPerTrade { get; set; }
            public decimal AvgWinSize { get; set; }
            public decimal AvgLossSize { get; set; }
            public decimal RiskRewardRatio { get; set; }
            public decimal ProfitFactor { get; set; }
            public decimal MaxDrawdown { get; set; }
            public decimal SharpeRatio { get; set; }
            public decimal BestTrade { get; set; }
            public decimal WorstTrade { get; set; }
            public int ConsecutiveWins { get; set; }
            public int ConsecutiveLosses { get; set; }
            public DateTime CalculatedAt { get; set; }
        }

        /// <summary>
        /// Analyzes trades within a date range.
        /// </summary>
        public static PerformanceMetrics Analyze(
            List<Trade> trades,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            // Filter by date range
            var filtered = trades
                .Where(t => t.Status == TradingBot.Domain.Enums.TradeStatus.Closed)
                .AsEnumerable();

            if (fromDate.HasValue)
                filtered = filtered.Where(t => t.EntryTime >= fromDate.Value);
            if (toDate.HasValue)
                filtered = filtered.Where(t => t.EntryTime <= toDate.Value);

            var closedTrades = filtered.OrderBy(t => t.EntryTime).ToList();

            if (closedTrades.Count == 0)
                return new PerformanceMetrics
                {
                    TotalTrades = 0,
                    CalculatedAt = DateTime.UtcNow
                };

            // Basic counts
            var wins = closedTrades.Count(t => t.PnL > 0);
            var losses = closedTrades.Count(t => t.PnL <= 0);
            var winRate = wins / (decimal)closedTrades.Count * 100m;

            // PnL stats
            var netPnL = closedTrades.Sum(t => t.PnL ?? 0m);
            var avgPnL = netPnL / closedTrades.Count;
            var winTrades = closedTrades.Where(t => t.PnL > 0).ToList();
            var lossTrades = closedTrades.Where(t => t.PnL <= 0).ToList();

            var avgWin = winTrades.Count > 0 ? winTrades.Average(t => t.PnL ?? 0m) : 0m;
            var avgLoss = lossTrades.Count > 0 ? lossTrades.Average(t => t.PnL ?? 0m) : 0m;

            // Risk-Reward Ratio
            var riskRewardRatio = avgLoss != 0 ? Math.Abs(avgWin / avgLoss) : 0m;

            // Profit Factor
            var totalWins = winTrades.Sum(t => t.PnL ?? 0m);
            var totalLosses = Math.Abs(lossTrades.Sum(t => t.PnL ?? 0m));
            var profitFactor = totalLosses > 0 ? totalWins / totalLosses : 0m;

            // Max Drawdown
            var maxDrawdown = CalculateMaxDrawdown(closedTrades);

            // Sharpe Ratio (annualized)
            var sharpeRatio = CalculateSharpeRatio(closedTrades);

            // Best/Worst trade
            var bestTrade = closedTrades.Max(t => t.PnL ?? 0m);
            var worstTrade = closedTrades.Min(t => t.PnL ?? 0m);

            // Consecutive streaks
            var (consecWins, consecLosses) = CalculateConsecutiveStreaks(closedTrades);

            return new PerformanceMetrics
            {
                TotalTrades = closedTrades.Count,
                Wins = wins,
                Losses = losses,
                WinRate = Math.Round(winRate, 2),
                NetPnL = Math.Round(netPnL, 4),
                AvgPnLPerTrade = Math.Round(avgPnL, 4),
                AvgWinSize = Math.Round(avgWin, 4),
                AvgLossSize = Math.Round(avgLoss, 4),
                RiskRewardRatio = Math.Round(riskRewardRatio, 2),
                ProfitFactor = Math.Round(profitFactor, 2),
                MaxDrawdown = Math.Round(maxDrawdown, 4),
                SharpeRatio = Math.Round(sharpeRatio, 2),
                BestTrade = Math.Round(bestTrade, 4),
                WorstTrade = Math.Round(worstTrade, 4),
                ConsecutiveWins = consecWins,
                ConsecutiveLosses = consecLosses,
                CalculatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Max Drawdown: Largest cumulative loss from peak
        /// </summary>
        private static decimal CalculateMaxDrawdown(List<Trade> trades)
        {
            if (trades.Count == 0) return 0m;

            decimal maxDrawdown = 0m;
            decimal runningProfit = 0m;
            decimal peak = 0m;

            foreach (var trade in trades)
            {
                runningProfit += trade.PnL ?? 0m;
                if (runningProfit > peak) peak = runningProfit;

                var drawdown = peak - runningProfit;
                if (drawdown > maxDrawdown) maxDrawdown = drawdown;
            }

            return maxDrawdown;
        }

        /// <summary>
        /// Sharpe Ratio: (Return - RiskFreeRate) / StdDev
        /// Annualized assuming 252 trading days/year
        /// Using 2% annual risk-free rate
        /// </summary>
        private static decimal CalculateSharpeRatio(List<Trade> trades)
        {
            if (trades.Count < 2) return 0m;

            const decimal riskFreeRateDaily = 0.00008m; // ~2% annual / 252 days
            var returns = trades.Select(t => t.PnL ?? 0m).ToList();

            var avgReturn = returns.Average();
            var variance = returns.Sum(r => (r - avgReturn) * (r - avgReturn)) / returns.Count;
            var stdDev = (decimal)Math.Sqrt((double)variance);

            if (stdDev == 0) return 0m;

            var dailySharpe = (avgReturn - riskFreeRateDaily) / stdDev;
            var annualizedSharpe = dailySharpe * (decimal)Math.Sqrt(252);

            return annualizedSharpe;
        }

        /// <summary>
        /// Calculate max consecutive wins and losses
        /// </summary>
        private static (int maxWins, int maxLosses) CalculateConsecutiveStreaks(List<Trade> trades)
        {
            int maxWins = 0, maxLosses = 0;
            int curWins = 0, curLosses = 0;

            foreach (var trade in trades)
            {
                if (trade.PnL > 0)
                {
                    curWins++;
                    curLosses = 0;
                    maxWins = Math.Max(maxWins, curWins);
                }
                else
                {
                    curLosses++;
                    curWins = 0;
                    maxLosses = Math.Max(maxLosses, curLosses);
                }
            }

            return (maxWins, maxLosses);
        }
    }
}

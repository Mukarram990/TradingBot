using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence
{
    public class TradingBotDbContext(DbContextOptions<TradingBotDbContext> options) : DbContext(options)
    {
        public DbSet<Trade>? Trades { get; set; }
        public DbSet<Order>? Orders { get; set; }
        public DbSet<TradingPair>? TradingPairs { get; set; }
        public DbSet<IndicatorSnapshot>? IndicatorSnapshots { get; set; }
        public DbSet<TradeSignal>? TradeSignals { get; set; }
        public DbSet<PortfolioSnapshot>? PortfolioSnapshots { get; set; }
        public DbSet<DailyPerformance>? DailyPerformances { get; set; }
        public DbSet<SystemLog>? SystemLogs { get; set; }
        public DbSet<AIResponse>? AIResponses { get; set; }
        public DbSet<RiskProfile>? RiskProfiles { get; set; }
        public DbSet<MarketRegime>? MarketRegimes { get; set; }
        public DbSet<UserAccount>? UserAccounts { get; set; } 

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Indexes
            modelBuilder.Entity<Trade>()
                .HasIndex(t => t.Symbol);

            modelBuilder.Entity<Order>()
                .HasOne(o => o.Trade)
                .WithMany(t => t.Orders)
                .HasForeignKey(o => o.TradeId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TradingPair>()
                .HasIndex(p => p.Symbol)
                .IsUnique();

            modelBuilder.Entity<IndicatorSnapshot>()
                .HasIndex(i => new { i.Symbol, i.Timestamp });

            modelBuilder.Entity<TradeSignal>()
                .HasIndex(s => new { s.Symbol, s.CreatedAt });

            modelBuilder.Entity<DailyPerformance>()
                .HasIndex(d => d.Date)
                .IsUnique();

            // Set decimal precision globally (VERY IMPORTANT for crypto)
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties())
                {
                    if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                    {
                        property.SetPrecision(18);
                        property.SetScale(8);
                    }
                }
            }
        }
    }
}

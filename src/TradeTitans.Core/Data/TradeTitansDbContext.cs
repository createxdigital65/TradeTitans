using Microsoft.EntityFrameworkCore;
using TradeTitans.Core.Domain.Entities;

namespace TradeTitans.Core.Data;

public class TradeTitansDbContext : DbContext
{
    public TradeTitansDbContext(DbContextOptions<TradeTitansDbContext> options)
        : base(options)
    {
    }

    public DbSet<TradeCouncilSession> TradeCouncilSessions => Set<TradeCouncilSession>();
    public DbSet<AgentProposal> AgentProposals => Set<AgentProposal>();
    public DbSet<RiskCheckLog> RiskLogs => Set<RiskCheckLog>();
    public DbSet<ExecutedOrder> ExecutedOrders => Set<ExecutedOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TradeCouncilSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Symbol);
            entity.HasIndex(e => e.Timestamp);
        });

        modelBuilder.Entity<AgentProposal>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Session)
                  .WithMany(s => s.AgentProposals)
                  .HasForeignKey(e => e.TradeCouncilSessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RiskCheckLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Session)
                  .WithMany(s => s.RiskLogs)
                  .HasForeignKey(e => e.TradeCouncilSessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExecutedOrder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Session)
                  .WithOne(s => s.ExecutedOrder)
                  .HasForeignKey<ExecutedOrder>(e => e.TradeCouncilSessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

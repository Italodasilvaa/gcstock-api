using GCStock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GCStock.Infra.Database;

public class GCStockDbContext(DbContextOptions<GCStockDbContext> options) : DbContext(options)
{
    public DbSet<Administrator> Administrators => Set<Administrator>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Kit> Kits => Set<Kit>();
    public DbSet<KitComponent> KitComponents => Set<KitComponent>();

    public DbSet<StockBatch> StockBatches => Set<StockBatch>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockLoss> StockLosses => Set<StockLoss>();
    public DbSet<StockLossLot> StockLossLots => Set<StockLossLot>();

    public DbSet<PurchaseEntry> PurchaseEntries => Set<PurchaseEntry>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();

    public DbSet<AccountsPayable> AccountsPayables => Set<AccountsPayable>();
    public DbSet<CapitalContribution> CapitalContributions => Set<CapitalContribution>();
    public DbSet<AccountsReceivable> AccountsReceivables => Set<AccountsReceivable>();
    public DbSet<PaymentFeeSetting> PaymentFeeSettings => Set<PaymentFeeSetting>();
    public DbSet<CardSettlement> CardSettlements => Set<CardSettlement>();
    public DbSet<AlertSetting> AlertSettings => Set<AlertSetting>();

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<SaleRecord> SaleRecords => Set<SaleRecord>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<SaleItemLot> SaleItemLots => Set<SaleItemLot>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // O PostgreSQL (timestamptz) só aceita DateTime com Kind=Utc. Datas vindas do front sem fuso
        // (Kind=Unspecified) são tratadas como UTC; as locais são convertidas.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        const string moneyType = "decimal(18,2)";
        const string qtyType = "decimal(18,3)";
        const string percentType = "decimal(9,4)";

        modelBuilder.Entity<Administrator>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<Company>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Owner).WithMany(a => a.Companies).HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.CorporateName).HasMaxLength(250).IsRequired();
            e.Property(x => x.TradeName).HasMaxLength(250).IsRequired();
        });

        modelBuilder.Entity<Employee>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.Salary).HasColumnType(moneyType);
            e.Property(x => x.Permissions).HasColumnType("text[]").HasDefaultValueSql("'{}'");
        });

        modelBuilder.Entity<Branch>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.Stock).HasColumnType(qtyType);
            e.Property(x => x.MinStock).HasColumnType(qtyType);
            e.Property(x => x.CostPrice).HasColumnType(moneyType);
            e.Property(x => x.SellingPrice).HasColumnType(moneyType);
        });

        modelBuilder.Entity<ProductCategory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Icon).HasMaxLength(60);
            e.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<Kit>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CompanyId);
            e.HasMany(x => x.Components).WithOne(c => c.Kit).HasForeignKey(c => c.KitId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.SuggestedPrice).HasColumnType(moneyType);
            e.Property(x => x.CostPrice).HasColumnType(moneyType);
            e.Property(x => x.MarginPercent).HasColumnType(percentType);
            e.Property(x => x.TotalWeightKg).HasColumnType(qtyType);
            e.Property(x => x.StockAvailable).HasColumnType(qtyType);
        });

        modelBuilder.Entity<KitComponent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Quantity).HasColumnType(qtyType);
            e.Property(x => x.UnitCost).HasColumnType(moneyType);
            e.Property(x => x.Subtotal).HasColumnType(moneyType);
            e.Property(x => x.AvailableStock).HasColumnType(qtyType);
        });

        modelBuilder.Entity<StockBatch>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.AvailableKg).HasColumnType(qtyType);
            e.Property(x => x.UnitCost).HasColumnType(moneyType);
        });

        modelBuilder.Entity<StockMovement>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.AmountKg).HasColumnType(qtyType);
        });

        modelBuilder.Entity<CapitalContribution>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.CompanyId, x.Date });
            e.Property(x => x.Amount).HasColumnType(moneyType);
        });

        modelBuilder.Entity<StockLoss>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.CompanyId, x.Date });
            e.Property(x => x.Qty).HasColumnType(qtyType);
            e.Property(x => x.UnitCost).HasColumnType(moneyType);
            e.Property(x => x.TotalCost).HasColumnType(moneyType);
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("ativa");
        });

        modelBuilder.Entity<StockLossLot>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.StockLoss).WithMany(l => l.Lots).HasForeignKey(x => x.StockLossId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.StockLossId);
            e.Property(x => x.Qty).HasColumnType(qtyType);
        });

        modelBuilder.Entity<PurchaseEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CompanyId);
            e.HasMany(x => x.Items).WithOne(i => i.PurchaseEntry).HasForeignKey(i => i.PurchaseEntryId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.TotalWeightKg).HasColumnType(qtyType);
            e.Property(x => x.TotalAmount).HasColumnType(moneyType);
            e.Property(x => x.ShippingCost).HasColumnType(moneyType);
        });

        modelBuilder.Entity<PurchaseItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Quantity).HasColumnType(qtyType);
            e.Property(x => x.UnitCost).HasColumnType(moneyType);
            e.Property(x => x.Subtotal).HasColumnType(moneyType);
        });

        modelBuilder.Entity<AccountsPayable>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.Amount).HasColumnType(moneyType);
        });

        modelBuilder.Entity<AccountsReceivable>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.GrossAmount).HasColumnType(moneyType);
            e.Property(x => x.FeeRetention).HasColumnType(moneyType);
            e.Property(x => x.NetAmount).HasColumnType(moneyType);
        });

        modelBuilder.Entity<PaymentFeeSetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.FeePercent).HasColumnType(percentType);
            e.Property(x => x.FixedFee).HasColumnType(moneyType);
        });

        modelBuilder.Entity<CardSettlement>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.GrossAmount).HasColumnType(moneyType);
            e.Property(x => x.MdrFeeAmount).HasColumnType(moneyType);
            e.Property(x => x.NetAmount).HasColumnType(moneyType);
            e.Property(x => x.AnticipatedFee).HasColumnType(moneyType);
        });

        modelBuilder.Entity<AlertSetting>(e =>
        {
            e.HasKey(x => x.CompanyId);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Recipients)
                .HasConversion(
                    v => string.Join('|', v),
                    v => v.Length == 0 ? new List<string>() : v.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    new ValueComparer<List<string>>(
                        (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
                        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                        v => v.ToList()))
                .HasColumnType("text");
        });

        modelBuilder.Entity<Client>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.CreditLimit).HasColumnType(moneyType);
            e.Property(x => x.TotalSpent).HasColumnType(moneyType);
        });

        modelBuilder.Entity<SaleRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.Total).HasColumnType(moneyType);
            e.Property(x => x.NetAmount).HasColumnType(moneyType);
            e.Property(x => x.FeeAmount).HasColumnType(moneyType);
            e.Property(x => x.DiscountAmount).HasColumnType(moneyType);
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("concluida");
            e.HasIndex(x => x.SaleId);
        });

        modelBuilder.Entity<SaleItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.SaleRecord).WithMany(r => r.Items).HasForeignKey(x => x.SaleRecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.SaleRecordId);
            e.Property(x => x.Qty).HasColumnType(qtyType);
            e.Property(x => x.UnitPrice).HasColumnType(moneyType);
            e.Property(x => x.Subtotal).HasColumnType(moneyType);
            e.Property(x => x.UnitCost).HasColumnType(moneyType);
            e.Property(x => x.TotalCost).HasColumnType(moneyType);
        });

        modelBuilder.Entity<SaleItemLot>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.SaleItem).WithMany(i => i.Lots).HasForeignKey(x => x.SaleItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.SaleItemId);
            e.Property(x => x.Qty).HasColumnType(qtyType);
        });

        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CompanyId);
        });
    }
}

internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

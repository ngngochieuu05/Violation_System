using Microsoft.EntityFrameworkCore;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

public class ViolationDbContext : DbContext
{
    public ViolationDbContext(DbContextOptions<ViolationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<ModelSetting> ModelSettings { get; set; } = null!;
    public DbSet<ViolationRecord> ViolationRecords { get; set; } = null!;
    public DbSet<AiModel> AiModels { get; set; } = null!;
    public DbSet<UserFaceEmbedding> UserFaceEmbeddings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
        });

        modelBuilder.Entity<ModelSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<ViolationRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<AiModel>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<UserFaceEmbedding>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

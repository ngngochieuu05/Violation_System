using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Manager;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Security;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

public class ViolationDbContext : DbContext
{
    private readonly IConfiguration? _configuration;

    public ViolationDbContext(DbContextOptions<ViolationDbContext> options, IConfiguration? configuration = null)
        : base(options)
    {
        _configuration = configuration;
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<ModelSetting> ModelSettings { get; set; } = null!;
    public DbSet<ViolationRecord> ViolationRecords { get; set; } = null!;
    public DbSet<AiModel> AiModels { get; set; } = null!;
    public DbSet<UserFaceEmbedding> UserFaceEmbeddings { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<WorkSession> WorkSessions { get; set; } = null!;
    public DbSet<ApprovalRequest> ApprovalRequests { get; set; } = null!;
    public DbSet<FormTemplate> FormTemplates { get; set; } = null!;
    public DbSet<EmployeeMessage> EmployeeMessages { get; set; } = null!;
    public DbSet<EmployeeTask> EmployeeTasks { get; set; } = null!;
    public DbSet<PayrollRecord> PayrollRecords { get; set; } = null!;
    public DbSet<EmployeePreference> EmployeePreferences { get; set; } = null!;
    public DbSet<KnowledgeBaseItem> KnowledgeBaseItems { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var encryptionKey = _configuration?["Security:EncryptionKey"] ?? "ma_khoa_bao_mat_32_ky_tu_cho_aes_1234";

        var encryptionConverter = new ValueConverter<string, string>(
            v => EncryptionHelper.Encrypt(v, encryptionKey),
            v => EncryptionHelper.Decrypt(v, encryptionKey)
        );

        modelBuilder.Entity<EmployeeMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).HasConversion(encryptionConverter);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

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

        modelBuilder.Entity<EmployeePreference>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
        });

        modelBuilder.Entity<KnowledgeBaseItem>(entity =>
        {
            entity.HasKey(e => e.Id);
        });
    }
}

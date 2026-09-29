using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NhatVuong.Domain.Entities;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Adapters.Persistence;

public sealed class NvcDbContext(DbContextOptions<NvcDbContext> options) : DbContext(options)
{
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceState> DeviceStates => Set<DeviceState>();
    public DbSet<TimetableEntry> TimetableEntries => Set<TimetableEntry>();
    public DbSet<AccessGrant> AccessGrants => Set<AccessGrant>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RuntimeSession> RuntimeSessions => Set<RuntimeSession>();
    public DbSet<PreCoolSchedule> PreCoolSchedules => Set<PreCoolSchedule>();
    public DbSet<PolicySettings> Policies => Set<PolicySettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        if (Database.IsSqlite())
        {
            // SQLite has no native DateTimeOffset; a sortable binary encoding keeps comparisons and ORDER BY in SQL.
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Building>(e =>
        {
            e.HasIndex(b => b.Code).IsUnique();
            e.Property(b => b.Code).HasMaxLength(32);
            e.Property(b => b.Name).HasMaxLength(200);
            e.HasMany(b => b.Rooms).WithOne(r => r.Building).HasForeignKey(r => r.BuildingId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Room>(e =>
        {
            e.HasIndex(r => r.Code).IsUnique();
            e.Property(r => r.Code).HasMaxLength(32);
            e.Property(r => r.Name).HasMaxLength(200);
            e.Navigation(r => r.Building).AutoInclude();
            // US-21-2: a room with devices cannot be deleted.
            e.HasMany(r => r.Devices).WithOne(d => d.Room).HasForeignKey(d => d.RoomId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(256);
            e.Property(u => u.FullName).HasMaxLength(200);
            e.Property(u => u.PasswordHash).HasMaxLength(100);
        });

        model.Entity<Device>(e =>
        {
            e.HasIndex(d => d.HardwareId).IsUnique();
            e.Property(d => d.HardwareId).HasMaxLength(64);
            e.Property(d => d.Name).HasMaxLength(200);
            e.Property(d => d.MqttPasswordHash).HasMaxLength(100);
            e.HasOne(d => d.State).WithOne().HasForeignKey<DeviceState>(s => s.DeviceId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(d => d.State).AutoInclude();
            e.Navigation(d => d.Room).AutoInclude();
        });

        model.Entity<DeviceState>(e =>
        {
            e.HasKey(s => s.DeviceId);
            e.Property(s => s.ActiveErrorCode).HasMaxLength(64);
            e.Property(s => s.LanEndpoint).HasMaxLength(256);
            e.Property(s => s.LanCertThumbprint).HasMaxLength(128);
        });

        model.Entity<TimetableEntry>(e =>
        {
            e.HasIndex(t => new { t.RoomId, t.StartsAt });
            e.HasIndex(t => new { t.LecturerId, t.StartsAt });
            e.HasIndex(t => t.EndsAt);
            e.Property(t => t.ExternalId).HasMaxLength(64);
            e.HasOne(t => t.Room).WithMany().HasForeignKey(t => t.RoomId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Lecturer).WithMany().HasForeignKey(t => t.LecturerId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<AccessGrant>(e =>
        {
            e.HasIndex(g => new { g.SubjectUserId, g.RoomId, g.ValidTo });
            e.HasIndex(g => g.TimetableEntryId);
            e.Property(g => g.Note).HasMaxLength(500);
            e.Property(g => g.RevokedReason).HasMaxLength(64);
            e.HasOne(g => g.Subject).WithMany().HasForeignKey(g => g.SubjectUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(g => g.Room).WithMany().HasForeignKey(g => g.RoomId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<AuditEntry>(e =>
        {
            // Unique command id doubles as the idempotency key for replayed device facts (AD-5, AD-9).
            e.HasIndex(a => a.CommandId).IsUnique();
            e.HasIndex(a => new { a.DeviceId, a.RequestedAt });
            e.HasIndex(a => a.RequestedAt);
            e.Property(a => a.ActorName).HasMaxLength(200);
            e.Property(a => a.DeviceName).HasMaxLength(200);
            e.Property(a => a.Value).HasMaxLength(64);
            e.Property(a => a.Detail).HasMaxLength(500);
        });

        model.Entity<Incident>(e =>
        {
            e.HasIndex(i => new { i.DeviceId, i.Kind, i.Code, i.Status });
            e.HasIndex(i => i.LastOccurredAt);
            e.Property(i => i.Code).HasMaxLength(64);
            e.Property(i => i.Message).HasMaxLength(1000);
            e.Property(i => i.ResolvedByName).HasMaxLength(200);
            e.Property(i => i.ResolutionNote).HasMaxLength(2000);
            e.HasOne(i => i.Device).WithMany().HasForeignKey(i => i.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Notification>(e =>
        {
            e.HasIndex(n => new { n.RecipientUserId, n.CreatedAt });
            e.Property(n => n.Category).HasMaxLength(64);
            e.Property(n => n.Title).HasMaxLength(200);
            e.Property(n => n.Body).HasMaxLength(1000);
        });

        model.Entity<RuntimeSession>(e =>
        {
            e.HasIndex(s => new { s.DeviceId, s.EndedAt });
            e.HasIndex(s => s.StartedAt);
        });

        model.Entity<PreCoolSchedule>(e =>
        {
            e.HasIndex(p => new { p.Status, p.DueAt });
            e.HasIndex(p => p.TimetableEntryId);
            e.Property(p => p.Outcome).HasMaxLength(500);
        });

        model.Entity<PolicySettings>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.CampusTimeZone).HasMaxLength(64);
            e.Ignore(p => p.ControlMargin);
            e.Ignore(p => p.AutoOffIdle);
            e.Ignore(p => p.StateFreshness);
            e.Ignore(p => p.CommandTimeout);
            e.Ignore(p => p.ScheduleCatchUp);
        });
    }
}

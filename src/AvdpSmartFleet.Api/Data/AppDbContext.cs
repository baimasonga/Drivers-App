using AvdpSmartFleet.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<GpsUnit> GpsUnits => Set<GpsUnit>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Geofence> Geofences => Set<Geofence>();
    public DbSet<TravelRequest> TravelRequests => Set<TravelRequest>();
    public DbSet<TripLog> TripLogs => Set<TripLog>();
    public DbSet<GpsPosition> GpsPositions => Set<GpsPosition>();
    public DbSet<GateClearance> GateClearances => Set<GateClearance>();
    public DbSet<TripException> TripExceptions => Set<TripException>();
    public DbSet<UnauthorizedMovement> UnauthorizedMovements => Set<UnauthorizedMovement>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RoadAlert> RoadAlerts => Set<RoadAlert>();
    public DbSet<RoadAlertConfirmation> RoadAlertConfirmations => Set<RoadAlertConfirmation>();
    public DbSet<ServiceRecord> ServiceRecords => Set<ServiceRecord>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();
        b.Entity<Vehicle>().HasIndex(v => v.PlateNumber).IsUnique();
        b.Entity<GpsUnit>().HasIndex(g => g.ExternalUnitId).IsUnique();
        b.Entity<TravelRequest>().HasIndex(t => t.RequestCode).IsUnique();

        b.Entity<TravelRequest>()
            .HasOne(t => t.Requester).WithMany().HasForeignKey(t => t.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<TravelRequest>()
            .HasOne(t => t.AssignedDriver).WithMany().HasForeignKey(t => t.AssignedDriverId)
            .OnDelete(DeleteBehavior.SetNull);
        b.Entity<TravelRequest>()
            .HasOne(t => t.AssignedVehicle).WithMany().HasForeignKey(t => t.AssignedVehicleId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<Driver>().HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId);
    }
}

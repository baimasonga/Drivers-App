using AvdpSmartFleet.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        if (await db.Users.AnyAsync()) return;

        string Hash(string p) => BCrypt.Net.BCrypt.HashPassword(p);

        var admin = new User { FullName = "System Admin", Email = "admin@avdp.sl", PasswordHash = Hash("admin123"), Role = UserRole.Admin, Department = "ICT" };
        var fleet = new User { FullName = "Fatmata Bangura", Email = "fleet@avdp.sl", PasswordHash = Hash("fleet123"), Role = UserRole.FleetOfficer, Department = "Admin" };
        var manager = new User { FullName = "Sahr Konteh", Email = "manager@avdp.sl", PasswordHash = Hash("manager123"), Role = UserRole.Manager, Department = "Operations" };
        var gate = new User { FullName = "Ibrahim Sesay", Email = "gate@avdp.sl", PasswordHash = Hash("gate123"), Role = UserRole.GateOfficer, Department = "Security" };
        var requester = new User { FullName = "Aminata Kallon", Email = "requester@avdp.sl", PasswordHash = Hash("user123"), Role = UserRole.Requester, Department = "M&E" };
        var driverUser = new User { FullName = "Mohamed Kamara", Email = "driver@avdp.sl", Phone = "+23276123456", PasswordHash = Hash("driver123"), Role = UserRole.Driver, Department = "Fleet" };
        var auditor = new User { FullName = "Aisha Turay", Email = "auditor@avdp.sl", PasswordHash = Hash("audit123"), Role = UserRole.Auditor, Department = "Internal Audit" };
        db.Users.AddRange(admin, fleet, manager, gate, requester, driverUser, auditor);
        await db.SaveChangesAsync();

        var driver = new Driver { UserId = driverUser.Id, LicenseNumber = "SL-DL-00123", LicenseExpiry = DateTime.UtcNow.AddYears(2) };
        db.Drivers.Add(driver);

        var unit = new GpsUnit { ExternalUnitId = "GT-AVDP-005", Imei = "356789012345678", IsOnline = true, LastSeenAt = DateTime.UtcNow };
        db.GpsUnits.Add(unit);
        await db.SaveChangesAsync();

        var vehicle = new Vehicle
        {
            PlateNumber = "AVDP-005",
            Make = "Toyota",
            Model = "Hilux",
            Type = "Pickup",
            HomeBase = "Freetown HQ",
            CurrentOdometer = 45230,
            GpsUnitId = unit.Id,
            Status = VehicleStatus.Available
        };
        var vehicle2 = new Vehicle
        {
            PlateNumber = "AVDP-008",
            Make = "Toyota",
            Model = "Land Cruiser",
            Type = "SUV",
            HomeBase = "Freetown HQ",
            CurrentOdometer = 88900,
            Status = VehicleStatus.Available
        };
        db.Vehicles.AddRange(vehicle, vehicle2);

        db.Geofences.AddRange(
            new Geofence { Name = "AVDP HQ Freetown", Category = "Office", CenterLat = 8.4844, CenterLng = -13.2344, RadiusMeters = 200 },
            new Geofence { Name = "Kambia District Office", Category = "Office", CenterLat = 9.1267, CenterLng = -12.9181, RadiusMeters = 300 },
            new Geofence { Name = "Bo Field Office", Category = "Office", CenterLat = 7.9626, CenterLng = -11.7383, RadiusMeters = 300 },
            new Geofence { Name = "Kenema Warehouse", Category = "Warehouse", CenterLat = 7.8767, CenterLng = -11.1875, RadiusMeters = 250 },
            new Geofence { Name = "Port Loko Fuel Station", Category = "FuelStation", CenterLat = 8.7667, CenterLng = -12.7833, RadiusMeters = 100 }
        );
        await db.SaveChangesAsync();
    }
}

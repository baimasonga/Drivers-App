using System.ComponentModel.DataAnnotations;

namespace AvdpSmartFleet.Api.Domain;

public class User
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string FullName { get; set; } = "";
    [Required, MaxLength(120)] public string Email { get; set; } = "";
    [MaxLength(20)] public string? Phone { get; set; }
    [Required] public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    [MaxLength(80)] public string? Department { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    [MaxLength(120)] public string? DeviceId { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
}

public class Vehicle
{
    public int Id { get; set; }
    [Required, MaxLength(20)] public string PlateNumber { get; set; } = "";
    [MaxLength(60)] public string Make { get; set; } = "";
    [MaxLength(60)] public string Model { get; set; } = "";
    [MaxLength(40)] public string Type { get; set; } = "Pickup";
    public int? CurrentOdometer { get; set; }
    public VehicleStatus Status { get; set; } = VehicleStatus.Available;
    [MaxLength(80)] public string? HomeBase { get; set; }
    public int? GpsUnitId { get; set; }
    public GpsUnit? GpsUnit { get; set; }
    /// <summary>
    /// Grams of CO2 emitted per kilometre. Defaults to 250 g/km (Toyota Hilux diesel).
    /// SUVs: ~200 g/km, motorbikes: ~80 g/km, trucks: ~600 g/km.
    /// </summary>
    public double Co2GramsPerKm { get; set; } = 250;

    // Maintenance config
    public int ServiceIntervalKm { get; set; } = 5000;
    public int? LastServiceOdometer { get; set; }
    public DateTime? LastServiceAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ServiceRecord
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    public ServiceType Type { get; set; }
    public int? OdometerAt { get; set; }
    public DateTime PerformedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(500)] public string? Notes { get; set; }
    public decimal? CostSll { get; set; }
    [MaxLength(120)] public string? ServicedBy { get; set; }
    public int RecordedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class GpsUnit
{
    public int Id { get; set; }
    [Required, MaxLength(60)] public string ExternalUnitId { get; set; } = "";
    [MaxLength(40)] public string? Imei { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public bool IsOnline { get; set; }
}

public class Driver
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    [MaxLength(40)] public string? LicenseNumber { get; set; }
    public DateTime? LicenseExpiry { get; set; }
    public bool IsAvailable { get; set; } = true;
}

public class Geofence
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(80)] public string? Category { get; set; } // Office, Warehouse, ProjectSite, FuelStation
    public double CenterLat { get; set; }
    public double CenterLng { get; set; }
    public int RadiusMeters { get; set; } = 300;
    public bool IsProvisional { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TravelRequest
{
    public int Id { get; set; }
    [Required, MaxLength(40)] public string RequestCode { get; set; } = "";
    public int RequesterId { get; set; }
    public User Requester { get; set; } = null!;
    [MaxLength(80)] public string? Department { get; set; }
    [Required, MaxLength(500)] public string Purpose { get; set; } = "";
    [MaxLength(40)] public string? RequestedVehicleType { get; set; }
    [MaxLength(80)] public string? DestinationDistrict { get; set; }
    [MaxLength(200)] public string? SpecificDestination { get; set; }
    public int? DestinationGeofenceId { get; set; }
    public Geofence? DestinationGeofence { get; set; }
    public double? DestinationLat { get; set; }
    public double? DestinationLng { get; set; }
    public DateTime PlannedDeparture { get; set; }
    public DateTime PlannedReturn { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;
    public string? PassengerList { get; set; } // JSON
    public TripStatus Status { get; set; } = TripStatus.Submitted;
    public int? AssignedVehicleId { get; set; }
    public Vehicle? AssignedVehicle { get; set; }
    public int? AssignedDriverId { get; set; }
    public Driver? AssignedDriver { get; set; }
    public int? ApprovedById { get; set; }
    public DateTime? ApprovedAt { get; set; }
    [MaxLength(500)] public string? RejectionReason { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? StartOdometer { get; set; }
    public int? EndOdometer { get; set; }
    public double? ComplianceScore { get; set; }
    public double? Co2Kg { get; set; }
    public double? DistanceKm { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<TripLog> TripLogs { get; set; } = new();
    public List<GateClearance> GateClearances { get; set; } = new();
    public List<TripException> Exceptions { get; set; } = new();
}

public class TripLog
{
    public int Id { get; set; }
    public int TravelRequestId { get; set; }
    public TravelRequest TravelRequest { get; set; } = null!;
    [Required, MaxLength(60)] public string EventType { get; set; } = ""; // Departed, Arrived, Stopped, Fuel, Issue, Diversion, Returned
    public DateTime EventAt { get; set; } = DateTime.UtcNow;
    public double? PhoneLat { get; set; }
    public double? PhoneLng { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    [MaxLength(500)] public string? PhotoUrl { get; set; }
    public int? OdometerReading { get; set; }
    public int LoggedByUserId { get; set; }
    [MaxLength(120)] public string? DeviceId { get; set; }
    [MaxLength(80)] public string? LocalUuid { get; set; }
    public bool SyncedOffline { get; set; }
}

public class GpsPosition
{
    public int Id { get; set; }
    public int GpsUnitId { get; set; }
    public GpsUnit GpsUnit { get; set; } = null!;
    public int? TravelRequestId { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double? SpeedKph { get; set; }
    public double? Heading { get; set; }
    public bool? IgnitionOn { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

public class GateClearance
{
    public int Id { get; set; }
    public int TravelRequestId { get; set; }
    public TravelRequest TravelRequest { get; set; } = null!;
    public GateAction Action { get; set; }
    public int GateOfficerId { get; set; }
    public DateTime EventAt { get; set; } = DateTime.UtcNow;
    public int? OdometerReading { get; set; }
    public double? GateLat { get; set; }
    public double? GateLng { get; set; }
    [MaxLength(500)] public string? PhotoUrl { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public class TripException
{
    public int Id { get; set; }
    public int TravelRequestId { get; set; }
    public TravelRequest TravelRequest { get; set; } = null!;
    public ExceptionType Type { get; set; }
    public ExceptionStatus Status { get; set; } = ExceptionStatus.Open;
    [MaxLength(1000)] public string Description { get; set; } = "";
    [MaxLength(1000)] public string? DriverExplanation { get; set; }
    [MaxLength(1000)] public string? OfficerNote { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedById { get; set; }
}

public class UnauthorizedMovement
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public bool Resolved { get; set; }
    [MaxLength(1000)] public string? Resolution { get; set; }
}

public class ChatMessage
{
    public int Id { get; set; }
    [Required, MaxLength(40)] public string Room { get; set; } = "";
    public int SenderUserId { get; set; }
    [MaxLength(120)] public string SenderName { get; set; } = "";
    [MaxLength(40)] public string SenderRole { get; set; } = "";
    [Required, MaxLength(2000)] public string Body { get; set; } = "";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public class RoadAlert
{
    public int Id { get; set; }
    public RoadAlertType Type { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.Medium;
    public double Lat { get; set; }
    public double Lng { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    [MaxLength(500)] public string? PhotoUrl { get; set; }
    public int ReportedByUserId { get; set; }
    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    public int ConfirmationsStillThere { get; set; }
    public int ConfirmationsCleared { get; set; }
    public bool IsActive { get; set; } = true;
    public List<RoadAlertConfirmation> Confirmations { get; set; } = new();
}

public class RoadAlertConfirmation
{
    public int Id { get; set; }
    public int RoadAlertId { get; set; }
    public RoadAlert RoadAlert { get; set; } = null!;
    public int UserId { get; set; }
    public AlertConfirmation Vote { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public double? Lat { get; set; }
    public double? Lng { get; set; }
}

public class AuditLog
{
    public int Id { get; set; }
    public int? ActorUserId { get; set; }
    [MaxLength(60)] public string Action { get; set; } = "";
    [MaxLength(60)] public string? TargetType { get; set; }
    public int? TargetId { get; set; }
    [MaxLength(2000)] public string? Details { get; set; }
    [MaxLength(60)] public string? IpAddress { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

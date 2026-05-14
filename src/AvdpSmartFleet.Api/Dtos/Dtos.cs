using AvdpSmartFleet.Api.Domain;

namespace AvdpSmartFleet.Api.Dtos;

public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token, int UserId, string FullName, string Role);

public record RegisterRequest(string FullName, string Email, string Password, UserRole Role, string? Department, string? Phone);

public record CreateTravelRequest(
    string Purpose,
    string? RequestedVehicleType,
    string? DestinationDistrict,
    string? SpecificDestination,
    int? DestinationGeofenceId,
    double? DestinationLat,
    double? DestinationLng,
    DateTime PlannedDeparture,
    DateTime PlannedReturn,
    Priority Priority,
    string? PassengerList);

public record AssignVehicleDriverRequest(int VehicleId, int DriverId);
public record ApproveRequest(bool Approve, string? RejectionReason);

public record GateClearanceRequest(
    int TravelRequestId,
    GateAction Action,
    int? OdometerReading,
    double? GateLat,
    double? GateLng,
    string? Notes);

public record TripLogRequest(
    int TravelRequestId,
    string EventType,
    double? PhoneLat,
    double? PhoneLng,
    string? Notes,
    int? OdometerReading,
    string? LocalUuid,
    DateTime? EventAt,
    string? PhotoUrl = null);

public record CreateGeofenceRequest(string Name, string? Category, double CenterLat, double CenterLng, int RadiusMeters);
public record CreateVehicleRequest(string PlateNumber, string Make, string Model, string Type, string? HomeBase, int? GpsUnitId);
public record CreateGpsUnitRequest(string ExternalUnitId, string? Imei);

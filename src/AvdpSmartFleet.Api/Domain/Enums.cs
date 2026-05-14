namespace AvdpSmartFleet.Api.Domain;

public enum UserRole
{
    Requester = 1,
    Driver = 2,
    FleetOfficer = 3,
    Manager = 4,
    GateOfficer = 5,
    Auditor = 6,
    Admin = 7
}

public enum TripStatus
{
    Draft = 0,
    Submitted = 1,
    PendingFleetReview = 2,
    PendingApproval = 3,
    ApprovedForDispatch = 4,
    ReadyForGateClearance = 5,
    TripStarted = 6,
    InTransit = 7,
    ArrivedAtDestination = 8,
    Returning = 9,
    ReturnedToBase = 10,
    PendingVerification = 11,
    Verified = 12,
    Queried = 13,
    Escalated = 14,
    Rejected = 15,
    Cancelled = 16,
    Aborted = 17,
    Closed = 18
}

public enum Priority { Normal = 0, Urgent = 1, Emergency = 2 }

public enum ExceptionType
{
    RouteDeviation,
    DestinationMismatch,
    UnauthorizedStop,
    TimeViolation,
    GpsAppMismatch,
    UnapprovedMovement,
    OdometerMismatch,
    LateReturn,
    TrackerOffline,
    DriverSeparation,
    TamperSuspicion
}

public enum ExceptionStatus { Open, UnderReview, Resolved, Escalated, Appealed }

public enum GateAction { Exit, Entry }

public enum VehicleStatus { Available, Assigned, InUse, Maintenance, OutOfService }

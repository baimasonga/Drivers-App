namespace AvdpSmartFleet.Api.Services;

public record GpsTracePoint(
    string UnitId,
    double Lat,
    double Lng,
    double SpeedKph,
    double Heading,
    bool? IgnitionOn,
    DateTime RecordedAt);

public interface IGpsTraceClient
{
    Task<IReadOnlyList<GpsTracePoint>> GetPositionsAsync(string unitId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<GpsTracePoint?> GetLatestAsync(string unitId, CancellationToken ct = default);
}

using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Services;

public enum TipKind { Praise, Suggestion, Warning }

public record CoachingTip(TipKind Kind, string Title, string Detail, double Evidence);

public record DriverCoaching(
    int DriverId,
    string Name,
    int TripsAnalysed,
    double AvgScore,
    string OverallVerdict,
    List<CoachingTip> Tips);

/// <summary>
/// Generates personalised, plain-English coaching tips per driver using rule-based pattern detection
/// over their last 60 days of trips. Each tip carries an "evidence" weight (0-1) indicating how
/// strongly the data supports it. Designed to be additive — drivers and coaches both see the same tips.
/// </summary>
public class DriverCoachingService
{
    private readonly AppDbContext _db;
    public DriverCoachingService(AppDbContext db) => _db = db;

    public async Task<DriverCoaching?> ForDriverAsync(int driverId, CancellationToken ct = default)
    {
        var driver = await _db.Drivers.Include(d => d.User).FirstOrDefaultAsync(d => d.Id == driverId, ct);
        if (driver == null) return null;

        var from = DateTime.UtcNow.AddDays(-60);
        var trips = await _db.TravelRequests
            .Where(t => t.AssignedDriverId == driverId && t.CompletedAt >= from)
            .ToListAsync(ct);

        var exceptions = await _db.TripExceptions
            .Include(e => e.TravelRequest)
            .Where(e => e.TravelRequest.AssignedDriverId == driverId && e.DetectedAt >= from)
            .ToListAsync(ct);

        var tips = new List<CoachingTip>();

        if (trips.Count == 0)
        {
            return new DriverCoaching(driver.Id, driver.User.FullName, 0, 0,
                "Not enough trip data yet. Complete a few trips and we'll have insights for you.",
                tips);
        }

        var scored = trips.Where(t => t.ComplianceScore.HasValue).ToList();
        var avg = scored.Count == 0 ? 0 : scored.Average(t => t.ComplianceScore!.Value);

        // ── Rule 1: praise streaks of 90%+ compliance ──
        var streak = 0;
        foreach (var t in scored.OrderByDescending(t => t.CompletedAt))
        {
            if (t.ComplianceScore >= 90) streak++; else break;
        }
        if (streak >= 5)
            tips.Add(new(TipKind.Praise, $"🏆 {streak}-trip compliance streak",
                $"Your last {streak} trips all scored 90% or higher. Keep up the consistency — your record is donor-audit ready.", 1.0));

        // ── Rule 2: late returns ──
        var lateCount = trips.Count(t => t.CompletedAt > t.PlannedReturn.AddMinutes(60));
        if (trips.Count >= 5 && (double)lateCount / trips.Count >= 0.3)
            tips.Add(new(TipKind.Suggestion, "Plan more buffer time on return",
                $"You returned more than 60 min late on {lateCount} of your last {trips.Count} trips. Consider requesting a later planned return time, or leaving the destination earlier.",
                Math.Min(1.0, (double)lateCount / trips.Count)));

        // ── Rule 3: late departures ──
        var startsKnown = trips.Where(t => t.StartedAt.HasValue).ToList();
        var lateDepartures = startsKnown.Where(t => (t.StartedAt!.Value - t.PlannedDeparture).TotalMinutes > 15).ToList();
        if (startsKnown.Count >= 5 && lateDepartures.Count >= 3)
        {
            var avgLateMin = lateDepartures.Average(t => (t.StartedAt!.Value - t.PlannedDeparture).TotalMinutes);
            tips.Add(new(TipKind.Suggestion, "You're often starting late",
                $"On {lateDepartures.Count} of {startsKnown.Count} trips you left {avgLateMin:F0} min after the planned departure on average. Aim to arrive at the vehicle 15 min before scheduled time.",
                Math.Min(1.0, (double)lateDepartures.Count / startsKnown.Count)));
        }

        // ── Rule 4: odometer accuracy ──
        var withBoth = trips.Where(t => t.DistanceKm.HasValue && t.StartOdometer.HasValue && t.EndOdometer.HasValue).ToList();
        if (withBoth.Count >= 3)
        {
            var avgDeltaPct = withBoth.Average(t =>
            {
                var claimed = t.EndOdometer!.Value - t.StartOdometer!.Value;
                if (t.DistanceKm == 0) return 0;
                return Math.Abs(claimed - t.DistanceKm!.Value) / t.DistanceKm.Value;
            });
            if (avgDeltaPct < 0.05)
                tips.Add(new(TipKind.Praise, "📏 Your odometer readings are spot-on",
                    $"On average your odometer entries match GPS distance within {(avgDeltaPct * 100):F1}%. Excellent record keeping.", 1.0));
            else if (avgDeltaPct > 0.15)
                tips.Add(new(TipKind.Warning, "Odometer entries are drifting",
                    $"Your odometer claims differ from GPS distance by {(avgDeltaPct * 100):F0}% on average. Double-check the meter at gate clearance — or take a photo so the OCR can fill it in.",
                    Math.Min(1.0, avgDeltaPct * 4)));
        }

        // ── Rule 5: recurring exception types ──
        var grouped = exceptions
            .GroupBy(e => e.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();
        var top = grouped.FirstOrDefault();
        if (top != null && top.Count >= 3)
        {
            var advice = top.Type switch
            {
                ExceptionType.UnauthorizedStop => "Plan fuel and rest stops at registered geofences before you leave. Suggest new official locations to the Fleet Officer via the map.",
                ExceptionType.RouteDeviation => "If the approved corridor is impractical, raise a diversion request before leaving the corridor instead of after.",
                ExceptionType.DestinationMismatch => "Confirm the destination geofence is exactly where you need to be. The Fleet Officer can adjust the radius if it's too tight.",
                ExceptionType.LateReturn => "Aim to leave the destination earlier — consider requesting a longer planned return window.",
                ExceptionType.OdometerMismatch => "Capture an odometer photo on departure and return — the app will OCR the reading for you.",
                ExceptionType.TrackerOffline => "If the tracker keeps going silent, raise a vehicle issue and let Fleet investigate the GPS device.",
                _ => "Discuss with your Fleet Officer how to avoid this pattern next time."
            };
            tips.Add(new(TipKind.Warning, $"Recurring: {top.Type}",
                $"{top.Count} '{top.Type}' exception(s) in the last 60 days. {advice}",
                Math.Min(1.0, top.Count / 5.0)));
        }

        // ── Rule 6: open queries pile-up ──
        var openQueries = exceptions.Count(e => e.Status == ExceptionStatus.Open);
        if (openQueries >= 2)
            tips.Add(new(TipKind.Warning, "Open queries waiting for your response",
                $"You have {openQueries} unanswered queries from Fleet. Open them in the app and explain — silence is treated as non-compliance during audits.",
                1.0));

        // ── Rule 7: trip volume praise ──
        if (trips.Count >= 20)
            tips.Add(new(TipKind.Praise, "Workhorse",
                $"You completed {trips.Count} trips in the last 60 days — among the most active in the fleet.", 0.9));

        // ── Rule 8: photo discipline ──
        var logs = await _db.TripLogs
            .Include(l => l.TravelRequest)
            .Where(l => l.TravelRequest.AssignedDriverId == driverId && l.EventAt >= from)
            .ToListAsync(ct);
        var preDepLogs = logs.Where(l => l.EventType == "PreDeparture").ToList();
        if (preDepLogs.Count >= 5)
        {
            var photoRate = (double)preDepLogs.Count(l => !string.IsNullOrEmpty(l.PhotoUrl)) / preDepLogs.Count;
            if (photoRate < 0.5)
                tips.Add(new(TipKind.Suggestion, "Capture odometer photos",
                    $"Only {(photoRate * 100):F0}% of your pre-departure checklists included a photo. The OCR can fill in the reading automatically — it's faster than typing.",
                    1 - photoRate));
        }

        // Overall verdict
        string verdict = avg switch
        {
            >= 95 => "Outstanding driver. Your record is donor-audit ready.",
            >= 90 => "Strong, consistent driver with minor improvements possible.",
            >= 80 => "Reliable, but a few patterns are worth tightening.",
            >= 70 => "Mixed results — review the suggestions below.",
            _ => "Focused coaching needed. Work through each suggestion with your Fleet Officer."
        };

        // Always add a final encouragement if no tips otherwise
        if (tips.Count == 0)
            tips.Add(new(TipKind.Praise, "No issues found",
                "Your trip records are clean. Keep doing what you're doing.", 1.0));

        return new DriverCoaching(driver.Id, driver.User.FullName, trips.Count,
            Math.Round(avg, 1), verdict, tips.OrderByDescending(t => t.Kind).ThenByDescending(t => t.Evidence).ToList());
    }
}

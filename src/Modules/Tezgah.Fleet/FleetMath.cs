namespace Tezgah.Fleet;

internal static class FleetMath
{
    public const int AlertWindowDays = 30;

    /// <summary>
    /// Average consumption in litres per 100 km. The first fill-up only marks the starting odometer
    /// (the fuel bought then was burned before we started counting), so it takes at least two entries.
    /// </summary>
    public static decimal? AverageConsumption(IReadOnlyCollection<(int OdometerKm, decimal Liters)> fillUps)
    {
        if (fillUps.Count < 2)
        {
            return null;
        }

        var ordered = fillUps.OrderBy(fill => fill.OdometerKm).ToList();
        var distance = ordered[^1].OdometerKm - ordered[0].OdometerKm;
        if (distance <= 0)
        {
            return null;
        }

        var burned = ordered.Skip(1).Sum(fill => fill.Liters);
        return decimal.Round(burned / distance * 100, 1);
    }

    /// <summary>Days until the date, negative when overdue; <c>null</c> when it is not close enough to mention.</summary>
    public static int? DaysLeftIfDueSoon(DateOnly? dueOn, DateOnly today)
    {
        if (dueOn is null)
        {
            return null;
        }

        var daysLeft = dueOn.Value.DayNumber - today.DayNumber;
        return daysLeft <= AlertWindowDays ? daysLeft : null;
    }
}

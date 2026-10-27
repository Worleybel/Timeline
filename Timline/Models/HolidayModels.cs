namespace Timline.Models;

/// <summary>一个节假日条目：要么是法定假日（IsOffDay=true），要么是调休补班日（IsOffDay=false）。</summary>
public record HolidayDay(string Name, DateOnly Date, bool IsOffDay);

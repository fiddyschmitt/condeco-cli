namespace condeco_cli.Scheduling
{
    public class ScheduleInfo
    {
        public required string Days { get; set; }
        public required TimeOnly Time { get; set; }

        public string Summary => $"{Days} {Time:h:mm tt}";
    }
}

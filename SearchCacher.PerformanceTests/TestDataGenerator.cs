namespace SearchCacher.PerformanceTests
{
    public class TestDataGenerator
    {
        // We seed the random generator to get consistent results
        public static Random Random = new Random(1234567890);

        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                      .Select(s => s[Random.Next(s.Length)]).ToArray());
        }
    }
}

using NUnit.Framework;

namespace SearchCacher.UnitTests
{
    public class StatisticsTests
    {
        public StatisticsTests()
        {
            _statistics = new Statistics("teststats.json", "test", true);
            _statistics.Start();
        }

        ~StatisticsTests()
        {
            _statistics.Stop();
        }

        [Test()]
        public void AddFile_ShouldAddFileToStatistics()
        {
            // Arrange
            long size = 100;
            long hash = 12345678;
            string testFilePath = "C:\\temp\\testfile.txt";

            // Act
            _statistics.AddFile(size, hash, testFilePath);

            // Wait for the statistics to process the queue
            Thread.Sleep(50);

            // Assert
            var tops = _statistics.GetTop(0, 25);
            NUnit.Framework.Assert.AreEqual(tops, new List<(string, long)>() { (testFilePath, size) });
        }

        [Test()]
        public void AddFileWithSameHash_ShouldRemoveOldFile()
        {
            // Arrange
            long size = 100;
            long hash = 12345678;
            string testFilePath = "C:\\temp\\testfile.txt";

            // Act
            _statistics.AddFile(size, hash, testFilePath);
            _statistics.AddFile(size, hash, testFilePath);

            // Wait for the statistics to process the queue
            Thread.Sleep(50);

            // Assert
            var tops = _statistics.GetTop(0, 25);
            NUnit.Framework.Assert.AreEqual(tops, new List<(string, long)>() { (testFilePath, size) });
        }

        [Test()]
        public void RemoveFile_ShouldRemoveOldFile()
        {
            // Arrange
            long size = 100;
            long hash = 12345678;
            string testFilePath = "C:\\temp\\testfile.txt";
            _statistics.AddFile(size, hash, testFilePath);

            // Wait for the statistics to process the queue
            Thread.Sleep(50);

            // Act
            _statistics.RemoveFile(hash);

            // Wait for the statistics to process the queue
            Thread.Sleep(50);

            // Assert
            var tops = _statistics.GetTop(0, 25);
            NUnit.Framework.Assert.AreEqual(tops, new List<(string, long)>() { });
        }

        private Statistics _statistics;
    }
}

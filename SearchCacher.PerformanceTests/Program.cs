namespace SearchCacher.PerformanceTests
{
	internal class Program
	{
		static void Main(string[] args)
		{
			dynamic test;

            // Hash vs String comparison
			//test = new HashVsStringTest();

            test = new InitTest();
			test.Start();
        }
	}

	public class InitTest
	{
		public InitTest()
		{
			try
			{
				_randomWords = System.IO.File.ReadAllLines(Path.Combine(CryLib.Core.Paths.AppPath, "words_alpha.txt"));
				GenerateFakeData();
			}
			catch(Exception ex)
			{
				throw;
			}
        }

		public void Start()
		{
			
        }

		private void GenerateFakeData()
		{
			int depth = 0;
			int maxDepth = 10;
            rootDir = new Dir("C:\\temp\\root", null);
			RecursiveFill(rootDir, depth, maxDepth);
        }

		private void RecursiveFill(Dir dirToFill, int depth, int maxDepth)
		{
			depth++;
			if (depth == maxDepth)
				return;

			int dirCount = TestDataGenerator.Random.Next(0, 8);
			dirToFill.Directories = Enumerable.Repeat<Dir>(new Dir(), dirCount).ToArray();

            for (int i = 0; i < dirCount; i++)
			{
				// Repeat for as long as the parent dir already contains the given folder name
				string newFolderName = "";
				do
				{
					newFolderName = _randomWords[TestDataGenerator.Random.Next(0, _randomWords.Length)];
				} while (dirToFill.Directories.Any(d => d.Name == newFolderName));

				Dir newDir = new Dir(newFolderName, dirToFill);
				dirToFill.Directories[i] = newDir;
				RecursiveFill(newDir, depth, maxDepth);

                int fileCount = TestDataGenerator.Random.Next(0, 15);
				dirToFill.Files = new File[fileCount];

                // Repeat for as long as the parent dir already contains the given file name
                string newFileName = "";
                do
                {
                    newFileName = _randomWords[TestDataGenerator.Random.Next(0, _randomWords.Length)] + ".txt";
                } while (dirToFill.Directories.Any(d => d.Name == newFileName));

                for (int j = 0; j < fileCount; j++)
                    dirToFill.Files[j] = new File(newFileName, newDir, 10);
            }
        }

        private Dir rootDir;
        private string[] _randomWords = [];
	}
}

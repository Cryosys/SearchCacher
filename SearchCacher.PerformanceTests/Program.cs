using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace SearchCacher.PerformanceTests
{
	internal class Program
	{
		static void Main(string[] args)
		{
			var summary = BenchmarkRunner.Run<HashVsString>();
		}

		// We seed the random generator to get consistent results
		public static Random Random = new Random(123456);

		public static string RandomString(int length)
		{
			const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
			return new string(Enumerable.Repeat(chars, length)
					  .Select(s => s[Random.Next(s.Length)]).ToArray());
		}
	}

	// * Result *
	//
	// BenchmarkDotNet v0.14.0, Windows 10 (10.0.19045.5371/22H2/2022Update)
	// AMD Ryzen 9 7900X3D, 1 CPU, 24 logical and 12 physical cores
	// .NET SDK 9.0.200
	//  [Host]     : .NET 9.0.2 (9.0.225.6610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
	//  DefaultJob : .NET 9.0.2 (9.0.225.6610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
	//
	//
	// | Method        | Mean     | Error   | StdDev  |
	// |-------------- |---------:|--------:|--------:|
	// | HashCompare   | 293.2 ns | 0.59 ns | 0.46 ns |
	// | StringCompare | 983.4 ns | 4.39 ns | 4.10 ns |
	public class HashVsString
	{
		static string[] _texts        = new string[1000];
		static string[] _compareTexts = new string[1000];
		static long[] _hashes         = new long[1000];
		static long[] _compareHashes  = new long[1000];

		public HashVsString()
		{
			// File paths would be realistically at least 18 chars long and at most 250
			for (int i = 0; i < _texts.Length; i++)
			{
				_texts[i]        = Program.RandomString(Program.Random.Next(18, 250));
				_compareTexts[i] = Program.RandomString(Program.Random.Next(18, 250));

				_hashes[i]        = BitConverter.ToInt64(MD5.HashData(Encoding.Unicode.GetBytes(_texts[i])));
				_compareHashes[i] = BitConverter.ToInt64(MD5.HashData(Encoding.Unicode.GetBytes(_compareTexts[i])));
			}
		}

		[Benchmark]
		public void HashCompare()
		{
			bool result = false;
			for (int i = 0; i < _hashes.Length; i++)
				result = _hashes[i] == _compareHashes[i];
		}

		[Benchmark]
		public void StringCompare()
		{
			bool result = false;
			for (int i = 0; i < _texts.Length; i++)
				result = _texts[i] == _compareTexts[i];
		}
	}
}

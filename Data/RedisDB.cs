using StackExchange.Redis;

namespace SearchCacher
{
	internal class RedisDB
	{
		StackExchange.Redis.ConnectionMultiplexer? _connector;
		IDatabase? _db;
		IServer? _server;

		string _connectionString = "localhost:6379";

		internal object LockObj = new object();

		public bool Connect(string connectionString)
		{
			if (_connector != null)
				_connector.Dispose();

			_connectionString = connectionString;

			try
			{
				_connector = StackExchange.Redis.ConnectionMultiplexer.Connect($"{connectionString},syncTimeout=60000,allowAdmin=true");
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
				return false;
			}

			try
			{
				_server = _connector.GetServer($"{connectionString}");
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
				return false;
			}

			try
			{
				_db = _connector.GetDatabase();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
				return false;
			}

			return _connector.IsConnected;
		}

		public void FlushAll()
		{
			if (_server is null)
				return;

			_server.FlushAllDatabases();
		}

		public bool KeyExists(string key)
		{
			if (_db is null)
				return false;

			return _db.KeyExists(key);
		}

		public bool SetString(string key, string value)
		{
			if (_db is null)
				return false;

			return _db.StringSet(key, value);
		}

		public string? GetString(string key)
		{
			if (_db is null)
				return String.Empty;

			return _db.StringGet(key).ToString();
		}

		public string?[] LRANGE(string key)
		{
			if (_db is null)
				return Array.Empty<string>();

			return _db.ListRange(key).ToStringArray();
		}

		public long LPUSH(string key, string value)
		{
			if (_db is null)
				return 0L;

			return _db.ListLeftPush(key, value);
		}

		public long RPUSH(string key, string value)
		{
			if (_db is null)
				return 0L;

			return _db.ListRightPush(key, value);
		}

		public long LREM(string key, string value)
		{
			if (_db is null)
				return 0L;

			return _db.ListRemove(key, value);
		}

		public string?[] SSCAN(string? key, string pattern)
		{
			if (_db is null || key is null)
				return Array.Empty<string>();

			return _db.SetScan(key, pattern).ToArray().ToStringArray();
		}

		public bool DEL(string key)
		{
			if (_db is null)
				return false;

			return _db.KeyDelete(key);
		}

		public bool SADD(string key, string value)
		{
			if (_db is null)
				return false;

			return _db.SetAdd(key, value);
		}

		public long SADD(string key, string[] value)
		{
			if (_db is null)
				return 0;

			return _db.SetAdd(key, value.ToRedisValueArray());
		}

		public bool SREM(string key, string value)
		{
			if (_db is null)
				return false;

			return _db.SetRemove(key, value);
		}

		public string?[] SMEMBERS(string key)
		{
			if (_db is null)
				return Array.Empty<string>();

			return _db.SetMembers(key).ToStringArray();
		}

		public List<RedisKey> SCAN(string pattern)
		{
			if (_server is null)
				return new List<RedisKey>();

			List<RedisKey> keys = new List<RedisKey>();

			int pageSize                  = 250;
			IEnumerable<RedisKey> newKeys = _server.Keys(pattern: pattern, pageSize: pageSize, cursor: 0);
			keys.AddRange(newKeys);

			while (newKeys.Count() >= pageSize)
			{
				newKeys = _server.Keys(pattern: pattern, pageSize: pageSize, cursor: keys.Count() - 1);
				keys.AddRange(newKeys);
			}

			return keys;
		}
	}
}

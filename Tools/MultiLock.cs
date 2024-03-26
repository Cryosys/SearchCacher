namespace SearchCacher.Tools
{
	public class MultiLock
	{
		public int CurrentLocks => _counter;

		public bool IsMasterLocked => _isMasterLocked;

		private int _counter;

		private object _masterLockObject = new object();
		private bool _isMasterLocked     = false;

		private ManualResetEvent _masterLockResetEvent = new ManualResetEvent(true);

		public MultiLock()
		{
			_counter = 0;
		}

		public bool RequestMultiLock()
		{
			if (_isMasterLocked)
				return false;

			lock (_masterLockObject)
			{
				_counter++;
				Program.Log("Acquired lock");
				return true;
			}
		}

		public Task<bool> RequestMultiLockAsync(CancellationToken token)
		{
			return Task<bool>.Run(() =>
			{
				while (!token.IsCancellationRequested)
				{
					if (_masterLockResetEvent.WaitOne(200))
					{
						lock (_masterLockObject)
						{
							_counter++;
							Program.Log("Acquired lock");
							return true;
						}
					}
				}

				return false;
			});
		}

		public bool ReleaseMultiLock()
		{
			lock (_masterLockObject)
			{
				_counter--;
				Program.Log("Released lock");
				return true;
			}
		}

		public Task<bool> ReleaseMultiLockAsync(CancellationToken token)
		{
			return Task<bool>.Run(() =>
			{
				while (!token.IsCancellationRequested)
				{
					if (_masterLockResetEvent.WaitOne(200))
					{
						lock (_masterLockObject)
						{
							_counter--;
							Program.Log("Released lock");
							return true;
						}
					}
				}

				return false;
			});
		}

		public bool RequestMasterLock()
		{
			// Someone may have already acquired the master lock
			if (_isMasterLocked)
				return false;

			lock (_masterLockObject)
			{
				if (_counter != 0)
					return false;

				_masterLockResetEvent.Reset();
				_isMasterLocked = true;
				Program.Log("Acquired master lock");
				return true;
			}
		}

		public Task<bool> RequestMasterLockAsync(CancellationToken token)
		{
			return Task.Run(() =>
			{
				// Set the lock so that we can wait for the counters
				_isMasterLocked = true;

				while (!token.IsCancellationRequested)
				{
					if (!_masterLockResetEvent.WaitOne(200))
						// The master lock is already in use
						continue;

					if (_counter != 0)
						// Can only acquire the master lock if no other locks is set
						continue;

					_masterLockResetEvent.Reset();
					Program.Log("Acquired master lock");
					return true;
				}

				return false;
			});
		}

		public bool ReleaseMasterLock()
		{
			// The master lock may not be acquired previously or already released
			if (!_isMasterLocked)
				return true;

			lock (_masterLockObject)
			{
				if (_counter != 0)
					// This should not be the case but could happen
					return false;

				_isMasterLocked = false;
				_masterLockResetEvent.Set();
				Program.Log("Released master lock");
				return true;
			}
		}

		public Task<bool> ReleaseMasterLockAsync(CancellationToken token)
		{
			return Task.Run(() =>
			{
				while (!token.IsCancellationRequested)
				{
					if (_masterLockResetEvent.WaitOne(200))
						// The master lock is already in use
						continue;

					if (_counter != 0)
						// Can only acquire the master lock if no other locks is set
						continue;

					_isMasterLocked = false;
					_masterLockResetEvent.Set();
					Program.Log("Released master lock");
					return true;
				}

				return false;
			});
		}
	}
}

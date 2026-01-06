namespace SearchCacher.Tools
{
    public class MultiLock
    {
        public int CurrentLocks => _counter;

        public bool IsMasterLocked => _isMasterLocked;

        private int _counter;

        private Lock _masterLockObject = new();
        private bool _isMasterLocked = false;

        private ManualResetEvent _masterLockResetEvent = new ManualResetEvent(true);

        public MultiLock()
        {
            _counter = 0;
        }

        public bool RequestMultiLock()
        {
            Program.Log("Waiting for multi lock");
            lock (_masterLockObject)
            {
                if (_isMasterLocked)
                {
                    Program.Log("Master is currently locked, cannot acquire multi lock");
                    return false;
                }

                _counter++;
                Program.Log("Acquired multi lock");
                return true;
            }
        }

        public Task<bool> RequestMultiLockAsync(CancellationToken token)
        {
            return Task<bool>.Run(() =>
            {
                Program.Log("Waiting for multi lock async");
                while (!token.IsCancellationRequested)
                {
                    if (!_masterLockResetEvent.WaitOne(200))
                        continue;

                    lock (_masterLockObject)
                    {
                        _counter++;
                        Program.Log("Acquired multi lock");
                        return true;
                    }
                }

                return false;
            });
        }

        public bool ReleaseMultiLock()
        {
            Program.Log("Releasing multi lock");
            lock (_masterLockObject)
            {
                _counter--;
                Program.Log("Released multi lock");
                return true;
            }
        }

        public Task<bool> ReleaseMultiLockAsync(CancellationToken token)
        {
            return Task<bool>.Run(() =>
            {
                Program.Log("Releasing multi lock async");
                while (!token.IsCancellationRequested)
                {
                    if (!_masterLockResetEvent.WaitOne(200))
                        continue;

                    lock (_masterLockObject)
                    {
                        _counter--;
                        Program.Log("Released multi lock");
                        return true;
                    }
                }

                return false;
            });
        }

        public bool RequestMasterLock()
        {

            Program.Log("Waiting for master lock");
            lock (_masterLockObject)
            {
                // Someone may have already acquired the master lock
                if (_isMasterLocked)
                {
                    Program.Log("Master lock already aquired by someone else");
                    return false;
                }

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
                Program.Log("Waiting for master lock async");
                while (!token.IsCancellationRequested)
                {
                    if (!_masterLockResetEvent.WaitOne(200))
                        // The master lock is already in use
                        continue;

                    lock (_masterLockObject)
                    {
                        if (_counter != 0)
                            // Can only acquire the master lock if no other locks is set
                            continue;

                        _masterLockResetEvent.Reset();
                        _isMasterLocked = true;
                        Program.Log("Acquired master lock");
                        return true;
                    }
                }

                return false;
            });
        }

        public bool ReleaseMasterLock()
        {
            Program.Log("Releasing master lock");
            lock (_masterLockObject)
            {
                // The master lock may not be acquired previously or already released
                if (!_isMasterLocked)
                {
                    Program.Log("Master lock did not need to be released, someone called release without locking first?");
                    return true;
                }

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
                Program.Log("Releasing master lock async");
                while (!token.IsCancellationRequested)
                {
                    if (_masterLockResetEvent.WaitOne(200))
                        // The master lock is already in use
                        continue;

                    lock (_masterLockObject)
                    {
                        // The master lock may not be acquired previously or already released
                        if (!_isMasterLocked)
                        {
                            Program.Log("Master lock did not need to be released, someone called release without locking first?");
                            return true;
                        }

                        if (_counter != 0)
                            // Can only acquire the master lock if no other locks is set
                            continue;

                        _isMasterLocked = false;
                        _masterLockResetEvent.Set();
                        Program.Log("Released master lock");
                        return true;
                    }
                }

                return false;
            });
        }
    }
}

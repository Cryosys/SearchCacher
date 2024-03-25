using NUnit.Framework;
using SearchCacher.Tools;

namespace SearchCacher.Tools.Tests
{
	[TestFixture()]
	public class MultiLockTests
	{
		[Test()]
		public void RequestMultiLockAsync_ShouldAcquireLockAndReleaseIt()
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			MultiLock lockItem                   = new MultiLock();

			if (!lockItem.RequestMultiLockAsync(cancelSource.Token).Result)
				NUnit.Framework.Assert.Fail();
			Task.Delay(100).Wait();

			if (!lockItem.RequestMultiLockAsync(cancelSource.Token).Result)
				NUnit.Framework.Assert.Fail();
			Task.Delay(100).Wait();

			if (lockItem.CurrentLocks != 2)
				NUnit.Framework.Assert.Fail();

			if (!lockItem.ReleaseMultiLockAsync(cancelSource.Token).Result)
				NUnit.Framework.Assert.Fail();
			Task.Delay(100).Wait();

			if (!lockItem.ReleaseMultiLockAsync(cancelSource.Token).Result)
				NUnit.Framework.Assert.Fail();

			if (lockItem.CurrentLocks != 0)
				NUnit.Framework.Assert.Fail();
		}

		[Test()]
		public void RequestMasterLockAsync_ShouldAcquireMasterLockAndReleaseIt()
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			MultiLock lockItem                   = new MultiLock();

			if (!lockItem.RequestMasterLockAsync(cancelSource.Token).Result)
				NUnit.Framework.Assert.Fail();

			Task.Delay(100).Wait();

			if (!lockItem.IsMasterLocked)
				NUnit.Framework.Assert.Fail();

			if (!lockItem.ReleaseMasterLockAsync(cancelSource.Token).Result)
				NUnit.Framework.Assert.Fail();

			if (lockItem.IsMasterLocked)
				NUnit.Framework.Assert.Fail();
		}

		[Test()]
		public void RequestMasterLockAsync_ShouldWaitForAllOtherLocksAndAcquireMasterLockAndReleaseIt()
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			MultiLock lockItem                   = new MultiLock();

			if (!lockItem.RequestMultiLock())
				NUnit.Framework.Assert.Fail();

			Task<bool> reqMasterLockTask = lockItem.RequestMasterLockAsync(cancelSource.Token);

			Task.Delay(5000).Wait();

			if (!lockItem.ReleaseMultiLock())
				NUnit.Framework.Assert.Fail();

			if (!reqMasterLockTask.Result)
				NUnit.Framework.Assert.Fail();

			Task.Delay(100).Wait();

			if (!lockItem.IsMasterLocked)
				NUnit.Framework.Assert.Fail();

			if (!lockItem.ReleaseMasterLockAsync(cancelSource.Token).Result)
				NUnit.Framework.Assert.Fail();

			if (lockItem.IsMasterLocked)
				NUnit.Framework.Assert.Fail();
		}
	}
}

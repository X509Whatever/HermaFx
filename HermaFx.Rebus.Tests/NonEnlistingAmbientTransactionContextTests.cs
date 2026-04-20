using System.Transactions;

using NUnit.Framework;

namespace HermaFx.Rebus.Tests
{
	[TestFixture]
	public class NonEnlistingAmbientTransactionContextTests
	{
		#region Private methods

		private static TransactionOptions GetTransactionOptions() =>
			new TransactionOptions()
			{
				IsolationLevel = IsolationLevel.ReadCommitted,
				Timeout = TransactionManager.DefaultTimeout
			};

		#endregion

		[Test]
		public void Context_Disposed_Before_Ambient_Commit_Still_Triggers_DoCommit()
		{
			var committed = false;

			using (var ts = new TransactionScope(TransactionScopeOption.RequiresNew, GetTransactionOptions(), TransactionScopeAsyncFlowOption.Enabled))
			using (var ctx = new NonEnlistingAmbientTransactionContext())
			{
				ctx.DoCommit += () => committed = true;
				ts.Complete();
			}

			Assert.That(committed, Is.True);
		}
	}
}

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Transactions;

using Rebus;
using Rebus.Bus;

namespace HermaFx.Rebus
{
	/// <summary>
	/// Transaction context bound to an existing ambient <see cref="Transaction"/>, without enlisting as a
	/// transaction resource manager (neither durable nor volatile).
	///
	/// This context is intended for best-effort coordination: it observes the ambient transaction completion
	/// status and raises Rebus commit/rollback callbacks, while avoiding enlistment/promotion side effects.
	/// </summary>
	public sealed class NonEnlistingAmbientTransactionContext : ITransactionContext
	{
		#region Fields & Properties

		private readonly ConcurrentDictionary<string, object> _items = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
		private readonly Transaction _tx;

		private int _disposed;
		private int _completed;
		private int _cleanupRan;

		/// <summary>
		/// Returns true because this context requires and tracks an ambient transaction.
		/// (This does not imply full distributed transactional guarantees for all side effects).
		/// </summary>
		public bool IsTransactional => true;

		public event Action DoCommit = delegate { };
		public event Action DoRollback = delegate { };
		public event Action BeforeCommit = delegate { };
		public event Action AfterRollback = delegate { };
		public event Action Cleanup = delegate { };

		#endregion

		#region .ctors

		public NonEnlistingAmbientTransactionContext()
		{
			_tx = Transaction.Current?.Clone()
				?? throw new InvalidOperationException(
					"There's currently no ambient transaction associated with this thread. " +
					"You can only instantiate this context within a TransactionScope.");
			_tx.TransactionCompleted += OnTransactionCompleted;

			TransactionContext.Set(this);
		}

		#endregion

		#region Indexers

		public object this[string key]
		{
			get => _items.TryGetValue(key.ThrowIfNullOrWhiteSpace(nameof(key)), out var value) ? value : null;
			set
			{
				Guard.IsNotNullNorWhitespace(key, nameof(key));
				if (value == null)
				{
					_items.TryRemove(key, out _);
					return;
				}

				_items[key] = value;
			}
		}

		#endregion

		#region Private methods

		private void RunCleanupOnce()
		{
			if (Interlocked.Exchange(ref _cleanupRan, 1) != 0)
				return;

			try
			{
				Cleanup();
			}
			finally
			{
				if (ReferenceEquals(TransactionContext.Current, this))
					TransactionContext.Clear();
			}
		}

		private void OnTransactionCompleted(object sender, TransactionEventArgs e)
		{
			if (Interlocked.Exchange(ref _completed, 1) != 0)
				return;

			try
			{
				switch (e.Transaction.TransactionInformation.Status)
				{
				case TransactionStatus.Committed:
					BeforeCommit();
					DoCommit();
					break;

				case TransactionStatus.Aborted:
					DoRollback();
					AfterRollback();
					break;

				case TransactionStatus.InDoubt:
					DoRollback();
					AfterRollback();
					break;
				}
			}
			finally
			{
				RunCleanupOnce();
			}
		}

		#endregion

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) != 0)
				return;

			_tx.TransactionCompleted -= OnTransactionCompleted;
			_tx.Dispose(); //< _tx is a Transaction.Clone(), disposing it only releases this reference.

			RunCleanupOnce();
		}
	}
}

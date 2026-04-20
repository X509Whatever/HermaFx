using System;
using System.Collections.Concurrent;
using System.Transactions;
using HermaFx.Logging;

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
		#region Inner Types

		public enum _State
		{
			Created = 1,
			Commit,
			Rollback,
			Cleaned
		}

		#endregion

		#region Fields & Properties

		private static readonly ILog _logger = LogProvider.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		private readonly ConcurrentDictionary<string, object> _items = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
		private readonly Transaction _tx;
		private readonly string _transactionId;

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

		public static event Action<string, _State> StateObserver = delegate { };

		#endregion

		#region .ctors

		public NonEnlistingAmbientTransactionContext()
		{
			_tx = Transaction.Current?.Clone()
				?? throw new InvalidOperationException(
					"There's currently no ambient transaction associated with this thread. " +
					"You can only instantiate this context within a TransactionScope.");

			_transactionId = _tx.TransactionInformation.LocalIdentifier;
			_logger.Debug("Attaching to ambient transaction {0}", _transactionId);
			_tx.TransactionCompleted += OnTransactionCompleted;

			TransactionContext.Set(this);
			NotifyState(_State.Created);
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

		private void NotifyState(_State state)
		{
			Guard.IsNotDefault(state, nameof(state));

			var handlers = StateObserver;
			foreach (Action<string, _State> observer in handlers.GetInvocationList())
			{
				try
				{
					observer(_transactionId, state);
				}
				catch (Exception ex)
				{
					_logger.Error(ex, "Observer {0} threw an exception while processing state transition {1} for transaction {2}", observer.Method.Name, state, _transactionId);
				}
			}
		}

		private void DetachTransaction()
		{
			_logger.Debug("Detaching from ambient transaction {0}", _transactionId);
			_tx.TransactionCompleted -= OnTransactionCompleted;
			_tx.Dispose(); //< _tx is a Transaction.Clone(), disposing it only releases this reference.
		}

		private void RunCleanup()
		{
			_logger.Debug("Running transaction context cleanup for ambient transaction {0}", _transactionId);

			try
			{
				Cleanup();
				NotifyState(_State.Cleaned);
			}
			finally
			{
				if (ReferenceEquals(TransactionContext.Current, this))
					TransactionContext.Clear();
			}
		}

		private void OnTransactionCompleted(object sender, TransactionEventArgs e)
		{
			var txInfo = e.Transaction.TransactionInformation;

			_logger.Debug("Ambient transaction {0} completed with status {1}", txInfo.LocalIdentifier, txInfo.Status);

			try
			{
				switch (txInfo.Status)
				{
				case TransactionStatus.Committed:
					BeforeCommit();
					DoCommit();
					NotifyState(_State.Commit);
					break;

				case TransactionStatus.Aborted:
				case TransactionStatus.InDoubt: //< Treat InDoubt as a rollback trigger instead of a commit.
					DoRollback();
					AfterRollback();
					NotifyState(_State.Rollback);
					break;
				}
			}
			finally
			{
				DetachTransaction();
				RunCleanup();
			}
		}

		#endregion

		public void Dispose()
		{
			// We don't own the ambient transaction, so disposal is a no-op.
		}
	}
}

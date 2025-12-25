using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

internal class MessagingQueue : IDisposable, IMemoryPackerEntityPool
{
	internal class OwnerData
	{
		public readonly Dictionary<Type, Dictionary<string, object?>> TypedCallbacks = new();
		public TypeManager OutgoingTypeManager;
		public TypeManager IncomingTypeManager;
		public readonly string Id;
		public bool Initialized;
		public MessagingQueue Queue {get; private set;}
		public OwnerData(string id, MessagingQueue queue)
		{
			Id = id;
			Queue = queue;
			OutgoingTypeManager = new(queue.Pool, OnOutgoingTypeRegistered);
			IncomingTypeManager = new(queue.Pool, null);
			
		}
		public void OnOutgoingTypeRegistered(Type type)
		{
			var typeRegCommand = new TypeRegistrationCommand();
			typeRegCommand.Type = type;
			Queue.SendPackable(Id, "", typeRegCommand);
		}
	}
	public bool IsAuthority { get; }

	public string QueueName { get; }

	public long QueueCapacity { get; }

	public int ReceivedMessages => _primary.ReceivedMessages;

	public int SentMessages => _primary.SentMessages;

	public bool IsInitialized { get; private set; }

	public bool IsDisposed { get; private set; }

	private MessagingManager _primary;

	private static readonly MethodInfo _handlePackableMethod = typeof(MessagingQueue).GetMethod(nameof(HandlePackable), BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(HandlePackable));

	private Action<string>? _onWarning;

	private Action<string>? _onDebug;

	private Action<Exception>? _onFailure;

	private readonly Dictionary<string, OwnerData> _ownerData = new();

	private static readonly Dictionary<string, MessagingQueue> _registeredQueues = new();

	public IMemoryPackerEntityPool Pool;

	public void RegisterOwner(string ownerId)
	{
		_ownerData.Add(ownerId, new(ownerId, this));
		SendPackable(ownerId, "", new QueueOwnerInitCommand());
	}

	public bool HasOwner(string ownerName)
	{
		return _ownerData.ContainsKey(ownerName);
	}

	public OwnerData GetOwnerData(string ownerId)
	{
		return _ownerData[ownerId];
	}

	public void RegisterCallback<T>(string owner, string id, Action<T>? callback) where T : IMemoryPackable
	{
		if(!_ownerData[owner].TypedCallbacks.ContainsKey(typeof(T)))
			_ownerData[owner].TypedCallbacks.Add(typeof(T), new());

		_ownerData[owner].TypedCallbacks[typeof(T)][id] = callback;
	}

	public MessagingQueue(bool isAuthority, string queueName, long queueCapacity, IMemoryPackerEntityPool pool, Action<Exception>? failhandler = null, Action<string>? warnHandler = null, Action<string>? debugHandler = null)
	{
		if (queueName is null) throw new ArgumentNullException(nameof(queueName));
		if (pool is null) throw new ArgumentNullException(nameof(pool));

		IsAuthority = isAuthority;
		QueueName = queueName;
		QueueCapacity = queueCapacity;

		_onDebug = debugHandler;
		_onWarning = warnHandler;
		_onFailure = failhandler;

		Pool = pool;

		_primary = new MessagingManager(this);
		_primary.CommandHandler = CommandHandler;
		_primary.FailureHandler = (ex) =>
		{
			_onFailure?.Invoke(ex);
		};
		_primary.WarningHandler = (msg) =>
		{
			_onWarning?.Invoke(msg);
		};

		_registeredQueues.Add(QueueName, this);

		_primary.Connect(QueueName, IsAuthority, QueueCapacity);
	}

	public void Dispose()
	{
		_primary.Dispose();
		IsDisposed = true;
	}

	internal static MessagingQueue? TryGetRegisteredQueue(string queueName)
	{
		if (_registeredQueues.TryGetValue(queueName, out var queue)) return queue;
		return null;
	}

	private void HandlePackable<T>(string ownerId, string id, T obj) where T : class, IMemoryPackable, new()
	{
		if (_ownerData[ownerId].TypedCallbacks.TryGetValue(typeof(T), out var data) && data.ContainsKey(id))
		{
			var callback = (Action<T>?)data[id];
			callback?.Invoke(obj);
		}
		else
		{
			_onWarning?.Invoke($"{QueueName}:{ownerId} Packable of type {typeof(T)} with Id {id} is not registered to receive a callback!");
		}
	}

	private void CommandHandler(RendererCommand command, int messageSize)
	{
		if (command is WrapperCommand wrapperCommand)
		{
			var ownerData = wrapperCommand.OwnerData!;

			IMemoryPackable packable = wrapperCommand.Packable!;
			_onDebug?.Invoke($"{QueueName}:{ownerData.Id} Received {packable}");

			if (packable is QueueOwnerInitCommand)
			{
				if (ownerData.Initialized)
				{
					_onWarning?.Invoke($"{QueueName}:{ownerData.Id} Received additional QueueOwnerInitCommand! Registered types will be reset!");
					ownerData.OutgoingTypeManager = new(this, ownerData.OnOutgoingTypeRegistered);
					ownerData.IncomingTypeManager = new(this, null);
				}
				else
				{
					ownerData.Initialized = true;
				}
			}
			else if (!ownerData.Initialized)
			{
				throw new InvalidDataException("QueueOwnerInitCommand needs to be first!");
			}
			else if (packable is TypeRegistrationCommand typeRegCommand)
			{
				if (typeRegCommand.Type is not null)
				{
					_onDebug?.Invoke($"{QueueName}:{ownerData.Id} * Registering incoming type: {typeRegCommand.Type.Name}<{string.Join(",", (IEnumerable<Type>)typeRegCommand.Type.GenericTypeArguments)}>");
					_ownerData[ownerData.Id!].IncomingTypeManager.InvokeRegisterType(typeRegCommand.Type);
				}
				else
				{
					throw new InvalidDataException("Other process tried to register a type that could not be found in this process!");
				}
			}
			else
			{
				_handlePackableMethod.MakeGenericMethod(packable.GetType()).Invoke(this, [ownerData.Id, wrapperCommand.Id, packable]);
			}
		}
		else
		{
			_onWarning?.Invoke($"{QueueName} Received an unexpected RendererCommand! {command}");
		}
	}

	public void SendPackable<T>(string ownerId, string id, T packable) where T : class, IMemoryPackable, new()
	{
		if (packable is null) throw new ArgumentNullException(nameof(packable));

		if (ownerId is null) throw new ArgumentNullException(nameof(ownerId));

		_onDebug?.Invoke($"{QueueName}:{ownerId} Sending: {packable}");

		var ownerData = _ownerData[ownerId];

		if (!ownerData.OutgoingTypeManager.IsTypeRegistered<T>())
		{
			_onDebug?.Invoke($"{QueueName}:{ownerId} * Registering outgoing type: {typeof(T).Name}<{string.Join(",", (IEnumerable<Type>)typeof(T).GenericTypeArguments)}>");
			ownerData.OutgoingTypeManager.RegisterType<T>();
		}

		var wrapper = new WrapperCommand();
		wrapper.TypeIndex = ownerData.OutgoingTypeManager.GetTypeIndex(typeof(T));
		wrapper.OwnerData = ownerData;
		wrapper.Id = id;
		wrapper.Packable = packable;

		_primary.SendCommand(wrapper);
	}

	public void UnregisterOwner(string ownerId)
	{
		if (HasOwner(ownerId))
		{
			_ownerData.Remove(ownerId);
		}
		else
		{
			_onWarning?.Invoke($"{QueueName} Tried to unregister owner that was not registered: {ownerId}");
		}
		if (_ownerData.Count == 0)
		{
			Dispose();
		}
	}

    T IMemoryPackerEntityPool.Borrow<T>()
    {
        return Pool.Borrow<T>();
    }

    void IMemoryPackerEntityPool.Return<T>(T value)
    {
        Pool.Return(value);
    }
}
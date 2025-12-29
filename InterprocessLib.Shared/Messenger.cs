using Renderite.Shared;

namespace InterprocessLib;

/// <summary>
/// Simple interprocess messaging API.
/// </summary>
public class Messenger : IDisposable
{
	private MessagingQueue _currentQueue;

	/// <summary>
	/// Called when the interprocess queue has a critical error
	/// </summary>
	public static event Action<Exception>? OnFailure;

	/// <summary>
	/// Called when something potentially bad/unexpected happens
	/// </summary>
	public static event Action<string>? OnWarning;

	/// <summary>
	/// Called with debugging information
	/// </summary>
	public static event Action<string>? OnDebug;

	internal static event Action? OnShutdown;

	private string _ownerId;

	static Messenger()
	{
		Defaults.Init();
	}

	/// <summary>
	/// Creates an instance with a unique owner using the default queue (If using the standalone version of the library this will throw an error because there is no default queue!)
	/// </summary>
	/// <param name="ownerId">Unique identifier for this instance in this process. Should match the other process.</param>
	/// <param name="additionalObjectTypes">Unused parameter kept for backwards compatibility.</param>
	/// <param name="additionalValueTypes">Unused parameter kept for backwards compatibility.</param>
	/// <exception cref="ArgumentNullException"></exception>
	/// <exception cref="NotImplementedException"></exception>
	[Obsolete("Use the other constructors that don't take Type lists", false)]
	public Messenger(string ownerId, List<Type>? additionalObjectTypes = null, List<Type>? additionalValueTypes = null) : this(ownerId)
	{
	}

	/// <summary>
	/// Creates an instance with a unique owner using the default queue (If using the standalone version of the library this will throw an error because there is no default queue!)
	/// </summary>
	/// <param name="ownerId">Unique identifier for this instance in this process. Should match the other process.</param>
	/// <exception cref="ArgumentNullException"></exception>
	/// <exception cref="NotImplementedException"></exception>
	public Messenger(string ownerId)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		_ownerId = ownerId;
		var queueName = $"{ownerId}-{Defaults.DefaultQueuePrefix}";
		if (MessagingQueue.TryGetRegisteredQueue(queueName) is not MessagingQueue existingQueue)
		{
			_currentQueue = new MessagingQueue(Defaults.DefaultIsAuthority, queueName, MessagingManager.DEFAULT_CAPACITY, Defaults.DefaultPool, OnFailure, OnWarning, OnDebug);
		}
		else
		{
			_currentQueue = existingQueue;
		}
		Init();
	}

	/// <summary>
	/// Creates an instance with a unique owner and connects to a custom queue so you can talk to any process. You must remember to call Dispose after using it!
	/// </summary>
	/// <param name="ownerId">Unique identifier for this instance in this process. Should match the other process.</param>
	/// <param name="isAuthority">Does this process have authority over the other process? The authority process should always be started first.</param>
	/// <param name="queueName">Custom queue name. Should match the other process.</param>
	/// <param name="pool">Custom pool for borrowing and returning memory-packable types.</param>
	/// <param name="queueCapacity">Capacity for the custom queue in bytes.</param>
	/// <exception cref="ArgumentNullException"></exception>
	public Messenger(string ownerId, bool isAuthority, string queueName, IMemoryPackerEntityPool? pool = null, long queueCapacity = 1024*1024)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		_ownerId = ownerId;

		if (queueName is null)
			throw new ArgumentNullException(nameof(queueName));

		if (MessagingQueue.TryGetRegisteredQueue(queueName) is not MessagingQueue existingQueue)
		{
			_currentQueue = new MessagingQueue(isAuthority, queueName, queueCapacity, pool ?? Defaults.DefaultPool, OnFailure, OnWarning, OnDebug);
		}
		else
		{
			_currentQueue = existingQueue;
		}

		Init();
	}

	internal static void Shutdown()
	{
		OnShutdown?.Invoke();
	}

	private void Init()
	{
		_currentQueue.RegisterOwner(_ownerId);

		if (!_currentQueue.IsConnected)
			_currentQueue.Connect();

		_currentQueue.SendPackable(_ownerId, "", new QueueOwnerInitCommand());

		OnShutdown += _currentQueue.Dispose;
	}

	internal static void WarnHandler(string str)
	{
		OnWarning?.Invoke(str);
	}

	internal static void FailHandler(Exception ex)
	{
		OnFailure?.Invoke(ex);
	}

	internal static void DebugHandler(string str)
	{
		OnDebug?.Invoke(str);
	}

	public void SendValue<T>(string id, T value) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		var command = new ValueCommand<T>();
		command.Value = value;
		_currentQueue.SendPackable(_ownerId, id, command);
	}

	[Obsolete("Use SendValueCollection instead.")]
	public void SendValueList<T>(string id, List<T>? list) where T : unmanaged
	{
		SendValueCollection<List<T>, T>(id, list);
	}

	[Obsolete("Use SendValueCollection instead.")]
	public void SendValueHashSet<T>(string id, HashSet<T>? hashSet) where T : unmanaged
	{
		SendValueCollection<HashSet<T>, T>(id, hashSet);
	}

	public void SendValueCollection<C, T>(string id, C? collection) where C : ICollection<T>?, new() where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		var command = new ValueCollectionCommand<C, T>();
		command.Values = collection;
		_currentQueue.SendPackable(_ownerId, id, command);
	}

	public void SendValueArray<T>(string id, T[]? array) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		var command = new ValueArrayCommand<T>();
		command.Values = array;
		_currentQueue.SendPackable(_ownerId, id, command);
	}

	public void SendString(string id, string? str)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		var command = new StringCommand();
		command.String = str;
		_currentQueue.SendPackable(_ownerId, id, command);
	}

	[Obsolete("Use SendStringCollection instead.")]
	public void SendStringList(string id, List<string>? list)
	{
		SendStringCollection<List<string>>(id, list);
	}

	public void SendStringCollection<C>(string id, IReadOnlyCollection<string?>? collection) where C : ICollection<string>?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		var command = new StringCollectionCommand<C>();
		command.Strings = collection;
		_currentQueue.SendPackable(_ownerId, id, command);
	}

	public void SendStringArray(string id, string?[]? array)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		var command = new StringArrayCommand();
		command.Strings = array;
		_currentQueue.SendPackable(_ownerId, id, command);
	}

	public void SendEmptyCommand(string id)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		var command = new EmptyCommand();
		_currentQueue.SendPackable(_ownerId, id, command);
	}

	public void SendObject<T>(string id, T? obj) where T : class?, IMemoryPackable?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		var command = new ObjectCommand<T>();
		command.Object = obj;

		_currentQueue.SendPackable(_ownerId, id, command);
	}

	[Obsolete("Use SendObjectCollection instead.")]
	public void SendObjectList<T>(string id, List<T>? list) where T : class?, IMemoryPackable?, new()
	{
		SendObjectCollection<List<T>, T>(id, list);
	}

	public void SendObjectCollection<C, T>(string id, C? collection) where C : ICollection<T>?, new() where T : class?, IMemoryPackable?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		var command = new ObjectCollectionCommand<C, T>();
		command.Objects = collection;
		_currentQueue.SendPackable(_ownerId, id, command);
	}

	public void SendObjectArray<T>(string id, T[]? array) where T : class?, IMemoryPackable?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		var command = new ObjectArrayCommand<T>();
		command.Objects = array;
		_currentQueue.SendPackable(_ownerId, id, command);
	}

	public void ReceiveValue<T>(string id, Action<T>? callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		_currentQueue.RegisterCallback<ValueCommand<T>>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Value));
	}

	[Obsolete("Use ReceiveValueCollection instead.")]
	public void ReceiveValueList<T>(string id, Action<List<T>>? callback) where T : unmanaged
	{
		ReceiveValueCollection<List<T>, T>(id, callback);
	}

	[Obsolete("Use ReceiveValueCollection instead.")]
	public void ReceiveValueHashSet<T>(string id, Action<HashSet<T>>? callback) where T : unmanaged
	{
		ReceiveValueCollection<HashSet<T>, T>(id, callback);
	}

	public void ReceiveValueCollection<C, T>(string id, Action<C>? callback) where C : ICollection<T>?, new() where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		_currentQueue.RegisterCallback<ValueCollectionCommand<C, T>>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Values!));
	}

	public void ReceiveValueArray<T>(string id, Action<T[]?>? callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		_currentQueue.RegisterCallback<ValueArrayCommand<T>>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Values));
	}

	public void ReceiveString(string id, Action<string?>? callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		_currentQueue.RegisterCallback<StringCommand>(_ownerId, id, (cmd) => callback?.Invoke(cmd.String));
	}

	[Obsolete("Use ReceiveStringCollection instead.")]
	public void ReceiveStringList(string id, Action<List<string>?>? callback)
	{
		ReceiveStringCollection(id, callback);
	}

	public void ReceiveStringCollection<C>(string id, Action<C>? callback) where C : ICollection<string>?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		_currentQueue.RegisterCallback<StringCollectionCommand<C>>(_ownerId, id, (cmd) => callback?.Invoke((C)cmd.Strings!));
	}

	public void ReceiveStringArray(string id, Action<string?[]?>? callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		_currentQueue.RegisterCallback<StringArrayCommand>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Strings));
	}

	public void ReceiveEmptyCommand(string id, Action? callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		_currentQueue.RegisterCallback<EmptyCommand>(_ownerId, id, (cmd) => callback?.Invoke());
	}

	public void ReceiveObject<T>(string id, Action<T>? callback) where T : class?, IMemoryPackable?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		_currentQueue.RegisterCallback<ObjectCommand<T>>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Object!));
	}

	[Obsolete("Use ReceiveObjectCollection instead.")]
	public void ReceiveObjectList<T>(string id, Action<List<T>>? callback) where T : class?, IMemoryPackable?, new()
	{
		ReceiveObjectCollection<List<T>, T>(id, callback);
	}

	public void ReceiveObjectCollection<C, T>(string id, Action<C>? callback) where C : ICollection<T>?, new() where T : class?, IMemoryPackable?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		_currentQueue.RegisterCallback<ObjectCollectionCommand<C, T>>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Objects!));
	}

	public void ReceiveObjectArray<T>(string id, Action<T[]>? callback) where T : class?, IMemoryPackable?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		_currentQueue.RegisterCallback<ObjectArrayCommand<T>>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Objects!));
	}

    public void Dispose()
    {
        _currentQueue.UnregisterOwner(_ownerId);
    }
}
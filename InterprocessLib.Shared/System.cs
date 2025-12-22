using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

internal class MessagingSystem : IDisposable
{
	private struct OwnerData
	{
		public readonly Dictionary<string, object?> ValueCallbacks = new();

		public readonly Dictionary<string, Action<string?>?> StringCallbacks = new();

		public readonly Dictionary<string, Action?> EmptyCallbacks = new();

		public readonly Dictionary<string, object?> ObjectCallbacks = new();

		public readonly Dictionary<string, object?> ValueArrayCallbacks = new();

		public readonly Dictionary<string, object?> ValueCollectionCallbacks = new();

		public readonly Dictionary<string, Action<string?[]?>?> StringArrayCallbacks = new();

		public readonly Dictionary<string, object?> StringCollectionCallbacks = new();

		public readonly Dictionary<string, object?> ObjectArrayCallbacks = new();

		public readonly Dictionary<string, object?> ObjectCollectionCallbacks = new();

		public readonly Dictionary<string, Action<Type?>?> TypeCallbacks = new();

		public OwnerData()
		{
		}
	}

	public bool IsAuthority { get; }

	public string QueueName { get; }

	public long QueueCapacity { get; }

	private MessagingManager? _primary;

	private static readonly MethodInfo _handleValueCommandMethod = typeof(MessagingSystem).GetMethod(nameof(HandleValueCommand), BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(HandleValueCommand));

	private static readonly MethodInfo _handleValueCollectionCommandMethod = typeof(MessagingSystem).GetMethod(nameof(HandleValueCollectionCommand), BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(HandleValueCollectionCommand));

	private static readonly MethodInfo _handleValueArrayCommandMethod = typeof(MessagingSystem).GetMethod(nameof(HandleValueArrayCommand), BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(HandleValueArrayCommand));

	private static readonly MethodInfo _handleObjectCommandMethod = typeof(MessagingSystem).GetMethod(nameof(HandleObjectCommand), BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(HandleObjectCommand));

	private static readonly MethodInfo _handleObjectCollectionCommandMethod = typeof(MessagingSystem).GetMethod(nameof(HandleObjectCollectionCommand), BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(HandleObjectCollectionCommand));

	private static readonly MethodInfo _handleObjectArrayCommandMethod = typeof(MessagingSystem).GetMethod(nameof(HandleObjectArrayCommand), BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(HandleObjectArrayCommand));

	private static readonly MethodInfo _handleStringCollectionCommandMethod = typeof(MessagingSystem).GetMethod(nameof(HandleStringCollectionCommand), BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(HandleStringCollectionCommand));

	private RenderCommandHandler? _onCommandReceived { get; }

	private Action<string>? _onWarning { get; }

	private Action<string>? _onDebug { get; }

	private Action<Exception>? _onFailure { get; }

	private readonly Dictionary<string, OwnerData> _ownerData = new();

	private Action? _postInitCallback;

	public bool IsConnected { get; private set; }

	public bool IsInitialized => _postInitActions is null;

	private List<Action>? _postInitActions = new();

	internal TypeManager OutgoingTypeManager; 

	internal TypeManager IncomingTypeManager; 

	private static readonly Dictionary<string, MessagingSystem> _backends = new();

	internal Action<PingCommand>? PingCallback;

	private readonly IMemoryPackerEntityPool _pool;

	private bool _messengerReadyCommandReceived = false;

	//private readonly CancellationTokenSource _cancel = new();

	internal void SetPostInitActions(List<Action>? actions)
	{
		if (IsInitialized)
			throw new InvalidOperationException("Already initialized!");

		_postInitActions = actions;
	}

	public void Connect()
	{
		if (IsConnected)
			throw new InvalidOperationException("Already connected!");

		_primary!.Connect(QueueName, IsAuthority, QueueCapacity);
		IsConnected = true;
	}

	public void Initialize()
	{
		if (IsInitialized)
			throw new InvalidOperationException("Already initialized!");

		SendPackable(new MessengerReadyCommand());

		var actions = _postInitActions!.ToArray();
		_postInitActions = null;

		foreach (var action in actions)
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				_onWarning?.Invoke($"Exception running post-init action:\n{ex}");
			}
		}

		_postInitCallback?.Invoke();
		_postInitCallback = null;
	}

	internal void RunPostInit(Action act)
	{
		if (!IsInitialized)
		{
			_postInitActions!.Add(act);
		}
		else
			throw new InvalidOperationException("Already initialized!");
	}

	public void RegisterOwner(string ownerName)
	{
		var ownerData = new OwnerData();
		_ownerData.Add(ownerName, ownerData);
	}

	public bool HasOwner(string ownerName)
	{
		return _ownerData.ContainsKey(ownerName);
	}

	public void RegisterValueCallback<T>(string owner, string id, Action<T>? callback) where T : unmanaged
	{
		_ownerData[owner].ValueCallbacks[id] = callback;
	}

	public void RegisterValueCollectionCallback<C, T>(string owner, string id, Action<C>? callback) where C : ICollection<T>, new() where T : unmanaged
	{
		_ownerData[owner].ValueCollectionCallbacks[id] = callback;
	}

	public void RegisterValueArrayCallback<T>(string owner, string id, Action<T[]>? callback) where T : unmanaged
	{
		_ownerData[owner].ValueArrayCallbacks[id] = callback;
	}

	public void RegisterStringCallback(string owner, string id, Action<string?>? callback)
	{
		_ownerData[owner].StringCallbacks[id] = callback;
	}

	public void RegisterStringArrayCallback(string owner, string id, Action<string?[]?>? callback)
	{
		_ownerData[owner].StringArrayCallbacks[id] = callback;
	}

	public void RegisterStringCollectionCallback<C>(string owner, string id, Action<C>? callback) where C : ICollection<string?>?, new()
	{
		_ownerData[owner].StringCollectionCallbacks[id] = callback;
	}

	public void RegisterEmptyCallback(string owner, string id, Action? callback)
	{
		_ownerData[owner].EmptyCallbacks[id] = callback;
	}

	public void RegisterObjectCallback<T>(string owner, string id, Action<T>? callback) where T : class?, IMemoryPackable?, new()
	{
		_ownerData[owner].ObjectCallbacks[id] = callback;
	}

	public void RegisterObjectArrayCallback<T>(string owner, string id, Action<T[]?>? callback) where T : class?, IMemoryPackable?, new()
	{
		_ownerData[owner].ObjectArrayCallbacks[id] = callback;
	}

	public void RegisterObjectCollectionCallback<C, T>(string owner, string id, Action<C?>? callback) where C : ICollection<T>?, new() where T : class?, IMemoryPackable?, new()
	{
		_ownerData[owner].ObjectCollectionCallbacks[id] = callback;
	}

	public void RegisterTypeCallback(string owner, string id, Action<Type?>? callback)
	{
		_ownerData[owner].TypeCallbacks[id] = callback;
	}

	public MessagingSystem(bool isAuthority, string queueName, long queueCapacity, IMemoryPackerEntityPool pool, RenderCommandHandler? commandHandler = null, Action<Exception>? failhandler = null, Action<string>? warnHandler = null, Action<string>? debugHandler = null, Action? postInitCallback = null)
	{
		if (queueName is null) throw new ArgumentNullException(nameof(queueName));
		if (pool is null) throw new ArgumentNullException(nameof(pool));

		IsAuthority = isAuthority;
		QueueName = queueName;
		QueueCapacity = queueCapacity;

		_onDebug = debugHandler;
		_onWarning = warnHandler;
		_onFailure = failhandler;
		_onCommandReceived = commandHandler;

		_postInitCallback = postInitCallback;

		_pool = pool;

		OutgoingTypeManager = new(_pool, OnOutgoingTypeRegistered);
		IncomingTypeManager = new(_pool, null);

		_primary = new MessagingManager(pool);
		_primary.CommandHandler = CommandHandler;
		_primary.FailureHandler = (ex) =>
		{
			if (ex is OperationCanceledException) return; // this happens when you call Dispose
			Dispose();
			_onFailure?.Invoke(ex);
		};
		_primary.WarningHandler = (msg) =>
		{
			_onWarning?.Invoke(msg);
		};

		_backends.Add(QueueName, this);

		// _primary.StartKeepAlive(2500);
		// _cancel.Token.Register(Dispose);
	}

	// private void OnKeepAlive()
	// {
	// 	_cancel.CancelAfter(5000);
	// }

	public void Dispose()
	{
		_primary?.Dispose();
		_primary = null;
		IsConnected = false;
	}

	internal static MessagingSystem? TryGetRegisteredSystem(string queueName)
	{
		if (_backends.TryGetValue(queueName, out var backend)) return backend;
		return null;
	}

	private void HandleValueCommand<T>(ValueCommand<T> command) where T : unmanaged
	{
		if (_ownerData[command.Owner!].ValueCallbacks.TryGetValue(command.Id!, out var callback))
		{
			if (callback != null)
			{
				((Action<T>)callback).Invoke(command.Value);
			}
		}
		else
		{
			_onWarning?.Invoke($"ValueCommand<{typeof(T).Name}> with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandleValueCollectionCommand<C, T>(ValueCollectionCommand<C, T> command) where C : ICollection<T>?, new() where T : unmanaged
	{
		if (_ownerData[command.Owner!].ValueCollectionCallbacks.TryGetValue(command.Id!, out var callback))
		{
			if (callback != null)
			{
				((Action<C?>)callback).Invoke(command.Values);
			}
		}
		else
		{
			_onWarning?.Invoke($"ValueCollectionCommand<{typeof(C).Name}, {typeof(T).Name}> with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandleValueArrayCommand<T>(ValueArrayCommand<T> command) where T : unmanaged
	{
		if (_ownerData[command.Owner!].ValueArrayCallbacks.TryGetValue(command.Id!, out var callback))
		{
			if (callback != null)
			{
				((Action<T[]?>)callback).Invoke(command.Values);
			}
		}
		else
		{
			_onWarning?.Invoke($"ValueArrayCommand<{typeof(T).Name}> with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandleStringCommand(StringCommand command)
	{
		if (_ownerData[command.Owner!].StringCallbacks.TryGetValue(command.Id!, out var callback))
		{
			if (callback != null)
			{
				callback.Invoke(command.String!);
			}
		}
		else
		{
			_onWarning?.Invoke($"StringCommand with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandleStringArrayCommand(StringArrayCommand command)
	{
		if (_ownerData[command.Owner!].StringArrayCallbacks.TryGetValue(command.Id!, out var callback))
		{
			if (callback != null)
			{
				callback.Invoke(command.Strings);
			}
		}
		else
		{
			_onWarning?.Invoke($"StringArrayCommand with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandleStringCollectionCommand<C>(StringCollectionCommand<C> command) where C : ICollection<string?>?, new()
	{
		if (_ownerData[command.Owner!].StringCollectionCallbacks.TryGetValue(command.Id!, out var callback))
		{
			if (callback != null)
			{
				((Action<C?>)callback).Invoke((C?)command.Strings);
			}
		}
		else
		{
			_onWarning?.Invoke($"StringArrayCommand with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandleEmptyCommand(EmptyCommand command)
	{
		if (_ownerData[command.Owner!].EmptyCallbacks.TryGetValue(command.Id!, out var callback))
		{
			if (callback != null)
			{
				callback.Invoke();
			}
		}
		else
		{
			_onWarning?.Invoke($"EmptyCommand with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandleObjectCommand<T>(ObjectCommand<T> command) where T : class?, IMemoryPackable?, new()
	{
		if (_ownerData[command.Owner!].ObjectCallbacks.TryGetValue(command.Id!, out var callback))
		{
			if (callback != null)
			{
				((Action<T?>)callback).Invoke((T?)command.UntypedObject);
			}
		}
		else
		{
			_onWarning?.Invoke($"ObjectCommand<{command.ObjectType.Name}> with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandleObjectArrayCommand<T>(ObjectArrayCommand<T> command) where T : class?, IMemoryPackable?, new()
	{
		if (_ownerData[command.Owner!].ObjectArrayCallbacks.TryGetValue(command.Id!, out var callback))
		{
			if (callback != null)
			{
				((Action<T[]?>)callback).Invoke(command.Objects);
			}
		}
		else
		{
			_onWarning?.Invoke($"ObjectArrayCommand<{typeof(T).Name}> with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandleObjectCollectionCommand<C, T>(ObjectCollectionCommand<C, T> command) where C : ICollection<T>?, new() where T : class?, IMemoryPackable?, new()
	{
		if (_ownerData[command.Owner!].ObjectCollectionCallbacks.TryGetValue(command.Id!, out var callback))
		{
			if (callback != null)
			{
				((Action<C?>)callback).Invoke(command.Objects);
			}
		}
		else
		{
			_onWarning?.Invoke($"ObjectCollectionCommand<{typeof(C).Name}, {typeof(T).Name}> with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandlePingCommand(PingCommand ping)
	{
		if (!ping.ReceivedTime.HasValue)
		{
			ping.ReceivedTime = DateTime.UtcNow;
			SendPackable(ping);
		}
		else
		{
			PingCallback?.Invoke(ping);
			PingCallback = null;
		}
	}

	private void HandleTypeCommand(TypeCommand command)
	{
		if (_ownerData[command.Owner!].TypeCallbacks.TryGetValue(command.Id!, out var callback))
		{
			if (callback != null)
			{
				callback.Invoke(command.Type);
			}
		}
		else
		{
			_onWarning?.Invoke($"IdentifiableTypeCommand with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void CommandHandler(RendererCommand command, int messageSize)
	{
		_onCommandReceived?.Invoke(command, messageSize);

		// if (command is KeepAlive)
		// {
		// 	OnKeepAlive();
		// 	return;
		// }

		IMemoryPackable? packable = null;
		if (command is WrapperCommand wrapperCommand)
		{
			packable = wrapperCommand.Packable;
			_onDebug?.Invoke($"{QueueName}: Received {packable?.ToString() ?? packable?.GetType().Name ?? "NULL"}");
		}
		else
		{
			_onWarning?.Invoke($"{QueueName}: Received an unexpected RendererCommand type! {command?.ToString() ?? command?.GetType().Name ?? "NULL"}");
			return;
		}

		// ping command before ready command is okay (to check if the queue is active)
		if (packable is PingCommand pingCommand)
		{
			HandlePingCommand(pingCommand);
			return;
		}

		if (packable is MessengerReadyCommand)
		{
			if (_messengerReadyCommandReceived)
			{
				OutgoingTypeManager = new(_pool, OnOutgoingTypeRegistered);
				IncomingTypeManager = new(_pool, null);
			}
			else
			{
				_messengerReadyCommandReceived = true;
			}
			return;
		}

		if (!_messengerReadyCommandReceived)
		{
			throw new InvalidDataException("MessengerReadyCommand needs to be first!");
		}

		if (packable is TypeRegistrationCommand typeRegCommand)
		{
			if (typeRegCommand.Type is not null)
			{
				IncomingTypeManager.InitDirectCommandType(typeRegCommand.Type);
			}
			else
			{
				throw new InvalidDataException("Other process tried to register a type that could not be found in this process!");
			}
			return;
		}

		if (packable is IdentifiableCommand identifiableCommand)
		{
			if (identifiableCommand.Owner is null) throw new InvalidDataException("Received IdentifiableCommand with null Owner!");
			if (identifiableCommand.Id is null) throw new InvalidDataException("Received IdentifiableCommand with null Id!");
			if (!_ownerData.TryGetValue(identifiableCommand.Owner, out var data))
			{
				_onWarning?.Invoke($"Owner \"{identifiableCommand.Owner}\" is not registered!");
				return;
			}
			if (packable is ValueCommand valueCommand)
			{
				var valueType = valueCommand.ValueType;
				var typedMethod = _handleValueCommandMethod.MakeGenericMethod(valueType);
				typedMethod.Invoke(this, [packable]);
			}
			else if (packable is ObjectCommand objectCommand)
			{
				var objectType = objectCommand.ObjectType;
				var typedMethod = _handleObjectCommandMethod.MakeGenericMethod(objectType);
				typedMethod.Invoke(this, [packable]);
			}
			else if (packable is EmptyCommand emptyCommand)
			{
				HandleEmptyCommand(emptyCommand);
			}
			else if (packable is StringCommand stringCommand)
			{
				HandleStringCommand(stringCommand);
			}
			else if (packable is StringArrayCommand stringArrayCommand)
			{
				HandleStringArrayCommand(stringArrayCommand);
			}
			else if (packable is TypeCommand typeCommand)
			{
				HandleTypeCommand(typeCommand);
			}
			else if (packable is CollectionCommand collectionCommand)
			{
				var collectionType = collectionCommand.CollectionType;
				var innerDataType = collectionCommand.StoredType;
				if (innerDataType == typeof(string))
				{
					var typedMethod = _handleStringCollectionCommandMethod.MakeGenericMethod(collectionType);
					typedMethod.Invoke(this, [packable]);
				}
				else if (innerDataType.IsValueType)
				{
					if (collectionType.IsArray)
					{
						var typedMethod = _handleValueArrayCommandMethod.MakeGenericMethod(innerDataType);
						typedMethod.Invoke(this, [packable]);
					}
					else
					{
						var typedMethod = _handleValueCollectionCommandMethod.MakeGenericMethod(collectionType, innerDataType);
						typedMethod.Invoke(this, [packable]);
					}
				}
				else
				{
					if (collectionType.IsArray)
					{
						var typedMethod = _handleObjectArrayCommandMethod.MakeGenericMethod(innerDataType);
						typedMethod.Invoke(this, [packable]);
					}
					else
					{
						var typedMethod = _handleObjectCollectionCommandMethod.MakeGenericMethod(collectionType, innerDataType);
						typedMethod.Invoke(this, [packable]);
					}
				}
			}
			else
			{
				throw new InvalidDataException($"Received unrecognized IdentifiableCommand of type {identifiableCommand.GetType().Name}: {identifiableCommand.Owner}:{identifiableCommand.Id}");
			}
		}
		else
		{
			// packable is not identifiable, has no owner
			// right now this should never happen
			// but in the future maybe it can be handled with a custom user-supplied callback
		}
	}

	public void SendPackable(IMemoryPackable packable)
	{
		if (packable is null) throw new ArgumentNullException(nameof(packable));

		if (!IsConnected) throw new InvalidOperationException("Not connected!");

		_onDebug?.Invoke($"Sending packable: {packable}");

		var wrapper = new WrapperCommand();
		wrapper.QueueName = QueueName;
		wrapper.Packable = packable;

		_primary!.SendCommand(wrapper);
	}

	public void SendRendererCommand(RendererCommand command)
	{
		if (command is null) throw new ArgumentNullException(nameof(command));

		if (!IsConnected) throw new InvalidOperationException("Not connected!");

		_onDebug?.Invoke($"Sending RendererCommand: {command}");

		_primary!.SendCommand(command);
	}

	internal void EnsureValueTypeInitialized<T>() where T : unmanaged
	{
		if (!OutgoingTypeManager.IsValueTypeInitialized<T>())
		{
			OutgoingTypeManager.RegisterAdditionalValueType<T>();
		}
	}

	internal void EnsureObjectTypeInitialized<T>() where T : class?, IMemoryPackable?, new()
	{
		if (!OutgoingTypeManager.IsObjectTypeInitialized<T>())
		{
			OutgoingTypeManager.RegisterAdditionalObjectType<T>();
		}
	}

	private void OnOutgoingTypeRegistered(Type type)
	{
		var typeRegCommand = new TypeRegistrationCommand();
		typeRegCommand.Type = type;
		SendPackable(typeRegCommand);
	}

	public void UnregisterOwner(string ownerId)
	{
		if (HasOwner(ownerId))
		{
			_ownerData.Remove(ownerId);
		}
		else
		{
			_onWarning?.Invoke($"Tried to unregister owner that was not registered: {ownerId}");
		}
		if (_ownerData.Count == 0)
		{
			Dispose();
		}
	}
}
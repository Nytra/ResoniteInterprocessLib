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

		public readonly Dictionary<string, object?> ValueCollectionCallbacks = new();

		// not List<string?>, because FrooxEngine just takes List<string>
		public readonly Dictionary<string, Action<List<string>?>?> StringListCallbacks = new();

		//public readonly Dictionary<string, object?> ObjectListCallbacks = new();

		//public readonly Dictionary<string, object?> ObjectArrayCallbacks = new();

		public readonly Dictionary<string, object?> ObjectCollectionCallbacks = new();

		public OwnerData()
		{
		}
	}

	public bool IsAuthority { get; }

	public string QueueName { get; }

	public long QueueCapacity { get; }

	private MessagingManager _primary;

	private static MethodInfo? _handleValueCommandMethod = typeof(MessagingSystem).GetMethod(nameof(HandleValueCommand), BindingFlags.Instance | BindingFlags.NonPublic);

	private static MethodInfo? _handleValueCollectionCommandMethod = typeof(MessagingSystem).GetMethod(nameof(HandleValueCollectionCommand), BindingFlags.Instance | BindingFlags.NonPublic);

	private static MethodInfo? _handleObjectCommandMethod = typeof(MessagingSystem).GetMethod(nameof(HandleObjectCommand), BindingFlags.Instance | BindingFlags.NonPublic);

	private static MethodInfo? _handleObjectListCommandMethod = typeof(MessagingSystem).GetMethod(nameof(HandleObjectListCommand), BindingFlags.Instance | BindingFlags.NonPublic);

	private RenderCommandHandler? _onCommandReceived { get; }

	private Action<string>? _onWarning { get; }

	private Action<string>? _onDebug { get; }

	private Action<Exception>? _onFailure { get; }

	private Dictionary<string, OwnerData> _ownerData = new();

	private Action? _postInitCallback;

	public bool IsConnected { get; private set; }

	public bool IsInitialized => _postInitActions is null;

	private List<Action>? _postInitActions = new();

	internal TypeManager TypeManager; 

	private static Dictionary<string, MessagingSystem> _backends = new();

	private IMemoryPackerEntityPool _pool;

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

		_primary.Connect(QueueName, IsAuthority, QueueCapacity);
		IsConnected = true;

		if (!IsAuthority)
			Initialize();
	}

	private void Initialize()
	{
		if (IsInitialized)
			throw new InvalidOperationException("Already initialized!");

		if (!IsAuthority)
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

	public void RegisterValueCallback<T>(string owner, string id, Action<T> callback) where T : unmanaged
	{
		_ownerData[owner].ValueCallbacks[id] = callback;
	}

	public void RegisterValueCollectionCallback<C, T>(string owner, string id, Action<C> callback) where C : ICollection<T>, new() where T : unmanaged
	{
		_ownerData[owner].ValueCollectionCallbacks[id] = callback;
	}

	public void RegisterValueArrayCallback<T>(string owner, string id, Action<T[]> callback) where T : unmanaged
	{
		_ownerData[owner].ValueCollectionCallbacks[id] = callback;
	}

	public void RegisterStringCallback(string owner, string id, Action<string?> callback)
	{
		_ownerData[owner].StringCallbacks[id] = callback;
	}

	public void RegisterStringListCallback(string owner, string id, Action<List<string>?>? callback)
	{
		_ownerData[owner].StringListCallbacks[id] = callback;
	}

	public void RegisterEmptyCallback(string owner, string id, Action callback)
	{
		_ownerData[owner].EmptyCallbacks[id] = callback;
	}

	public void RegisterObjectCallback<T>(string owner, string id, Action<T> callback) where T : class, IMemoryPackable, new()
	{
		_ownerData[owner].ObjectCallbacks[id] = callback;
	}

	public void RegisterObjectListCallback<T>(string owner, string id, Action<List<T>> callback) where T : class, IMemoryPackable, new()
	{
		_ownerData[owner].ObjectCollectionCallbacks[id] = callback;
	}

	public void RegisterObjectArrayCallback<T>(string owner, string id, Action<T[]> callback) where T : class, IMemoryPackable, new()
	{
		_ownerData[owner].ObjectCollectionCallbacks[id] = callback;
	}

	public MessagingSystem(bool isAuthority, string queueName, long queueCapacity, IMemoryPackerEntityPool pool, RenderCommandHandler? commandHandler = null, Action<Exception>? failhandler = null, Action<string>? warnHandler = null, Action<string>? debugHandler = null, Action? postInitCallback = null)
	{
		IsAuthority = isAuthority;
		QueueName = queueName;
		QueueCapacity = queueCapacity;

		_onDebug = debugHandler;
		_onWarning = warnHandler;
		_onFailure = failhandler;
		_onCommandReceived = commandHandler;

		_postInitCallback = postInitCallback;

		TypeManager = new(QueueName, pool);

		_pool = pool;

		_primary = new MessagingManager(pool);
		_primary.CommandHandler = CommandHandler;
		_primary.FailureHandler = (ex) => 
		{
			IsConnected = false;
			_onFailure?.Invoke(ex);
		};
		_primary.WarningHandler = (msg) =>
		{
			_onWarning?.Invoke(msg);
		};

		_backends.Add(QueueName, this);
	}

	public void Dispose()
	{
		_primary.Dispose();
	}

	internal static MessagingSystem? TryGetRegisteredSystem(string queueName)
	{
		if (_backends.TryGetValue(queueName, out var backend)) return backend;
		return null;
	}

	private void HandleValueCommand<T>(ValueCommand<T> command) where T : unmanaged
	{
		if (_ownerData[command.Owner].ValueCallbacks.TryGetValue(command.Id, out var callback))
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

	private void HandleValueCollectionCommand<C, T>(ValueCollectionCommand<C, T> command) where C : ICollection<T>, new() where T : unmanaged
	{
		if (_ownerData[command.Owner].ValueCollectionCallbacks.TryGetValue(command.Id, out var callback))
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

	private void HandleStringCommand(StringCommand command)
	{
		if (_ownerData[command.Owner].StringCallbacks.TryGetValue(command.Id, out var callback))
		{
			if (callback != null)
			{
				callback.Invoke(command.String);
			}
		}
		else
		{
			_onWarning?.Invoke($"StringCommand with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandleStringListCommand(StringListCommand command)
	{
		if (_ownerData[command.Owner].StringListCallbacks.TryGetValue(command.Id, out var callback))
		{
			if (callback != null)
			{
				callback.Invoke(command.Values);
			}
		}
		else
		{
			_onWarning?.Invoke($"StringListCommand with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandleEmptyCommand(EmptyCommand command)
	{
		if (_ownerData[command.Owner].EmptyCallbacks.TryGetValue(command.Id, out var callback))
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

	private void HandleObjectCommand<T>(ObjectCommand<T> command) where T : class, IMemoryPackable, new()
	{
		if (_ownerData[command.Owner].ObjectCallbacks.TryGetValue(command.Id, out var callback))
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

	private void HandleObjectListCommand<T>(ObjectListCommand<T> command) where T : class, IMemoryPackable, new()
	{
		if (_ownerData[command.Owner].ObjectCollectionCallbacks.TryGetValue(command.Id, out var callback))
		{
			if (callback != null)
			{
				((Action<List<T>?>)callback).Invoke(command.Objects);
			}
		}
		else
		{
			_onWarning?.Invoke($"ObjectListCommand<{typeof(T).Name}> with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandleObjectArrayCommand<T>(ObjectArrayCommand<T> command) where T : class, IMemoryPackable, new()
	{
		if (_ownerData[command.Owner].ObjectCollectionCallbacks.TryGetValue(command.Id, out var callback))
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

	private void CommandHandler(RendererCommand command, int messageSize)
	{
		_onCommandReceived?.Invoke(command, messageSize);

		IMemoryPackable? packable = null;
		if (command is WrapperCommand wrapperCommand)
		{
			packable = wrapperCommand.Packable;
			_onDebug?.Invoke($"Received {packable?.ToString() ?? packable?.GetType().Name ?? "NULL"}");
		}
		else
		{
			_onWarning?.Invoke($"Received an unexpected RendererCommand type! {command?.ToString() ?? command?.GetType().Name ?? "NULL"}");
			return;
		}

		if (!IsInitialized)
		{
			if (packable is MessengerReadyCommand)
			{
				Initialize();
				return;
			}
			else
			{
				throw new InvalidDataException($"The first command needs to be the MessengerReadyCommand when not initialized!");
			}
		}

		if (packable is IdentifiableCommand identifiableCommand)
		{
			if (!_ownerData.TryGetValue(identifiableCommand.Owner, out var data))
			{
				_onWarning?.Invoke($"Owner \"{identifiableCommand.Owner}\" is not registered!");
				return;
			}
			if (packable is ValueCommand valueCommand)
			{
				var valueType = valueCommand.ValueType;
				var typedMethod = _handleValueCommandMethod!.MakeGenericMethod(valueType);
				typedMethod.Invoke(this, [packable]);
			}
			else if (packable is CollectionCommand collectionCommand)
			{
				var innerDataType = collectionCommand.InnerDataType;
				if (innerDataType == typeof(string))
				{
					HandleStringListCommand((StringListCommand)collectionCommand);
				}
				else if (innerDataType.IsValueType)
				{
					var collectionType = collectionCommand.CollectionType;
					var typedMethod = _handleValueCollectionCommandMethod!.MakeGenericMethod(collectionType, innerDataType);
					typedMethod.Invoke(this, [packable]);
				}
				else
				{
					var typedMethod = _handleObjectListCommandMethod!.MakeGenericMethod(innerDataType);
					typedMethod.Invoke(this, [packable]);
				}
			}
			else if (packable is ObjectCommand objectCommand)
			{
				var objectType = objectCommand.ObjectType;
				var typedMethod = _handleObjectCommandMethod!.MakeGenericMethod(objectType);
				typedMethod.Invoke(this, [packable]);
			}
			else
			{
				switch (packable)
				{
					case StringCommand:
						HandleStringCommand((StringCommand)packable);
						break;
					case EmptyCommand:
						HandleEmptyCommand((EmptyCommand)packable);
						break;
					case IdentifiableCommand unknownCommand:
						_onWarning?.Invoke($"Received unrecognized IdentifiableCommand of type {unknownCommand.GetType().Name}: {unknownCommand.Owner}:{unknownCommand.Id}");
						break;
					default:
						break;
				}
			}
		}
		else
		{
			// packable is not identifiable, has no owner
			// right now this should never happen
			// but in the future maybe it can be handled with a custom user-supplied callback
		}
	}

	public void SendPackable(IMemoryPackable? packable)
	{
		_onDebug?.Invoke($"Sending packable: {packable?.ToString() ?? packable?.GetType().Name ?? "NULL"}");

		var wrapper = new WrapperCommand();
		wrapper.QueueName = QueueName;
		wrapper.Packable = packable;

		_primary.SendCommand(wrapper);
	}
}
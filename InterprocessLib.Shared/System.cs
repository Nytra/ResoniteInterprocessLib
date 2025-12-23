using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

internal class MessagingSystem : IDisposable
{
	private struct OwnerData
	{
		public readonly Dictionary<string, object?> IdentifiableCallbacks = new();

		public OwnerData()
		{
		}
	}
	public bool IsAuthority { get; }

	public string QueueName { get; }

	public long QueueCapacity { get; }

	private MessagingManager? _primary;

	private static readonly MethodInfo _handleIdentifiableCommandMethod = typeof(MessagingSystem).GetMethod(nameof(HandleIdentifiableCommand), BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(HandleIdentifiableCommand));

	private static readonly MethodInfo _handlePackableMethod = typeof(MessagingSystem).GetMethod(nameof(HandlePackable), BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(HandlePackable));

	private Action<string>? _onWarning { get; }

	private Action<string>? _onDebug { get; }

	private Action<Exception>? _onFailure { get; }

	private readonly Dictionary<string, OwnerData> _ownerData = new();

	private readonly Dictionary<Type, object?> _typedCallbacks = new();

	private Action? _postInitCallback;

	public bool IsConnected { get; private set; }

	public bool IsInitialized => _postInitActions is null;

	private List<Action>? _postInitActions = new();

	internal TypeManager OutgoingTypeManager; 

	internal TypeManager IncomingTypeManager; 

	private static readonly Dictionary<string, MessagingSystem> _backends = new();

	private readonly IMemoryPackerEntityPool _pool;

	private bool _messengerReadyCommandReceived = false;

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
		_ownerData.Add(ownerName, new());
	}

	public bool HasOwner(string ownerName)
	{
		return _ownerData.ContainsKey(ownerName);
	}

	public void RegisterCallback<T>(string owner, string id, Action<T>? callback) where T : IdentifiableCommand
	{
		_ownerData[owner].IdentifiableCallbacks[id] = callback;
	}

	public void RegisterCallback<T>(Action<T>? callback) where T : class, IMemoryPackable, new()
	{
		_typedCallbacks[typeof(T)] = callback;
	}

	public MessagingSystem(bool isAuthority, string queueName, long queueCapacity, IMemoryPackerEntityPool pool, Action<Exception>? failhandler = null, Action<string>? warnHandler = null, Action<string>? debugHandler = null, Action? postInitCallback = null)
	{
		if (queueName is null) throw new ArgumentNullException(nameof(queueName));
		if (pool is null) throw new ArgumentNullException(nameof(pool));

		IsAuthority = isAuthority;
		QueueName = queueName;
		QueueCapacity = queueCapacity;

		_onDebug = debugHandler;
		_onWarning = warnHandler;
		_onFailure = failhandler;

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
	}

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

	private void HandleIdentifiableCommand<T>(T cmd) where T : IdentifiableCommand
	{
		if (_ownerData[cmd.Owner!].IdentifiableCallbacks.TryGetValue(cmd.Id!, out var callback))
		{
			if (callback != null)
			{
				((Action<T>)callback).Invoke(cmd);
			}
		}
		else
		{
			_onWarning?.Invoke($"IdentifiableCommand of type {cmd.GetType().Name} with Id \"{cmd.Id}\" and Owner \"{cmd.Owner}\" is not registered to receive a callback!");
		}
	}

	private void HandlePackable<T>(T obj) where T : class, IMemoryPackable, new()
	{
		if (_typedCallbacks.TryGetValue(typeof(T), out var data))
		{
			var callback = (Action<T>?)_typedCallbacks[typeof(T)];
			callback?.Invoke(obj);
		}
		else
		{
			_onWarning?.Invoke($"Packable of type \"{typeof(T)}\" is not registered to receive a callback!");
		}
	}

	private void CommandHandler(RendererCommand command, int messageSize)
	{
		if (command is WrapperCommand wrapperCommand)
		{
			IMemoryPackable packable = wrapperCommand.Packable!;
			_onDebug?.Invoke($"{QueueName}: Received {packable}");

			if (packable is PingCommand ping)
			{
				if (!ping.Received)
				{
					ping.Received = true;
					SendPackable(ping);
				}
				else
				{
					HandlePackable(ping);
				}
			}
			else if (packable is MessengerReadyCommand)
			{
				if (_messengerReadyCommandReceived)
				{
					_onWarning?.Invoke("Received another MessengerReadyCommand! Registered types will be reset!");
					OutgoingTypeManager = new(_pool, OnOutgoingTypeRegistered);
					IncomingTypeManager = new(_pool, null);
					SendPackable(new MessengerReadyCommand());
				}
				else
				{
					_messengerReadyCommandReceived = true;
				}
			}
			else if (!_messengerReadyCommandReceived)
			{
				throw new InvalidDataException("MessengerReadyCommand needs to be first!");
			}
			else if (packable is TypeRegistrationCommand typeRegCommand)
			{
				if (typeRegCommand.Type is not null)
				{
					IncomingTypeManager.InvokeRegisterType(typeRegCommand.Type);
				}
				else
				{
					throw new InvalidDataException("Other process tried to register a type that could not be found in this process!");
				}
			}
			else if (packable is IdentifiableCommand identifiableCommand)
			{
				if (identifiableCommand.Owner is null) throw new InvalidDataException("Received IdentifiableCommand with null Owner!");
				if (identifiableCommand.Id is null) throw new InvalidDataException("Received IdentifiableCommand with null Id!");
				if (_ownerData.TryGetValue(identifiableCommand.Owner, out var data))
				{
					_handleIdentifiableCommandMethod.MakeGenericMethod(identifiableCommand.GetType()).Invoke(this, [identifiableCommand]);
				}
				else
				{
					_onWarning?.Invoke($"Owner \"{identifiableCommand.Owner}\" is not registered!");
				}
			}
			else
			{
				_handlePackableMethod.MakeGenericMethod(packable.GetType()).Invoke(this, [packable]);
			}
		}
		else
		{
			_onWarning?.Invoke($"{QueueName}: Received an unexpected RendererCommand! {command}");
		}
	}

	public void SendPackable<T>(T packable) where T : class, IMemoryPackable, new()
	{
		if (packable is null) throw new ArgumentNullException(nameof(packable));

		if (!IsConnected) throw new InvalidOperationException("Not connected!");

		_onDebug?.Invoke($"Sending: {packable}");

		if (!OutgoingTypeManager.IsTypeRegistered<T>())
		{
			OutgoingTypeManager.RegisterType<T>();
		}

		var wrapper = new WrapperCommand();
		wrapper.QueueName = QueueName;
		wrapper.Packable = packable;

		_primary!.SendCommand(wrapper);
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
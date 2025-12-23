using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

internal class MessagingSystem : IDisposable
{
	private struct OwnerData
	{
		public readonly Dictionary<Type, Dictionary<string, object?>> TypedCallbacks = new();

		public OwnerData()
		{
		}
	}
	public bool IsAuthority { get; }

	public string QueueName { get; }

	public long QueueCapacity { get; }

	public bool IsConnected { get; private set; }

	public bool IsInitialized { get; private set; }

	private MessagingManager? _primary;

	private static readonly MethodInfo _handlePackableMethod = typeof(MessagingSystem).GetMethod(nameof(HandlePackable), BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(nameof(HandlePackable));

	private Action<string>? _onWarning;

	private Action<string>? _onDebug;

	private Action<Exception>? _onFailure;

	private readonly Dictionary<string, OwnerData> _ownerData = new();

	internal TypeManager OutgoingTypeManager; 

	internal TypeManager IncomingTypeManager; 

	private static readonly Dictionary<string, MessagingSystem> _registeredSystems = new();

	private readonly IMemoryPackerEntityPool _pool;

	private bool _messengerReadyCommandReceived = false;

	private string LogPrefix
	{
		get
		{
#if DEBUG
			return $"{QueueName}: ";
#else
			return "";
#endif
		}
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
		if (!IsConnected)
			throw new InvalidOperationException("Not connected!");

		if (IsInitialized)
			throw new InvalidOperationException("Already initialized!");

		SendPackable(new MessengerReadyCommand());

		IsInitialized = true;
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
		if(!_ownerData[owner].TypedCallbacks.ContainsKey(typeof(T)))
			_ownerData[owner].TypedCallbacks.Add(typeof(T), new());

		_ownerData[owner].TypedCallbacks[typeof(T)][id] = callback;
	}

	public MessagingSystem(bool isAuthority, string queueName, long queueCapacity, IMemoryPackerEntityPool pool, Action<Exception>? failhandler = null, Action<string>? warnHandler = null, Action<string>? debugHandler = null)
	{
		if (queueName is null) throw new ArgumentNullException(nameof(queueName));
		if (pool is null) throw new ArgumentNullException(nameof(pool));

		IsAuthority = isAuthority;
		QueueName = queueName;
		QueueCapacity = queueCapacity;

		_onDebug = debugHandler;
		_onWarning = warnHandler;
		_onFailure = failhandler;

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

		_registeredSystems.Add(QueueName, this);
	}

	public void Dispose()
	{
		_primary?.Dispose();
		_primary = null;
		IsConnected = false;
	}

	internal static MessagingSystem? TryGetRegisteredSystem(string queueName)
	{
		if (_registeredSystems.TryGetValue(queueName, out var system)) return system;
		return null;
	}

	private void HandlePackable<T>(string owner, string id, T obj) where T : class, IMemoryPackable, new()
	{
		if (_ownerData[owner].TypedCallbacks.TryGetValue(typeof(T), out var data) && data.ContainsKey(id))
		{
			var callback = (Action<T>?)data[id];
			callback?.Invoke(obj);
		}
		else
		{
			_onWarning?.Invoke($"{LogPrefix}Packable of type {typeof(T)} is not registered to receive a callback!");
		}
	}

	private void CommandHandler(RendererCommand command, int messageSize)
	{
		//while (!IsInitialized)
			//Thread.Sleep(1);

		if (command is WrapperCommand wrapperCommand)
		{
			IMemoryPackable packable = wrapperCommand.Packable!;
			_onDebug?.Invoke($"{LogPrefix}Received {packable}");

			if (packable is PingCommand ping)
			{
				if (!ping.Received)
				{
					ping.Received = true;
					SendPackable(ping);
				}
				HandlePackable(ping.Owner!, ping.Id!, ping);
			}
			else if (packable is MessengerReadyCommand)
			{
				if (_messengerReadyCommandReceived)
				{
					_onDebug?.Invoke($"{LogPrefix}Received additional MessengerReadyCommand! Registered types will be reset!");
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
					_onDebug?.Invoke($"{LogPrefix}* Registering incoming type: {typeRegCommand.Type.Name}<{string.Join(",", (IEnumerable<Type>)typeRegCommand.Type.GenericTypeArguments)}>");
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
					_handlePackableMethod.MakeGenericMethod(identifiableCommand.GetType()).Invoke(this, [identifiableCommand.Owner, identifiableCommand.Id, identifiableCommand]);
				}
				else
				{
					_onWarning?.Invoke($"{LogPrefix}Owner \"{identifiableCommand.Owner}\" is not registered in this process!");
				}
			}
			else
			{
				_onWarning?.Invoke($"{LogPrefix}Received a packable that can't be identified! {packable}");
			}
		}
		else
		{
			_onWarning?.Invoke($"{LogPrefix}Received an unexpected RendererCommand! {command}");
		}
	}

	public void SendPackable<T>(T packable) where T : class, IMemoryPackable, new()
	{
		if (packable is null) throw new ArgumentNullException(nameof(packable));

		if (!IsConnected) throw new InvalidOperationException("Not connected!");

		_onDebug?.Invoke($"{LogPrefix}Sending: {packable}");

		if (!OutgoingTypeManager.IsTypeRegistered<T>())
		{
			_onDebug?.Invoke($"{LogPrefix}* Registering outgoing type: {typeof(T).Name}<{string.Join(",", (IEnumerable<Type>)typeof(T).GenericTypeArguments)}>");
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
			_onWarning?.Invoke($"{LogPrefix}Tried to unregister owner that was not registered: {ownerId}");
		}
		if (_ownerData.Count == 0)
		{
			Dispose();
		}
	}
}
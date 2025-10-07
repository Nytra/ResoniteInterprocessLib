using Renderite.Shared;

namespace InterprocessLib;

/// <summary>
/// Simple interprocess messaging API.
/// </summary>
public class Messenger
{
	private static MessagingSystem? _defaultSystem;

	private MessagingSystem? _customSystem;

	private MessagingSystem? CurrentSystem => _customSystem ?? _defaultSystem;

	/// <summary>
	/// If this messenger has a underlying messaging system assigned to it, or has it not been created yet
	/// </summary>
	public bool HasSystem => CurrentSystem is not null;

	/// <summary>
	/// If true the messenger will send commands immediately, otherwise commands will wait in a queue until the non-authority process sends the <see cref="MessengerReadyCommand"/>.
	/// </summary>
	private bool IsInitialized => CurrentSystem?.IsInitialized ?? false;

	/// <summary>
	/// Does this process have authority over the other process.
	/// </summary>
	public bool IsAuthority => CurrentSystem?.IsAuthority ?? false;

	/// <summary>
	/// Is the interprocess connection still available
	/// </summary>
	public bool IsConnected => CurrentSystem?.IsConnected ?? false;

	internal static bool DefaultInitStarted = false;

	/// <summary>
	/// Called when the backend connection has a critical error
	/// </summary>
	public static Action<Exception>? OnFailure;

	/// <summary>
	/// Called when something potentially bad/unexpected happens
	/// </summary>
	public static Action<string>? OnWarning;

	/// <summary>
	/// Called with additional debugging information
	/// </summary>
#pragma warning disable CS0649
	public static Action<string>? OnDebug;
#pragma warning restore

	internal static List<Action>? _defaultPostInitActions = new();

	private string _ownerId;

	//private static HashSet<string> _defaultBackendRegisteredOwnerIds = new();

	private List<Type>? _additionalObjectTypes;

	private List<Type>? _additionalValueTypes;

	/// <summary>
	/// Creates an instance with a unique owner
	/// </summary>
	/// <param name="ownerId">Unique identifier for this instance in this process. Should match the other process.</param>
	/// <param name="additionalObjectTypes">Optional list of additional <see cref="IMemoryPackable"/> class types you want to be able to send or receieve. Types you want to use that are vanilla go in here too.</param>
	/// <param name="additionalValueTypes">Optional list of additional unmanaged types you want to be able to send or receieve.</param>
	/// <exception cref="ArgumentNullException"></exception>
	/// <exception cref="EntryPointNotFoundException"></exception>
	public Messenger(string ownerId, List<Type>? additionalObjectTypes = null, List<Type>? additionalValueTypes = null)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		_ownerId = ownerId;

		_additionalObjectTypes = additionalObjectTypes;

		_additionalValueTypes = additionalValueTypes;

		if (_defaultSystem is null)
		{
			DefaultRunPostInit(() =>
			{
				Register();
			});
		}
		else
		{
			Register();
		}

		if (_defaultSystem is null && !DefaultInitStarted)
		{
			var frooxEngineInitType = Type.GetType("InterprocessLib.FrooxEngineInit");
			if (frooxEngineInitType is not null)
			{
				frooxEngineInitType.GetMethod("Init", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.Invoke(null, null);
			}
			else
			{
				var unityInitType = Type.GetType("InterprocessLib.UnityInit");
				if (unityInitType is not null)
				{
					unityInitType.GetMethod("Init", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.Invoke(null, null);
				}
				else
				{
					throw new EntryPointNotFoundException("Could not find InterprocessLib initialization type!");
				}
			}
		}
	}

	internal static void SetDefaultSystem(MessagingSystem system)
	{
		_defaultSystem = system;
		_defaultSystem.SetPostInitActions(_defaultPostInitActions);
		_defaultPostInitActions = null;
	}

	/// <summary>
	/// Creates an instance with a unique owner and a custom backend that can connect to any process
	/// </summary>
	/// <param name="ownerId">Unique identifier for this instance in this process. Should match the other process.</param>
	/// /// <param name="customBackend">Custom messaging backend. Allows connecting to any custom process.</param>
	/// <param name="additionalObjectTypes">Optional list of additional <see cref="IMemoryPackable"/> class types you want to be able to send or receieve. Types you want to use that are vanilla go in here too.</param>
	/// <param name="additionalValueTypes">Optional list of additional unmanaged types you want to be able to send or receieve.</param>
	/// <exception cref="ArgumentNullException"></exception>
	public Messenger(string ownerId, bool isAuthority, string queueName, long queueCapacity, IMemoryPackerEntityPool pool, List<Type>? additionalObjectTypes = null, List<Type>? additionalValueTypes = null)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		if (MessagingSystem.TryGet(queueName) is not MessagingSystem existingSystem)
		{
			_customSystem = new MessagingSystem(isAuthority, queueName, queueCapacity, pool, null, OnFailure, OnWarning, OnDebug);
		}
		else
		{
			_customSystem = existingSystem;
		}

		_ownerId = ownerId;

		_additionalObjectTypes = additionalObjectTypes;

		_additionalValueTypes = additionalValueTypes;

		Register();

		if (!_customSystem!.IsConnected)
			_customSystem.Connect();
	}

	private void RunPostInit(Action act)
	{
		if (CurrentSystem is null)
			DefaultRunPostInit(act);
		else
			CurrentSystem.RunPostInit(act);
	}

	private void Register()
	{
		if (CurrentSystem!.HasOwner(_ownerId))
		{
			OnWarning?.Invoke($"Owner {_ownerId} has already been registered in this process for messaging backend with queue name: {CurrentSystem.QueueName}");
		}
		else
			CurrentSystem.RegisterOwner(_ownerId);

		if (_additionalObjectTypes is not null)
		{
			CurrentSystem.TypeManager.InitObjectTypeList(_additionalObjectTypes.Where(t => !CurrentSystem.TypeManager.IsObjectTypeInitialized(t)).ToList());
		}
		if (_additionalValueTypes is not null)
		{
			CurrentSystem.TypeManager.InitValueTypeList(_additionalValueTypes.Where(t => !CurrentSystem.TypeManager.IsValueTypeInitialized(t)).ToList());
		}
	}

	private static void DefaultRunPostInit(Action act)
	{
		if (_defaultSystem is null)
		{
			_defaultPostInitActions!.Add(act);
		}
		else
			throw new InvalidOperationException("Default host already initialized!");
	}

	public void SendValue<T>(string id, T value) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendValue(id, value));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {value.GetType().Name} needs to be registered first!");

		var command = new ValueCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Value = value;
		CurrentSystem!.SendPackable(command);
	}

	public void SendValueList<T>(string id, List<T> list) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendValueList(id, list));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ValueCollectionCommand<List<T>, T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = list;
		CurrentSystem!.SendPackable(command);
	}

	public void SendValueHashSet<T>(string id, HashSet<T> hashSet) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendValueHashSet(id, hashSet));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ValueCollectionCommand<HashSet<T>, T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = hashSet;
		CurrentSystem!.SendPackable(command);
	}

	public void SendString(string id, string str)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendString(id, str));
			return;
		}

		var command = new StringCommand();
		command.Owner = _ownerId;
		command.Id = id;
		command.String = str;
		CurrentSystem!.SendPackable(command);
	}

	public void SendStringList(string id, List<string> list)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendStringList(id, list));
			return;
		}

		var command = new StringListCommand();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = list;
		CurrentSystem!.SendPackable(command);
	}

	public void SendEmptyCommand(string id)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendEmptyCommand(id));
			return;
		}

		var command = new IdentifiableCommand();
		command.Owner = _ownerId;
		command.Id = id;
		CurrentSystem!.SendPackable(command);
	}

	public void SendObject<T>(string id, T? obj) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendObject(id, obj));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var wrapper = new ObjectCommand<T>();
		wrapper.Object = obj;
		wrapper.Owner = _ownerId;
		wrapper.Id = id;

		CurrentSystem!.SendPackable(wrapper);
	}

	public void SendObjectList<T>(string id, List<T> list) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendObjectList(id, list));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ObjectListCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = list;
		CurrentSystem!.SendPackable(command);
	}

	public void ReceiveValue<T>(string id, Action<T> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveValue(id, callback));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		CurrentSystem!.RegisterValueCallback(_ownerId, id, callback);
	}

	public void ReceiveValueList<T>(string id, Action<List<T>> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveValueList(id, callback));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		CurrentSystem!.RegisterValueCollectionCallback<List<T>, T>(_ownerId, id, callback);
	}

	public void ReceiveValueHashSet<T>(string id, Action<HashSet<T>> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveValueHashSet(id, callback));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		CurrentSystem!.RegisterValueCollectionCallback<HashSet<T>, T>(_ownerId, id, callback);
	}

	public void ReceiveString(string id, Action<string?> callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveString(id, callback));
			return;
		}

		CurrentSystem!.RegisterStringCallback(_ownerId, id, callback);
	}

	public void ReceiveStringList(string id, Action<List<string>?>? callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveStringList(id, callback));
			return;
		}

		CurrentSystem!.RegisterStringListCallback(_ownerId, id, callback);
	}

	public void ReceiveEmptyCommand(string id, Action callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveEmptyCommand(id, callback));
			return;
		}

		CurrentSystem!.RegisterEmptyCallback(_ownerId, id, callback);
	}

	public void ReceiveObject<T>(string id, Action<T> callback) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveObject(id, callback));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		CurrentSystem!.RegisterObjectCallback(_ownerId, id, callback);
	}

	public void ReceiveObjectList<T>(string id, Action<List<T>> callback) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveObjectList(id, callback));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		CurrentSystem!.RegisterObjectListCallback(_ownerId, id, callback);
	}
}
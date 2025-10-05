using Renderite.Shared;

namespace InterprocessLib;

/// <summary>
/// Simple interprocess messaging API.
/// </summary>
public class Messenger
{
	private static MessagingBackend? _defaultBackend;

	private MessagingBackend? _customBackend;

	private MessagingBackend? Backend => _customBackend ?? _defaultBackend;

	/// <summary>
	/// If true the messenger will send commands immediately, otherwise commands will wait in a queue until the non-authority process sends the <see cref="MessengerReadyCommand"/>.
	/// </summary>
	public bool IsInitialized => Backend?.IsInitialized == true;

	/// <summary>
	/// Does this process have authority over the other process.
	/// </summary>
	public bool? IsAuthority => Backend?.IsAuthority;

	/// <summary>
	/// Is the interprocess connection still available
	/// </summary>
	public bool? IsAlive => Backend?.IsAlive;

	internal static bool DefaultBackendInitStarted = false;

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

	internal static List<Action>? _defaultBackendPostInitActions = new();

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

		if (_defaultBackend is null)
		{
			DefaultBackendRunPostInit(() =>
			{
				Register();
			});
		}
		else
		{
			Register();
		}

		if (_defaultBackend is null && !DefaultBackendInitStarted)
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

	internal static void SetDefaultBackend(MessagingBackend backend)
	{
		_defaultBackend = backend;
		_defaultBackend._postInitActions = _defaultBackendPostInitActions;
		_defaultBackendPostInitActions = null;
	}

	/// <summary>
	/// Creates an instance with a unique owner and a custom backend
	/// </summary>
	/// <param name="ownerId">Unique identifier for this instance in this process. Should match the other process.</param>
	/// /// <param name="customBackend">Custom messaging backend. Allows connecting to any custom process.</param>
	/// <param name="additionalObjectTypes">Optional list of additional <see cref="IMemoryPackable"/> class types you want to be able to send or receieve. Types you want to use that are vanilla go in here too.</param>
	/// <param name="additionalValueTypes">Optional list of additional unmanaged types you want to be able to send or receieve.</param>
	/// <exception cref="ArgumentNullException"></exception>
	public Messenger(string ownerId, MessagingBackend customBackend, List<Type>? additionalObjectTypes = null, List<Type>? additionalValueTypes = null)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		if (customBackend is null)
			throw new ArgumentNullException(nameof(customBackend));

		_customBackend = customBackend;

		_ownerId = ownerId;

		_additionalObjectTypes = additionalObjectTypes;

		_additionalValueTypes = additionalValueTypes;

		Register();
	}

	private void RunPostInit(Action act)
	{
		if (Backend is null)
			DefaultBackendRunPostInit(act);
		else
			Backend.RunPostInit(act);
	}

	private void Register()
	{
		if (Backend!.HasOwner(_ownerId))
		{
			OnWarning?.Invoke($"Owner {_ownerId} has already been registered in this process for messaging backend with queue name: {Backend.QueueName}");
		}
		else
			Backend.RegisterOwner(_ownerId);

		if (_additionalObjectTypes is not null)
		{
			Backend.TypeManager.InitObjectTypeList(_additionalObjectTypes.Where(t => !Backend.TypeManager.IsObjectTypeInitialized(t)).ToList());
		}
		if (_additionalValueTypes is not null)
		{
			Backend.TypeManager.InitValueTypeList(_additionalValueTypes.Where(t => !Backend.TypeManager.IsValueTypeInitialized(t)).ToList());
		}
	}

	private static void DefaultBackendRunPostInit(Action act)
	{
		if (_defaultBackend?.IsInitialized != true)
		{
			_defaultBackendPostInitActions!.Add(act);
		}
		else
			throw new InvalidOperationException("Default host already initialized!");
	}

	public void SendValue<T>(string id, T value) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendValue(id, value));
			return;
		}

		if (!Backend!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {value.GetType().Name} needs to be registered first!");

		var command = new ValueCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Value = value;
		Backend!.SendCommand(command);
	}

	public void SendValueList<T>(string id, List<T> list) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendValueList(id, list));
			return;
		}

		if (!Backend!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ValueCollectionCommand<List<T>, T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = list;
		Backend!.SendCommand(command);
	}

	public void SendValueHashSet<T>(string id, HashSet<T> hashSet) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendValueHashSet(id, hashSet));
			return;
		}

		if (!Backend!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ValueCollectionCommand<HashSet<T>, T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = hashSet;
		Backend!.SendCommand(command);
	}

	public void SendString(string id, string str)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendString(id, str));
			return;
		}

		var command = new StringCommand();
		command.Owner = _ownerId;
		command.Id = id;
		command.String = str;
		Backend!.SendCommand(command);
	}

	public void SendStringList(string id, List<string> list)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendStringList(id, list));
			return;
		}

		var command = new StringListCommand();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = list;
		Backend!.SendCommand(command);
	}

	public void SendEmptyCommand(string id)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendEmptyCommand(id));
			return;
		}

		var command = new IdentifiableCommand();
		command.Owner = _ownerId;
		command.Id = id;
		Backend!.SendCommand(command);
	}

	public void SendObject<T>(string id, T? obj) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendObject(id, obj));
			return;
		}

		if (!Backend!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var wrapper = new ObjectCommand<T>();
		wrapper.Object = obj;
		wrapper.Owner = _ownerId;
		wrapper.Id = id;

		Backend!.SendCommand(wrapper);
	}

	public void SendObjectList<T>(string id, List<T> list) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendObjectList(id, list));
			return;
		}

		if (!Backend!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ObjectListCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = list;
		Backend!.SendCommand(command);
	}

	public void ReceiveValue<T>(string id, Action<T> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveValue(id, callback));
			return;
		}

		if (!Backend!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		Backend!.RegisterValueCallback(_ownerId, id, callback);
	}

	public void ReceiveValueList<T>(string id, Action<List<T>> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveValueList(id, callback));
			return;
		}

		if (!Backend!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		Backend!.RegisterValueCollectionCallback<List<T>, T>(_ownerId, id, callback);
	}

	public void ReceiveValueHashSet<T>(string id, Action<HashSet<T>> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveValueHashSet(id, callback));
			return;
		}

		if (!Backend!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		Backend!.RegisterValueCollectionCallback<HashSet<T>, T>(_ownerId, id, callback);
	}

	public void ReceiveString(string id, Action<string?> callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if(!IsInitialized)
		{
			RunPostInit(() => ReceiveString(id, callback));
			return;
		}

		Backend!.RegisterStringCallback(_ownerId, id, callback);
	}

	public void ReceiveStringList(string id, Action<List<string>?>? callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveStringList(id, callback));
			return;
		}

		Backend!.RegisterStringListCallback(_ownerId, id, callback);
	}

	public void ReceiveEmptyCommand(string id, Action callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveEmptyCommand(id, callback));
			return;
		}

		Backend!.RegisterEmptyCallback(_ownerId, id, callback);
	}

	public void ReceiveObject<T>(string id, Action<T> callback) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveObject(id, callback));
			return;
		}

		if (!Backend!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		Backend!.RegisterObjectCallback(_ownerId, id, callback);
	}

	public void ReceiveObjectList<T>(string id, Action<List<T>> callback) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveObjectList(id, callback));
			return;
		}

		if (!Backend!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		Backend!.RegisterObjectListCallback(_ownerId, id, callback);
	}
}
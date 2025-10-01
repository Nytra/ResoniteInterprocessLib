using Renderite.Shared;

namespace InterprocessLib;

public partial class Messenger
{
	private static MessagingHost? _host;

	private static bool _isInitialized => _host is not null && _postInitActions is null;

	/// <summary>
	/// Does this process have authority over the other process.
	/// </summary>
	public const bool IsAuthority = IsFrooxEngine;

	internal static Action<Exception>? OnFailure;

	internal static Action<string>? OnWarning;

	internal static Action<string>? OnDebug;

	private static List<Action>? _postInitActions = new();

	private string _ownerId;

	private static HashSet<string> _registeredOwnerIds = new();

	private List<Type>? _additionalObjectTypes;

	private List<Type>? _additionalValueTypes;

	/// <summary>
	/// Simple interprocess messaging API.
	/// </summary>
	/// <param name="ownerId">Unique identifier for this instance in this process. Should match the other process.</param>
	/// <param name="additionalObjectTypes">Optional list of additional <see cref="IMemoryPackable"/> class types you want to be able to send or receieve.</param>
	/// <param name="additionalValueTypes">Optional list of additional unmanaged types you want to be able to send or receieve.</param>
	/// <exception cref="ArgumentNullException"></exception>
	/// <exception cref="ArgumentException"></exception>
	public Messenger(string ownerId, List<Type>? additionalObjectTypes = null, List<Type>? additionalValueTypes = null)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		if (_registeredOwnerIds.Contains(ownerId))
			throw new ArgumentException($"Owner \"{ownerId}\" is already registered!");

		_ownerId = ownerId;

		_registeredOwnerIds.Add(ownerId);

		_additionalObjectTypes = additionalObjectTypes;

		_additionalValueTypes = additionalValueTypes;

		if (_isInitialized)
			Register();
		else
			RunPostInit(Register);
	}

	private void Register()
	{
		_host!.RegisterOwner(_ownerId);
		if (_additionalObjectTypes is not null)
		{
			TypeManager.InitObjectTypeList(_additionalObjectTypes);
		}
		if (_additionalValueTypes is not null)
		{
			TypeManager.InitValueTypeList(_additionalValueTypes);
		}
	}

	private static void FinishInitialization()
	{
		if (IsAuthority)
			_host!.SendCommand(new MessengerReadyCommand());

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
				OnWarning?.Invoke($"Exception running post-init action:\n{ex}");
			}
		}
	}

	private static void RunPostInit(Action act)
	{
		if (!_isInitialized)
		{
			_postInitActions!.Add(act);
		}
		else
			throw new InvalidOperationException("Already initialized!");
	}

	public void SendValue<T>(string id, T value) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => SendValue(id, value));
			return;
		}

		if (!TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {value.GetType().Name} needs to be registered first!");

		var command = new ValueCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Value = value;
		_host!.SendCommand(command);
	}

	public void SendValueList<T>(string id, List<T> list) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => SendValueList(id, list));
			return;
		}

		if (!TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ValueListCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = list;
		_host!.SendCommand(command);
	}

	public void SendValueHashSet<T>(string id, HashSet<T> hashSet) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => SendValueHashSet(id, hashSet));
			return;
		}

		if (!TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ValueHashSetCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = hashSet;
		_host!.SendCommand(command);
	}

	public void SendString(string id, string str)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => SendString(id, str));
			return;
		}

		var command = new StringCommand();
		command.Owner = _ownerId;
		command.Id = id;
		command.String = str;
		_host!.SendCommand(command);
	}

	public void SendStringList(string id, List<string> list)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => SendStringList(id, list));
			return;
		}

		var command = new StringListCommand();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = list;
		_host!.SendCommand(command);
	}

	public void SendEmptyCommand(string id)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => SendEmptyCommand(id));
			return;
		}

		var command = new EmptyCommand();
		command.Owner = _ownerId;
		command.Id = id;
		_host!.SendCommand(command);
	}

	public void SendObject<T>(string id, T? obj) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => SendObject(id, obj));
			return;
		}

		if (!TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var wrapper = new ObjectCommand<T>();
		wrapper.Object = obj;
		wrapper.Owner = _ownerId;
		wrapper.Id = id;

		_host!.SendCommand(wrapper);
	}

	public void SendObjectList<T>(string id, List<T> list) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => SendObjectList(id, list));
			return;
		}

		if (!TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ObjectListCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = list;
		_host!.SendCommand(command);
	}

	public void ReceiveValue<T>(string id, Action<T> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => ReceiveValue(id, callback));
			return;
		}

		if (!TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		_host!.RegisterValueCallback(_ownerId, id, callback);
	}

	public void ReceiveValueList<T>(string id, Action<List<T>> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => ReceiveValueList(id, callback));
			return;
		}

		if (!TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		_host!.RegisterValueListCallback(_ownerId, id, callback);
	}

	public void ReceiveValueHashSet<T>(string id, Action<HashSet<T>> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => ReceiveValueHashSet(id, callback));
			return;
		}

		if (!TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		_host!.RegisterValueHashSetCallback(_ownerId, id, callback);
	}

	public void ReceiveString(string id, Action<string?> callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => ReceiveString(id, callback));
			return;
		}

		_host!.RegisterStringCallback(_ownerId, id, callback);
	}

	public void ReceiveStringList(string id, Action<List<string>> callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => ReceiveStringList(id, callback));
			return;
		}

		_host!.RegisterStringListCallback(_ownerId, id, callback);
	}

	public void ReceiveEmptyCommand(string id, Action callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => ReceiveEmptyCommand(id, callback));
			return;
		}

		_host!.RegisterEmptyCallback(_ownerId, id, callback);
	}

	public void ReceiveObject<T>(string id, Action<T> callback) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => ReceiveObject(id, callback));
			return;
		}

		if (!TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		_host!.RegisterObjectCallback(_ownerId, id, callback);
	}

	public void ReceiveObjectList<T>(string id, Action<List<T>> callback) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!_isInitialized)
		{
			RunPostInit(() => ReceiveObjectList(id, callback));
			return;
		}

		if (!TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		_host!.RegisterObjectListCallback(_ownerId, id, callback);
	}
}
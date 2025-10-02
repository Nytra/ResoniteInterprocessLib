using Renderite.Shared;
using System.Runtime.InteropServices;

namespace InterprocessLib;

/// <summary>
/// Simple interprocess messaging API.
/// </summary>
public class Messenger
{
	internal static MessagingHost? Host;

	/// <summary>
	/// If true the messenger will send commands immediately, otherwise commands will wait in a queue until the authority process sends the <see cref="MessengerReadyCommand"/>.
	/// </summary>
	public static bool IsInitialized => Host is not null && _postInitActions is null;

	/// <summary>
	/// Does this process have authority over the other process.
	/// </summary>
	public static bool IsAuthority { get; internal set; }

	internal static Action<Exception>? OnFailure;

	internal static Action<string>? OnWarning;

	internal static Action<string>? OnDebug;

	private static List<Action>? _postInitActions = new();

	private string _ownerId;

	private static HashSet<string> _registeredOwnerIds = new();

	private List<Type>? _additionalObjectTypes;

	private List<Type>? _additionalValueTypes;

	/// <summary>
	/// Creates an instance with a unique owner
	/// </summary>
	/// <param name="ownerId">Unique identifier for this instance in this process. Should match the other process.</param>
	/// <param name="additionalObjectTypes">Optional list of additional <see cref="IMemoryPackable"/> class types you want to be able to send or receieve. Types you want to use that are vanilla go in here too.</param>
	/// <param name="additionalValueTypes">Optional list of additional unmanaged types you want to be able to send or receieve.</param>
	/// <exception cref="ArgumentNullException"></exception>
	/// <exception cref="ArgumentException"></exception>
	public Messenger(string ownerId, List<Type>? additionalObjectTypes = null, List<Type>? additionalValueTypes = null)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		_ownerId = ownerId;

		_additionalObjectTypes = additionalObjectTypes;

		_additionalValueTypes = additionalValueTypes;

		if (_additionalObjectTypes is not null)
		{
			TypeManager.InitObjectTypeList(_additionalObjectTypes.Where(t => !TypeManager.IsObjectTypeInitialized(t)).ToList());
		}
		if (_additionalValueTypes is not null)
		{
			TypeManager.InitValueTypeList(_additionalValueTypes.Where(t => !TypeManager.IsValueTypeInitialized(t)).ToList());
		}

		if (!_registeredOwnerIds.Contains(ownerId))
		{
			_registeredOwnerIds.Add(ownerId);

			if (IsInitialized)
				Register();
			else
				RunPostInit(Register);
		}
		else
		{
			OnWarning?.Invoke($"A messenger with id {ownerId} has already been created in this process!");
		}

		if (Host is null)
		{
			OnWarning?.Invoke($"Environment Version: {Environment.Version}");
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

	private void Register()
	{
		Host!.RegisterOwner(_ownerId);
	}

	internal static void FinishInitialization()
	{
		if (IsAuthority)
			Host!.SendCommand(new MessengerReadyCommand());

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
		if (!IsInitialized)
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

		if (!IsInitialized)
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
		Host!.SendCommand(command);
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

		if (!TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ValueCollectionCommand<List<T>, T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = list;
		Host!.SendCommand(command);
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

		if (!TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ValueCollectionCommand<HashSet<T>, T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = hashSet;
		Host!.SendCommand(command);
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
		Host!.SendCommand(command);
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
		Host!.SendCommand(command);
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

		var command = new EmptyCommand();
		command.Owner = _ownerId;
		command.Id = id;
		Host!.SendCommand(command);
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

		if (!TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var wrapper = new ObjectCommand<T>();
		wrapper.Object = obj;
		wrapper.Owner = _ownerId;
		wrapper.Id = id;

		Host!.SendCommand(wrapper);
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

		if (!TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ObjectListCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = list;
		Host!.SendCommand(command);
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

		if (!TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		Host!.RegisterValueCallback(_ownerId, id, callback);
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

		if (!TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		Host!.RegisterValueCollectionCallback<List<T>, T>(_ownerId, id, callback);
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

		if (!TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		Host!.RegisterValueCollectionCallback<HashSet<T>, T>(_ownerId, id, callback);
	}

	public void ReceiveString(string id, Action<string?> callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveString(id, callback));
			return;
		}

		Host!.RegisterStringCallback(_ownerId, id, callback);
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

		Host!.RegisterStringListCallback(_ownerId, id, callback);
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

		Host!.RegisterEmptyCallback(_ownerId, id, callback);
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

		if (!TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		Host!.RegisterObjectCallback(_ownerId, id, callback);
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

		if (!TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		Host!.RegisterObjectListCallback(_ownerId, id, callback);
	}
}
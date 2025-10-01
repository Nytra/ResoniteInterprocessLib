using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

public partial class Messenger
{
	internal static MessagingHost? Host;

	internal static bool IsInitialized => Host is not null && PostInitActions is null;

	internal static RenderCommandHandler? OnCommandReceived;

	internal static Action<Exception>? OnFailure;

	internal static Action<string>? OnWarning;

	internal static Action<string>? OnDebug;

	private static List<Action>? PostInitActions = new();

	private string _ownerId;

	private static HashSet<string> _registeredOwnerIds = new();

	private List<Type>? _additionalPackableTypes;

	private List<Type>? _additionalValueTypes;

	public Messenger(string ownerId, List<Type>? additionalPackableTypes = null, List<Type>? additionalValueTypes = null)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		if (_registeredOwnerIds.Contains(ownerId))
			throw new ArgumentException($"Owner \"{ownerId}\" is already registered!");

		_ownerId = ownerId;

		_registeredOwnerIds.Add(ownerId);

		_additionalPackableTypes = additionalPackableTypes;

		if (IsInitialized)
			Register();
		else
			RunPostInit(Register);
	}

	private void Register()
	{
		Host!.RegisterOwner(_ownerId);
		if (_additionalPackableTypes is not null)
		{
			foreach (var type in _additionalPackableTypes)
			{
				try
				{
					TypeManager._registerPackableTypeMethod!.MakeGenericMethod(type).Invoke(null, null);
				}
				catch (Exception ex)
				{
					OnWarning?.Invoke($"Could not register additional type {type.Name}!\n{ex}");
				}
			}
		}
		if (_additionalValueTypes is not null)
		{
			foreach (var type in _additionalValueTypes)
			{
				try
				{
					TypeManager._registerValueTypeMethod!.MakeGenericMethod(type).Invoke(null, null);
				}
				catch (Exception ex)
				{
					OnWarning?.Invoke($"Could not register additional value type {type.Name}!\n{ex}");
				}
			}
		}
	}

	internal static void FinishInitialization()
	{
		if (Host!.IsAuthority)
			Host!.SendCommand(new MessengerReadyCommand());

		var actions = PostInitActions!.ToArray();
		PostInitActions = null;
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
			PostInitActions!.Add(act);
		}
		else
			throw new InvalidOperationException("Already initialized!");
	}

	public void Send<T>(string id, T value) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => Send(id, value));
			return;
		}

		var command = new ValueCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Value = value;
		Host!.SendCommand(command);
	}

	public void Send(string id, string str)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => Send(id, str));
			return;
		}

		var command = new StringCommand();
		command.Owner = _ownerId;
		command.Id = id;
		command.String = str;
		Host!.SendCommand(command);
	}

	public void Send(string id)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => Send(id));
			return;
		}

		var command = new EmptyCommand();
		command.Owner = _ownerId;
		command.Id = id;
		Host!.SendCommand(command);
	}

	public void SendObject<T>(string id, T obj) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendObject(id, obj));
			return;
		}

		if (!TypeManager.IsTypeInitialized<T>())
			throw new InvalidOperationException($"Type {obj.GetType().Name} needs to be registered first!");

		var wrapper = new WrapperCommand<T>();
		wrapper.Object = obj;
		wrapper.Owner = _ownerId;
		wrapper.Id = id;

		Host!.SendCommand(wrapper);
	}

	public void Receive<T>(string id, Action<T> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => Receive(id, callback));
			return;
		}

		Host!.RegisterValueCallback(_ownerId, id, callback);
	}

	public void Receive(string id, Action<string> callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => Receive(id, callback));
			return;
		}

		Host!.RegisterStringCallback(_ownerId, id, callback);
	}

	public void Receive(string id, Action callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => Receive(id, callback));
			return;
		}

		Host!.RegisterCallback(_ownerId, id, callback);
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

		Host!.RegisterWrapperCallback(_ownerId, id, callback);
	}

	
}
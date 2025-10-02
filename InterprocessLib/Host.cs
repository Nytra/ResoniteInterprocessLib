using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

internal class MessagingHost
{
	private struct OwnerData
	{
		public readonly Dictionary<string, object?> ValueCallbacks = new();

		public readonly Dictionary<string, Action<string?>?> StringCallbacks = new();

		public readonly Dictionary<string, Action?> EmptyCallbacks = new();

		public readonly Dictionary<string, object?> ObjectCallbacks = new();

		public readonly Dictionary<string, object?> ValueCollectionCallbacks = new();

		public readonly Dictionary<string, Action<List<string>?>?> StringListCallbacks = new();

		public readonly Dictionary<string, object?> ObjectListCallbacks = new();

		public OwnerData()
		{
		}
	}

	public bool IsAuthority { get; }

	public string QueueName { get; }

	public long QueueCapacity { get; }

	private MessagingManager _primary;

	private static MethodInfo? _handleValueCommandMethod = typeof(MessagingHost).GetMethod(nameof(HandleValueCommand), BindingFlags.Instance | BindingFlags.NonPublic);

	private static MethodInfo? _handleValueCollectionCommandMethod = typeof(MessagingHost).GetMethod(nameof(HandleValueCollectionCommand), BindingFlags.Instance | BindingFlags.NonPublic);

	private static MethodInfo? _handleObjectCommandMethod = typeof(MessagingHost).GetMethod(nameof(HandleObjectCommand), BindingFlags.Instance | BindingFlags.NonPublic);

	private static MethodInfo? _handleObjectListCommandMethod = typeof(MessagingHost).GetMethod(nameof(HandleObjectListCommand), BindingFlags.Instance | BindingFlags.NonPublic);

	private RenderCommandHandler? OnCommandReceived { get; }

	private Action<string>? OnWarning { get; }

	private Action<string>? OnDebug { get; }

	private Action<Exception>? OnFailure { get; }

	private Dictionary<string, OwnerData> _ownerData = new();

	public void RegisterOwner(string ownerName)
	{
		var ownerData = new OwnerData();
		_ownerData.Add(ownerName, ownerData);
	}

	public void RegisterValueCallback<T>(string owner, string id, Action<T> callback) where T : unmanaged
	{
		_ownerData[owner].ValueCallbacks[id] = callback;
	}

	public void RegisterValueCollectionCallback<C, T>(string owner, string id, Action<C> callback) where C : ICollection<T>, new() where T : unmanaged
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
		_ownerData[owner].ObjectListCallbacks[id] = callback;
	}

	static MessagingHost()
	{
		TypeManager.InitializeCoreTypes();
	}

	public MessagingHost(bool isAuthority, string queueName, long queueCapacity, IMemoryPackerEntityPool pool, RenderCommandHandler? commandHandler, Action<Exception>? failhandler, Action<string>? warnHandler, Action<string>? debugHandler)
	{
		IsAuthority = isAuthority;
		QueueName = queueName + "InterprocessLib";
		QueueCapacity = queueCapacity;

		OnDebug = debugHandler;
		OnWarning = warnHandler;
		OnFailure = failhandler;
		OnCommandReceived = commandHandler;

		_primary = new MessagingManager(pool);
		_primary.CommandHandler = CommandHandler;
		_primary.FailureHandler = (ex) => 
		{ 
			OnFailure?.Invoke(ex);
		};
		_primary.WarningHandler = (msg) =>
		{
			OnWarning?.Invoke(msg);
		};

		_primary.Connect(queueName, isAuthority, queueCapacity);
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
			OnWarning?.Invoke($"ValueCommand<{typeof(T).Name}> with Id \"{command.Id}\" is not registered to receive a callback!");
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
			OnWarning?.Invoke($"ValueCollectionCommand<{typeof(C).Name}, {typeof(T).Name}> with Id \"{command.Id}\" is not registered to receive a callback!");
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
			OnWarning?.Invoke($"StringCommand with Id \"{command.Id}\" is not registered to receive a callback!");
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
			OnWarning?.Invoke($"StringListCommand with Id \"{command.Id}\" is not registered to receive a callback!");
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
			OnWarning?.Invoke($"EmptyCommand with Id \"{command.Id}\" is not registered to receive a callback!");
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
			OnWarning?.Invoke($"ObjectCommand<{command.ObjectType.Name}> with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void HandleObjectListCommand<T>(ObjectListCommand<T> command) where T : class, IMemoryPackable, new()
	{
		if (_ownerData[command.Owner].ObjectListCallbacks.TryGetValue(command.Id, out var callback))
		{
			if (callback != null)
			{
				((Action<List<T>?>)callback).Invoke(command.Values);
			}
		}
		else
		{
			OnWarning?.Invoke($"ObjectListCommand<{typeof(T).Name}> with Id \"{command.Id}\" is not registered to receive a callback!");
		}
	}

	private void CommandHandler(RendererCommand command, int messageSize)
	{
		OnDebug?.Invoke($"Received {command}");

		OnCommandReceived?.Invoke(command, messageSize);

		if (command is IdentifiableCommand identifiableCommand)
		{
			if (!_ownerData.TryGetValue(identifiableCommand.Owner, out var data))
			{
				OnWarning?.Invoke($"Owner \"{identifiableCommand.Owner}\" is not registered!");
				return;
			}
		}

		if (command is ValueCommand valueCommand)
		{
			var valueType = valueCommand.ValueType;
			var typedMethod = _handleValueCommandMethod!.MakeGenericMethod(valueType);
			typedMethod.Invoke(this, [command]);
		}
		else if (command is CollectionCommand collectionCommand)
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
				typedMethod.Invoke(this, [command]);
			}
			else
			{
				var typedMethod = _handleObjectListCommandMethod!.MakeGenericMethod(innerDataType);
				typedMethod.Invoke(this, [command]);
			}
		}
		else if (command is ObjectCommand objectCommand)
		{
			var objectType = objectCommand.ObjectType;
			var typedMethod = _handleObjectCommandMethod!.MakeGenericMethod(objectType);
			typedMethod.Invoke(this, [command]);
		}
		else
		{
			switch (command)
			{
				case StringCommand:
					HandleStringCommand((StringCommand)command);
					break;
				case EmptyCommand:
					HandleEmptyCommand((EmptyCommand)command);
					break;
				case IdentifiableCommand unknownCommand:
					OnWarning?.Invoke($"Received unrecognized IdentifiableCommand of type {command.GetType().Name}: {unknownCommand.Owner}:{unknownCommand.Id}");
					break;
				default:
					break;
			}
		}
	}

	public void SendCommand(RendererCommand command)
	{
		OnDebug?.Invoke($"Sending {command}");
		_primary.SendCommand(command);
	}
}
using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

public class MessagingHost
{
	private struct OwnerData
	{
		public readonly Dictionary<string, object?> ValueCallbacks = new();

		public readonly Dictionary<string, Action<string?>?> StringCallbacks = new();

		public readonly Dictionary<string, Action?> EmptyCallbacks = new();

		public readonly Dictionary<string, object?> WrapperCallbacks = new();

		public OwnerData()
		{
		}
	}

	public bool IsAuthority { get; }

	public string QueueName { get; }

	public long QueueCapacity { get; }

	private MessagingManager _primary;

	private static MethodInfo? _handleValueCommandMethod = typeof(MessagingHost).GetMethod(nameof(HandleValueCommand), BindingFlags.Instance | BindingFlags.NonPublic);

	private static MethodInfo? _handleWrapperCommandMethod = typeof(MessagingHost).GetMethod(nameof(HandleWrapperCommand), BindingFlags.Instance | BindingFlags.NonPublic);

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

	public void RegisterStringCallback(string owner, string id, Action<string?> callback)
	{
		_ownerData[owner].StringCallbacks[id] = callback;
	}

	public void RegisterEmptyCallback(string owner, string id, Action callback)
	{
		_ownerData[owner].EmptyCallbacks[id] = callback;
	}

	public void RegisterWrapperCallback<T>(string owner, string id, Action<T?> callback) where T : class, IMemoryPackable, new()
	{
		_ownerData[owner].WrapperCallbacks[id] = callback;
	}

	public MessagingHost(bool isAuthority, string queueName, long queueCapacity, IMemoryPackerEntityPool pool, RenderCommandHandler? commandHandler, Action<Exception>? failhandler, Action<string>? warnHandler, Action<string>? debugHandler)
	{
		IsAuthority = isAuthority;
		QueueName = queueName + "InterprocessLib";
		QueueCapacity = queueCapacity;
	
		_primary = new MessagingManager(pool);
		_primary.CommandHandler = CommandHandler;
		_primary.FailureHandler = FailHandler;
		_primary.WarningHandler = WarnHandler;

		OnDebug = debugHandler;
		OnWarning = warnHandler;
		OnFailure = failhandler;
		OnCommandReceived = commandHandler;

		_primary.Connect(queueName, isAuthority, queueCapacity);

		TypeManager.InitializeCoreTypes();
	}

	private void FailHandler(Exception ex)
	{
		OnFailure?.Invoke(ex);
	}

	private void WarnHandler(string msg)
	{
		OnWarning?.Invoke(msg);
	}

	private void HandleValueCommand<T>(ValueCommand<T> command) where T : unmanaged
	{
		OnDebug?.Invoke($"Received ValueCommand<{typeof(T).Name}>: {command.Owner}:{command.Id}:{command.Value}");
		if (_ownerData[command.Owner].ValueCallbacks.TryGetValue(command.Id, out object? callback))
		{
			if (callback != null)
			{
				((Action<T>)callback).Invoke(command.Value);
			}
		}
		else
		{
			OnWarning?.Invoke($"ValueCommand<{typeof(T).Name}> with Id \"{command.Id}\" is not registered to receive a callback!");
			return;
		}
	}

	private void HandleStringCommand(StringCommand command)
	{
		OnDebug?.Invoke($"Received StringCommand: {command.Owner}:{command.Id}:{command.String ?? "NULL"}");
		if (_ownerData[command.Owner].StringCallbacks.TryGetValue(command.Id, out Action<string?>? callback))
		{
			if (callback != null)
			{
				callback.Invoke(command.String);
			}
		}
		else
		{
			OnWarning?.Invoke($"StringCommand with Id \"{command.Id}\" is not registered to receive a callback!");
			return;
		}
	}

	private void HandleEmptyCommand(EmptyCommand command)
	{
		OnDebug?.Invoke($"Received EmptyCommand: {command.Owner}:{command.Id}");
		if (_ownerData[command.Owner].EmptyCallbacks.TryGetValue(command.Id, out Action? callback))
		{
			if (callback != null)
			{
				callback.Invoke();
			}
		}
		else
		{
			OnWarning?.Invoke($"EmptyCommand with Id \"{command.Id}\" is not registered to receive a callback!");
			return;
		}
	}

	private void HandleWrapperCommand<T>(WrapperCommand command) where T : class, IMemoryPackable, new()
	{
		OnDebug?.Invoke($"Received WrapperCommand<{command.ObjectType.Name}>: {command.Owner}:{command.Id}:{command.UntypedObject ?? "NULL"}");
		if (_ownerData[command.Owner].WrapperCallbacks.TryGetValue(command.Id, out object? callback))
		{
			if (callback != null)
			{
				((Action<T?>)callback).Invoke((T?)command.UntypedObject);
			}
		}
		else
		{
			OnWarning?.Invoke($"WrapperCommand<{command.ObjectType.Name}> with Id \"{command.Id}\" is not registered to receive a callback!");
			return;
		}
	}

	private void CommandHandler(RendererCommand command, int messageSize)
	{
		OnCommandReceived?.Invoke(command, messageSize);

		if (command is IdentifiableCommand identifiableCommand)
		{
			if (!_ownerData.TryGetValue(identifiableCommand.Owner, out var data))
			{
				OnWarning?.Invoke($"Owner \"{identifiableCommand.Owner}\" is not registered!");
				return;
			}
		}

		var commandType = command.GetType();
		if (commandType.IsGenericType)
		{
			var genDef = commandType.GetGenericTypeDefinition();
			if (genDef == typeof(ValueCommand<>))
			{
				var valueType = commandType.GetGenericArguments()[0];
				var typedMethod = _handleValueCommandMethod!.MakeGenericMethod(valueType);
				typedMethod.Invoke(this, new object[] { command });
			}
			else if (genDef == typeof(WrapperCommand<>))
			{
				var objectType = commandType.GetGenericArguments()[0];
				var typedMethod = _handleWrapperCommandMethod!.MakeGenericMethod(objectType);
				typedMethod.Invoke(this, new object[] { command });
			}
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
					OnDebug?.Invoke($"Received RendererCommand: {command.GetType().Name}");
					break;
			}
		}
	}

	public void SendCommand(RendererCommand command)
	{
		if (OnDebug is not null)
		{
			if (command is StringCommand stringCommand)
			{
				OnDebug.Invoke($"Sending StringCommand: {stringCommand.Owner}:{stringCommand.Id}:{stringCommand.String}");
			}
			else if (command is ValueCommand valueCommand)
			{
				OnDebug.Invoke($"Sending ValueCommand<{valueCommand.ValueType.Name}>: {valueCommand.Owner}:{valueCommand.Id}:{valueCommand.UntypedValue}");
			}
			else if (command is WrapperCommand wrapperCommand)
			{
				OnDebug.Invoke($"Sending WrapperCommand<{wrapperCommand.ObjectType.Name}>: {wrapperCommand.Owner}:{wrapperCommand.Id}:{wrapperCommand.UntypedObject ?? "NULL"}");
			}
			else if (command is EmptyCommand emptyCommand)
			{
				OnDebug.Invoke($"Sending EmptyCommand: {emptyCommand.Owner}:{emptyCommand.Id}");
			}
			else if (command is IdentifiableCommand identifiableCommand)
			{
				OnWarning?.Invoke($"Sending unrecognized IdentifiableCommand of type {command.GetType().Name}: {identifiableCommand.Owner}:{identifiableCommand.Id}");
			}
			else
			{
				OnDebug.Invoke($"Sending RendererCommand: {command.GetType().Name}");
			}
		}
		_primary.SendCommand(command);
	}
}
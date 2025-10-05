using Elements.Core;
using InterprocessLib;
using InterprocessLib.Tests;
using Renderite.Shared;

namespace InterprocessLibStandaloneTest
{
	class MyPool : IMemoryPackerEntityPool
	{
		T IMemoryPackerEntityPool.Borrow<T>()
		{
			return Pool<T>.Borrow();
		}

		void IMemoryPackerEntityPool.Return<T>(T value)
		{
			Pool<T>.ReturnCleaned(ref value);
		}
	}

	internal class Program
    {
		private static void CommandHandler(RendererCommand command, int messageSize)
		{
		}

		private static void FailHandler(Exception ex)
		{
			Console.WriteLine($"[Custom Messaging Host] [ERROR] Exception in custom messaging host: {ex}");
		}

		private static void WarnHandler(string msg)
		{
			Console.WriteLine($"[Custom Messaging Host] [WARN] {msg}");
		}

		private static void DebugHandler(string msg)
		{
#if DEBUG
			Console.WriteLine($"[Custom Messaging Host] [DEBUG] {msg}");
#endif
		}

		static void Main(string[] args)
        {
			string? queueName;
			if (args.Length > 0)
			{
				queueName = args[0];
				Console.WriteLine("Queue name from args: " + queueName);
			}
			else
			{
				Console.WriteLine("Queue name:");
				queueName = Console.ReadLine();
			}
				
			Messenger.OnWarning = WarnHandler;
			Messenger.OnFailure = FailHandler;
			Messenger.OnDebug = DebugHandler;

			var customHost = new MessagingBackend(false, queueName!, 1024 * 1024, new MyPool(), CommandHandler, FailHandler, WarnHandler, DebugHandler);
			customHost.Initialize();

			var messenger = new Messenger("InterprocessLib.Tests", customHost, [typeof(TestCommand), typeof(TestNestedPackable), typeof(TestPackable), typeof(RendererInitData)], [typeof(TestStruct), typeof(TestNestedStruct), typeof(HapticPointState), typeof(ShadowType)]);

			Tests.RunTests(messenger, Console.WriteLine);

			Thread.Sleep(15000);
		}
    }
}

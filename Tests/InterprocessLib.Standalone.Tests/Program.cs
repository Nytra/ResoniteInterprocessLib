using System.Diagnostics;
using System.Runtime.CompilerServices;
using InterprocessLib;
using InterprocessLib.Tests;
using Renderite.Shared;

namespace InterprocessLibStandaloneTest
{
	internal class Program
    {
		private static CancellationTokenSource _cancel = new();
		private static void CommandHandler(RendererCommand command, int messageSize)
		{
		}

		private static void FailHandler(Exception ex)
		{
			Console.WriteLine($"[InterprocessLib.Tests] [ERROR] Exception in custom messaging backend: {ex}");
		}

		private static void WarnHandler(string msg)
		{
			Console.WriteLine($"[InterprocessLib.Tests] [WARN] {msg}");
		}

		private static void DebugHandler(string msg)
		{
#if DEBUG
			Console.WriteLine($"[InterprocessLib.Tests] [DEBUG] {msg}");
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
			
			var messenger = new Messenger("InterprocessLib.Tests", false, queueName!);

			Tests.RunTests(messenger, Console.WriteLine);

			messenger.ReceiveEmptyCommand("HeartbeatResponse", () =>
			{
				_cancel.CancelAfter(5000);
			});

			_cancel.CancelAfter(10000);

			Task.Run(async () =>
			{
				while (!_cancel.IsCancellationRequested)
				{
					messenger.SendEmptyCommand("Heartbeat");
					await Task.Delay(2500);
				}
			}).Wait();
		}
    }
}

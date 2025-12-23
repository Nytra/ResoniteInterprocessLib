using InterprocessLib;
using InterprocessLib.Tests;

namespace InterprocessLibStandaloneTest
{
	internal class Program
    {
		private static CancellationTokenSource _cancel = new();

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

			Messenger.OnWarning += (msg) =>
			{
				Console.WriteLine($"[InterprocessLib] [WARN] {msg}");
			};
			Messenger.OnFailure += (ex) =>
			{
				Console.WriteLine($"[InterprocessLib] [ERROR] {ex}");
			};
#if DEBUG
			Messenger.OnDebug += (msg) => 
			{
				Console.WriteLine($"[InterprocessLib] [DEBUG] {msg}");
			};
#endif
			
			var messenger = new Messenger("InterprocessLib.Tests", false, queueName!);

			Tests.RunTests(messenger, Console.WriteLine);

			messenger.ReceivePing((latency) =>
			{
				_cancel.CancelAfter(5000);
			});

			_cancel.CancelAfter(10000);

			Task.Run(async () =>
			{
				while (!_cancel.IsCancellationRequested)
				{
					messenger.SendPing();
					await Task.Delay(2500);
				}
			}).Wait();
		}
    }
}

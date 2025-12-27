using InterprocessLib;
using InterprocessLib.Tests;
using Renderite.Shared;

namespace InterprocessLibStandaloneTest
{
    internal class Pool : IMemoryPackerEntityPool
    {
        T IMemoryPackerEntityPool.Borrow<T>()
        {
            return new T();
        }

        void IMemoryPackerEntityPool.Return<T>(T value)
        {
        }
    }

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
			
			var messenger = new Messenger("InterprocessLib.Tests", false, queueName!, new Pool());

			Tests.RunTests(messenger, Console.WriteLine);

			messenger.ReceiveEmptyCommand("Ping", () =>
			{
				_cancel.CancelAfter(5000);
			});

			_cancel.CancelAfter(10000);

			Task.Run(async () =>
			{
				while (!_cancel.IsCancellationRequested)
				{
					messenger.SendEmptyCommand("Ping");
					await Task.Delay(2500);
				}
			}).Wait();
		}
    }
}

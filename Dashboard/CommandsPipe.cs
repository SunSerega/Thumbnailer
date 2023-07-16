using System;

using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;

using System.Linq;

using System.Threading.Tasks;

using System.Windows;

namespace Dashboard
{
	public class CommandsPipe
	{
		private const string file_lock_name = @".lock";
		private readonly FileStream file_lock;

		private static class Commands
		{
			public const int NewerKillsOlder = 1;

		}

		public CommandsPipe()
		{
			var name = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!;

			{
				var old_procs = System.Diagnostics.Process.GetProcessesByName(name).ToList();
				var curr_p = System.Diagnostics.Process.GetCurrentProcess();
				old_procs.RemoveAll(p => p.StartTime >= curr_p.StartTime);
				if (old_procs.Count != 0)
				{
					var sw = System.Diagnostics.Stopwatch.StartNew();

					using (var client = new NamedPipeClientStream(name))
						try
						{
							client.Connect(TimeSpan.FromSeconds(1));

							var bw = new BinaryWriter(client);
							bw.Write(Commands.NewerKillsOlder);

							client.Flush();
							client.WaitForPipeDrain();
						}
						catch (TimeoutException) { }

					Task.WaitAll(old_procs.Select(p => Task.Run(() =>
					{
						if (p.WaitForExit(TimeSpan.FromSeconds(1)))
							return;
						MessageBox.Show($"Force killing [{p.Id}]");
						p.Kill();
						if (p.WaitForExit(TimeSpan.FromSeconds(1)))
							return;
						throw new InvalidOperationException();
					})).ToArray());

					//MessageBox.Show(sw.Elapsed.ToString());
				}
			}

			file_lock = File.Create(file_lock_name);

			new System.Threading.Thread(() =>
			{
				while (true)
					try
					{
						var ps = new PipeSecurity();
						ps.AddAccessRule(new PipeAccessRule(Environment.UserName, PipeAccessRights.ReadWrite, AccessControlType.Allow));
						
						using var server = NamedPipeServerStreamAcl.Create(
							pipeName: name,
							direction: PipeDirection.InOut,
							maxNumberOfServerInstances: 1,
							transmissionMode: PipeTransmissionMode.Byte,
							options: PipeOptions.WriteThrough,
							inBufferSize: 0, outBufferSize: 0,
							pipeSecurity: ps
						);
						
						server.WaitForConnection();

						var br = new BinaryReader(server);
						var command = br.ReadInt32();
						
						if (command == Commands.NewerKillsOlder)
							Application.Current.Dispatcher.Invoke(Application.Current.Shutdown);
						else
							throw new InvalidOperationException($"Command [{command}] not defined");

					}
					catch (Exception e)
					{
						Utils.HandleExtension(e);
					}
			})
			{
				IsBackground = true
			}.Start();

		}

		public void Shutdown()
		{
			file_lock.Close();
			File.Delete(file_lock_name);
		}

	}
}

using System;

using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;

using System.Linq;
using System.Collections.Generic;

using System.Windows;

namespace Dashboard
{
	public class CommandsPipe
	{
		private const string file_lock_name = @".lock";
		private readonly string name = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!;
		private readonly FileStream file_lock;

		private static class Commands
		{
			public const int NewerKillsOlder = 1;
			public const int GimmiThumb = 2;
		}

		private readonly Dictionary<int, Action<Stream>> command_handlers = new()
		{
			{ Commands.NewerKillsOlder, str=>App.Current.Dispatcher.Invoke(App.Current.Shutdown) }
		};
		public void AddThumbGen(ThumbGenerator thumb_gen) =>
			command_handlers.Add(Commands.GimmiThumb, str =>
			{
				var br = new BinaryReader(str);
				var bw = new BinaryWriter(str);

				var fname = br.ReadString();
				var cfi = thumb_gen.Generate(fname, _ => { }, false);
				bw.Write(cfi.CurrentThumbPath);

			});

		public CommandsPipe()
		{

			{
				var old_procs = System.Diagnostics.Process.GetProcessesByName(name).ToList();
				var curr_p = System.Diagnostics.Process.GetCurrentProcess();
				old_procs.RemoveAll(p => p.StartTime >= curr_p.StartTime);
				if (old_procs.Count != 0)
				{
					var sw = System.Diagnostics.Stopwatch.StartNew();

					//Separate thread, in case old process accepts message, but then hangs
					new System.Threading.Thread(() => Utils.HandleExtension(() =>
					{
						using var client = new NamedPipeClientStream(name);
						try
						{
							client.Connect(TimeSpan.FromSeconds(1));
						}
						catch (TimeoutException) { return; }
						try
						{
							new BinaryWriter(client)
								.Write(Commands.NewerKillsOlder);
							client.Flush();
							client.WaitForPipeDrain();
						}
						catch (IOException) { return; }
					}))
					{
						IsBackground = true,
						Name = $"Try ask elder to die"
					}.Start();

					foreach (var p in old_procs)
					{
						if (p.WaitForExit(TimeSpan.FromSeconds(1)))
							continue;

						var mb = new CustomMessageBox($"Force killing [{p.Id}]", null, default(Window));
						p.WaitForExitAsync().ContinueWith(t => mb.Dispatcher.Invoke(mb.Close));
						mb.ShowDialog();

						p.Kill();
						if (p.WaitForExit(TimeSpan.FromSeconds(1)))
							continue;

						throw new InvalidOperationException();
					}

					//CustomMessageBox.Show(sw.Elapsed.ToString());
				}
			}

			file_lock = File.Create(file_lock_name);

		}

		public void StartAccepting() =>
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
						var bw = new BinaryWriter(server);
						var command = br.ReadInt32();

						if (!command_handlers.TryGetValue(command, out var handler))
							throw new InvalidOperationException($"Command [{command}] not defined");

						handler(server);
						server.Flush();
						server.Disconnect();
					}
					catch (Exception e)
					{
						Utils.HandleExtension(e);
					}
			})
			{
				IsBackground = true,
				Name = $"Commands pipe",
			}.Start();

		public void Shutdown()
		{
			file_lock.Close();
			File.Delete(file_lock_name);
		}

	}
}

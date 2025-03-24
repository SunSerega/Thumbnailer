using System;

using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;

using System.Threading;
using System.Threading.Tasks;

using System.Linq;
using System.Collections.Generic;

using SunSharpUtils;
using SunSharpUtils.WPF;
using SunSharpUtils.Threading;

namespace Dashboard;

public class CommandsPipe
{
    private readonly String name = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!;

    private enum ECommand
    {
        NewerKillsOlder = 1,
        GimmiThumb = 2,
        RefreshAndCompare = 3,
    }

    private readonly Dictionary<ECommand, Action<Stream>> command_handlers = new()
    {
        { ECommand.NewerKillsOlder, _=>Common.Shutdown() },
    };
    public void AddThumbGen(ThumbGenerator thumb_gen) =>
        command_handlers.Add(ECommand.GimmiThumb, str =>
        {
            var br = new BinaryReader(str);
            var bw = new BinaryWriter(str);

            var fname = br.ReadString();
            using var cfi_use = thumb_gen.Generate(fname, nameof(ECommand.GimmiThumb), () => false, null, false);
            if (cfi_use is null) return;

            // Some system icons are deleted right after the thumb is generated
            // Teturning a temp thumb causes them to break
            // But speed doesn't matter, because they are never generated in large quantities
            if (fname.StartsWith(@"C:\Program Files\"))
                cfi_use.CFI.WaitThumbFinal();

            bw.Write(cfi_use.CFI.CurrentThumbPath);
        });
    public void AddRefreshAndCompareHandler(Action<Boolean, String[]> handler) =>
        command_handlers.Add(ECommand.RefreshAndCompare, str =>
        {
            var br = new BinaryReader(str);

            var force_regen = br.ReadBoolean();
            var c = br.ReadInt32();
            var file_list = new String[c];
            for (var i = 0; i < file_list.Length; ++i)
                file_list[i] = br.ReadString();

            handler(force_regen, file_list);
        });

    public CommandsPipe()
    {
        KillOlder();
        StartAccepting();
    }

    private void KillOlder()
    {
        var old_procs = System.Diagnostics.Process.GetProcessesByName(name).ToList();
        var curr_p = System.Diagnostics.Process.GetCurrentProcess();
        old_procs.RemoveAll(p => p.StartTime >= curr_p.StartTime);
        if (old_procs.Count != 0)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var need_old_proc_kill_command = true;
            //Separate thread, in case old process accepts message, but then hangs
            new Thread(() => Err.Handle(() =>
            {
                while (need_old_proc_kill_command)
                {
                    using var client = new NamedPipeClientStream(name);
                    try
                    {
                        client.Connect(TimeSpan.FromSeconds(1));
                    }
                    catch (TimeoutException) { continue; }
                    try
                    {
                        new BinaryWriter(client)
                            .Write((Int32)ECommand.NewerKillsOlder);
                        client.Flush();
                        client.WaitForPipeDrain();
                    }
                    catch (IOException) { continue; }
                }
            }))
            {
                IsBackground = true,
                Name = $"Try ask elder to die"
            }.Start();

            foreach (var p in old_procs)
            {
                if (p.WaitForExit(TimeSpan.FromSeconds(1)))
                    continue;

                var mb = new CustomMessageBox($"Force killing [{p.Id}]", null, WPFCommon.CurrentApp?.MainWindow);
                p.WaitForExitAsync().ContinueWith(t => mb.Dispatcher.Invoke(mb.Close));
                mb.ShowDialog();

                p.Kill();
                p.WaitForExit();
            }

            need_old_proc_kill_command = false;
            //Prompt.Notify(sw.Elapsed.ToString());
        }
    }

    private void StartAccepting() =>
        new Thread(() =>
        {
            var ps = new PipeSecurity();
            ps.AddAccessRule(new PipeAccessRule(Environment.UserName, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));

            var tasks = new List<Task>();

            while (true)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    tasks.RemoveAll(t =>
                    {
                        if (!t.IsCompleted)
                            return false;
                        Err.Handle(t.Wait);
                        return true;
                    });

                    server = NamedPipeServerStreamAcl.Create(
                        pipeName: name,
                        direction: PipeDirection.InOut,
                        maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
                        transmissionMode: PipeTransmissionMode.Byte,
                        options: PipeOptions.WriteThrough,
                        inBufferSize: 0, outBufferSize: 0,
                        pipeSecurity: ps
                    );

                    server.WaitForConnection();
                    var command = (ECommand)new BinaryReader(server).ReadInt32();

                    var instance_detach_wh = new ManualResetEventSlim(false);
                    tasks.Add(Task.Run(() =>
                    {
                        NamedPipeServerStream? l_server = null;
                        try
                        {
                            l_server = Interlocked.Exchange(ref server, null);
                            instance_detach_wh.Set();

                            Action<Stream> handler;
                            while (true)
                            {
                                if (command_handlers.TryGetValue(command, out handler!))
                                    break;
                                if (can_throw_undefined_command)
                                    throw new InvalidOperationException($"Command [{command}] not defined");
                                Thread.Sleep(10);
                            }

                            ThreadingCommon.RunWithBackgroundReset(() =>
                            {
                                handler(l_server);
                            }, new_is_background: false);

                            l_server.Flush();
                            l_server.Disconnect();
                        }
                        catch when (Common.IsShuttingDown)
                        {
                            return;
                        }
                        catch (IOException e) when (e.Message == "Pipe is broken.")
                        {
                            return; // Client disconnected
                        }
                        catch (Exception e)
                        {
                            Err.Handle(e);
                        }
                        finally
                        {
                            l_server?.Dispose();
                        }
                    }));
                    instance_detach_wh.Wait();

                }
                catch when (Common.IsShuttingDown)
                {
                    break;
                }
                catch (IOException e) when (e.Message == "Pipe is broken.")
                {
                    continue; // Client disconnected
                }
                catch (Exception e)
                {
                    Err.Handle(e);
                }
                finally
                {
                    server?.Dispose();
                }
            }
        })
        {
            IsBackground = true,
            Name = $"Commands pipe",
        }.Start();

    private Boolean can_throw_undefined_command = false;
    public void StartThrowingUndefinedCommand() => can_throw_undefined_command = true;

}

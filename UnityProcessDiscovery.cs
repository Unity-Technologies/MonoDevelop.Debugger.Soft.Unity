using System;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.Net;
using System.Linq;

namespace MonoDevelop.Debugger.Soft.Unity
{
    public static class UnityProcessDiscovery
    {
        [Flags]
        public enum GetProcessOptions
        {
            Editor = (1 << 0),
            Players = (1 << 1),
            All = Editor | Players
        };

        static readonly PlayerConnection unityPlayerConnection;

        static List<UnityProcessInfo> usbProcesses = new List<UnityProcessInfo>();
        static bool usbProcessesFinished = true;
        static object usbLock = new object();

        static List<UnityProcessInfo> unityProcesses = new List<UnityProcessInfo>();

        static bool unityProcessesFinished = true;
        static bool run = true;

        internal static Dictionary<uint, PlayerConnection.PlayerInfo> UnityPlayers { get; private set; }

        internal static ConnectorRegistry ConnectorRegistry { get; private set; }

        static UnityProcessDiscovery()
        {
            UnityPlayers = new Dictionary<uint, PlayerConnection.PlayerInfo>();
            ConnectorRegistry = new ConnectorRegistry();

            try
            {
                // HACK: Poll Unity players
                unityPlayerConnection = new PlayerConnection();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    while (run)
                    {
                        lock (unityPlayerConnection)
                        {
                            unityPlayerConnection.Poll();
                        }

                        Thread.Sleep(1000);
                    }
                });
            }
            catch (Exception e)
            {
                UnityDebug.Log.Write("Error launching player connection discovery service: Unity player discovery will be unavailable");
                UnityDebug.Log.Write(e.Message);
                Log.Error("Error launching player connection discovery service: Unity player discovery will be unavailable", e);
            }
        }

        public static void Stop()
        {
            run = false;
        }

        public static UnityAttachInfo GetUnityAttachInfo(long processId, ref IUnityDbgConnector connector)
        {
            if (ConnectorRegistry.Connectors.ContainsKey((uint)processId))
            {
                connector = ConnectorRegistry.Connectors[(uint)processId];
                return connector.SetupConnection();
            }
            else if (UnityPlayers.ContainsKey((uint)processId))
            {
                PlayerConnection.PlayerInfo player = UnityPlayers[(uint)processId];
                int port = 0 == player.m_DebuggerPort
                    ? (int)(56000 + processId % 1000)
                    : (int)player.m_DebuggerPort;
                try
                {
                    return new UnityAttachInfo(player.m_Id, player.m_IPEndPoint.Address, port);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Unable to attach to {player.m_IPEndPoint.Address}:{port}", ex);
                }
            }

            long defaultPort = 56000 + (processId % 1000);
            return new UnityAttachInfo(null, IPAddress.Loopback, (int)defaultPort);
        }

        /*public static UnityProcessInfo[] GetAttachableProcessesAsync()
        {
            List<UnityProcessInfo> processes = new List<UnityProcessInfo>();

            // Get players
            processes.AddRange(GetUnityPlayerProcesses(false));

            // Get editor
            if (unityProcessesFinished)
            {
                unityProcessesFinished = false;

                ThreadPool.QueueUserWorkItem(delegate
                {
                    unityProcesses = GetUnityEditorProcesses();
                    unityProcessesFinished = true;
                });
            }

            processes.AddRange(unityProcesses);

            // Get iOS USB
            if (usbProcessesFinished)
            {
                usbProcessesFinished = false;

                ThreadPool.QueueUserWorkItem(delegate
                {
                    // Direct USB devices
                    lock (usbLock)
                    {
                        usbProcesses = GetUnityiOSUsbProcesses();
                        usbProcessesFinished = true;
                    }
                });
            }

            processes.AddRange(usbProcesses);

            return processes.ToArray();
        }*/

        public static List<UnityProcessInfo> GetAttachableProcesses(GetProcessOptions options = GetProcessOptions.All)
        {
            List<UnityProcessInfo> processes = new List<UnityProcessInfo>();

            if ((options & GetProcessOptions.Editor) == GetProcessOptions.Editor)
                processes.AddRange(GetUnityEditorProcesses());

            if ((options & GetProcessOptions.Players) == GetProcessOptions.Players)
            {
                processes.AddRange(GetUnityPlayerProcesses(true));
                processes.AddRange(GetUnityiOSUsbProcesses());
            }

            return processes;
        }

        public static List<UnityProcessInfo> GetUnityEditorProcesses()
        {
            var unityEditorProcessNames = new[] { "Unity", "Unity Editor" };

            Process[] systemProcesses = Process.GetProcesses();
            var unityEditorProcesses = new List<UnityProcessInfo>();

            if (systemProcesses == null)
                return unityEditorProcesses;
            foreach (Process p in systemProcesses)
            {
                try
                {
                    var processName = p.ProcessName;

                    foreach (var unityEditorProcessName in unityEditorProcessNames)
                    {
                        if (processName.Equals(unityEditorProcessName, StringComparison.OrdinalIgnoreCase))
                        {
                            unityEditorProcesses.Add(new UnityProcessInfo(p.Id, $"Unity Editor ({processName})", p.MainWindowTitle));
                            UnityDebug.Log.Write($"Unity Editor process: {unityEditorProcessName} on id: {p.Id}");
                        }
                    }
                }
                catch
                {
                    // Don't care; continue
                }
            }

            return unityEditorProcesses;
        }

        public static List<UnityProcessInfo> GetUnityPlayerProcesses(bool block)
        {
            int index = 1;
            List<UnityProcessInfo> processes = new List<UnityProcessInfo>();

            if (null == unityPlayerConnection)
            {
                return processes;
            }

            UnityDebug.Log.Write("UnityPlayerConnection is constructed");
            if (block)
            {
                Monitor.Enter(unityPlayerConnection);

                UnityDebug.Log.Write("Known size of available Players: " + unityPlayerConnection.AvailablePlayers.Count());
                for (int i = 0; i < 12 && !unityPlayerConnection.AvailablePlayers.Any(); ++i)
                {
                    unityPlayerConnection.Poll();
                    Thread.Sleep(250);
                }
            }
            else if (!Monitor.TryEnter(unityPlayerConnection))
            {
                return processes;
            }

            UnityDebug.Log.Write("New size of available Players: " + unityPlayerConnection.AvailablePlayers.Count());
            foreach (var availablePlayer in unityPlayerConnection.AvailablePlayers)
            {
                UnityDebug.Log.Write($"Available player: {availablePlayer}");
            }

            try
            {
                foreach (string player in unityPlayerConnection.AvailablePlayers)
                {
                    try
                    {
                        PlayerConnection.PlayerInfo info = PlayerConnection.PlayerInfo.Parse(player);
                        if (info.m_AllowDebugging)
                        {
                            UnityPlayers[info.m_Guid] = info;
                            processes.Add(new UnityProcessInfo(info.m_Guid, info.m_Id, info.m_ProjectName));
                            ++index;
                        }
                    }
                    catch (Exception e)
                    {
                        UnityDebug.Log.Write($"{player}: could not be parsed: {e.Message}");
                        // Don't care; continue
                    }
                }
            }
            finally
            {
                Monitor.Exit(unityPlayerConnection);
            }

            return processes;
        }

        public static List<UnityProcessInfo> GetUnityiOSUsbProcesses()
        {
            var processes = new List<UnityProcessInfo>();

            try
            {
                iOSDevices.GetUSBDevices(ConnectorRegistry, processes);
            }
            catch (NotSupportedException)
            {
                Log.Info("iOS over USB not supported on this platform");
            }
            catch (Exception e)
            {
                Log.Info("iOS USB Error: " + e);
            }

            return processes;
        }
    }

    // Allows to define how to setup and tear down connection for debugger to connect to the
    // debugee. For example to setup TCP tunneling over USB.
    public interface IUnityDbgConnector
    {
        UnityAttachInfo SetupConnection();
        void OnDisconnect();
    }

    public class ConnectorRegistry
    {
        // This is used to map process id <-> unique string id. MonoDevelop attachment is built
        // around process ids.
        object processIdLock = new object();
        uint nextProcessId = 1000000;
        Dictionary<uint, string> processIdToUniqueId = new Dictionary<uint, string>();
        Dictionary<string, uint> uniqueIdToProcessId = new Dictionary<string, uint>();

        public Dictionary<uint, IUnityDbgConnector> Connectors { get; private set; }

        public uint GetProcessIdForUniqueId(string uid)
        {
            lock (processIdLock)
            {
                uint processId;
                if (uniqueIdToProcessId.TryGetValue(uid, out processId))
                    return processId;

                processId = nextProcessId++;
                processIdToUniqueId.Add(processId, uid);
                uniqueIdToProcessId.Add(uid, processId);

                return processId;
            }
        }

        public string GetUniqueIdFromProcessId(uint processId)
        {
            lock (processIdLock)
            {
                string uid;
                if (processIdToUniqueId.TryGetValue(processId, out uid))
                    return uid;

                return null;
            }
        }

        public ConnectorRegistry()
        {
            Connectors = new Dictionary<uint, IUnityDbgConnector>();
        }
    }
}

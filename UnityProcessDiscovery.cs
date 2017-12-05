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

		static List<UnityProcessInfo> unityProcesses = new List<UnityProcessInfo> ();

		static bool unityProcessesFinished = true;
		static bool run = true;

		internal static Dictionary<uint, PlayerConnection.PlayerInfo> UnityPlayers {
			get;
			private set;
		}

		internal static ConnectorRegistry ConnectorRegistry { get; private set; }

		static UnityProcessDiscovery ()
		{
			UnityPlayers = new Dictionary<uint, PlayerConnection.PlayerInfo> ();
			ConnectorRegistry = new ConnectorRegistry();

			try {
				// HACK: Poll Unity players
				unityPlayerConnection = new PlayerConnection ();
				ThreadPool.QueueUserWorkItem (delegate {
					while (run) {
						lock (unityPlayerConnection) {
							unityPlayerConnection.Poll ();
						}
						Thread.Sleep (1000);
					}
				});
			} 
			catch (Exception e)
			{
				Log.Error ("Error launching player connection discovery service: Unity player discovery will be unavailable", e);
			}
		}

		public static void Stop()
		{
			run = false;
		}

		public static UnityAttachInfo GetUnityAttachInfo(long processId, ref IUnityDbgConnector connector)
		{
			if (ConnectorRegistry.Connectors.ContainsKey((uint)processId)) {
				connector = ConnectorRegistry.Connectors[(uint)processId];
				return connector.SetupConnection();
			} else if (UnityProcessDiscovery.UnityPlayers.ContainsKey ((uint)processId)) {
				PlayerConnection.PlayerInfo player = UnityProcessDiscovery.UnityPlayers[(uint)processId];
				int port = (0 == player.m_DebuggerPort
					? (int)(56000 + (processId % 1000))
					: (int)player.m_DebuggerPort);
				try {
					return new UnityAttachInfo (player.m_Id, player.m_IPEndPoint.Address, (int)port);
				} catch (Exception ex) {
					throw new Exception (string.Format ("Unable to attach to {0}:{1}", player.m_IPEndPoint.Address, port), ex);
				}
			}

			long defaultPort = 56000 + (processId % 1000);
			return new UnityAttachInfo (null, IPAddress.Loopback, (int)defaultPort);
		}

		public static UnityProcessInfo[] GetAttachableProcessesAsync ()
		{
			List<UnityProcessInfo> processes = new List<UnityProcessInfo> ();

			// Get players
			processes.AddRange (GetUnityPlayerProcesses (false));

			// Get editor
			if (unityProcessesFinished) 
			{
				unityProcessesFinished = false;

				ThreadPool.QueueUserWorkItem (delegate 
				{
					unityProcesses = GetUnityEditorProcesses();
					unityProcessesFinished = true;
				});
			}

			processes.AddRange (unityProcesses);

			// Get iOS USB
			if (usbProcessesFinished)
			{
				usbProcessesFinished = false;

				ThreadPool.QueueUserWorkItem (delegate {
					// Direct USB devices
					lock(usbLock)
					{
						usbProcesses = GetUnityiOSUsbProcesses();
						usbProcessesFinished = true;
					}
				});
			}

			processes.AddRange (usbProcesses);

			return processes.ToArray ();
		}

		public static List<UnityProcessInfo> GetAttachableProcesses (GetProcessOptions options = GetProcessOptions.All)
		{
			List<UnityProcessInfo> processes = new List<UnityProcessInfo> ();

			if((options & GetProcessOptions.Editor) == GetProcessOptions.Editor)
				processes.AddRange (GetUnityEditorProcesses ());
	
			if ((options & GetProcessOptions.Players) == GetProcessOptions.Players) {
				processes.AddRange (GetUnityPlayerProcesses (true));
				processes.AddRange (GetUnityiOSUsbProcesses ());
			}

			return processes;
		}

		public static List<UnityProcessInfo> GetUnityEditorProcesses()
		{
			StringComparison comparison = StringComparison.OrdinalIgnoreCase;

			Process[] systemProcesses = Process.GetProcesses();
			var unityEditorProcesses = new List<UnityProcessInfo>();

			if (systemProcesses != null) {
				foreach (Process p in systemProcesses) {
					try {
						if (((p.ProcessName.StartsWith ("unity", comparison) && !p.ProcessName.StartsWith ("unity-", comparison)) ||
							p.ProcessName.Contains ("Unity.app")) &&
							!p.ProcessName.Contains ("unityiproxy") &&
							!p.ProcessName.Contains ("UnityDebug") &&
							!p.ProcessName.Contains ("UnityShader") &&
							!p.ProcessName.Contains ("UnityHelper") &&
							!p.ProcessName.Contains ("Unity Helper") &&
							!p.ProcessName.Contains ("UnityCrashHandler"))
                        {
							unityEditorProcesses.Add (new UnityProcessInfo (p.Id, string.Format ("{0} ({1})", "Unity Editor", p.ProcessName)));
						}
					} catch {
						// Don't care; continue
					}
				}
			}

			return unityEditorProcesses;
		}

		public static List<UnityProcessInfo> GetUnityPlayerProcesses(bool block)
		{
			int index = 1;
			List<UnityProcessInfo> processes = new List<UnityProcessInfo> ();

			if (null != unityPlayerConnection) 
			{
				if (block)
				{
					Monitor.Enter (unityPlayerConnection);

					for (int i = 0; i < 12 && !unityPlayerConnection.AvailablePlayers.Any (); ++i) {
						unityPlayerConnection.Poll ();
						Thread.Sleep (250);
					}
				}
				else
					if(!Monitor.TryEnter(unityPlayerConnection))
						return processes;
				try 
				{
					foreach (string player in unityPlayerConnection.AvailablePlayers) {
						try {
							PlayerConnection.PlayerInfo info = PlayerConnection.PlayerInfo.Parse (player);
							if (info.m_AllowDebugging) {
								UnityPlayers[info.m_Guid] = info;
								processes.Add (new UnityProcessInfo (info.m_Guid, info.m_Id));
								++index;
							}
						} catch {
							// Don't care; continue
						}
					}
				}
				finally 
				{
					Monitor.Exit (unityPlayerConnection);
				}
			}

			return processes;
		}

		public static List<UnityProcessInfo> GetUnityiOSUsbProcesses()
		{
			var processes = new List<UnityProcessInfo>();

			try
			{
				iOSDevices.GetUSBDevices (ConnectorRegistry, processes);
			}
			catch(NotSupportedException)
			{
				Log.Info("iOS over USB not supported on this platform");
			}
			catch(Exception e)
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
			lock (processIdLock) {
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


using System;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using Mono.Debugging.Soft;

namespace MonoDevelop.Debugger.Soft.Unity
{
	public static class UnityProcessDiscovery
	{
		public interface ILogger
		{
			void Log (string message);
		}

		static List<ILogger> loggers = new List<ILogger>();
		static readonly PlayerConnection unityPlayerConnection;

		#if UNITY_IOS_USB_ATTACH
		static List<UnityProcessInfo> usbProcesses = new List<UnityProcessInfo>();
		static bool usbProcessesFinished = true;
		static object usbLock = new object();
		#endif

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
				Log ("Error launching player connection discovery service: Unity player discovery will be unavailable\n" + e);
			}
		}

		static void Log(string message)
		{
			foreach (var logger in loggers)
				logger.Log (message);
		}

		public static void AddLogger(ILogger logger)
		{
			loggers.Add (logger);
		}

		public static void Stop()
		{
			run = false;
		}

		public static UnityProcessInfo[] GetAttachableProcesses ()
		{
			int index = 1;
			List<UnityProcessInfo> processes = new List<UnityProcessInfo> ();

			StringComparison comparison = StringComparison.OrdinalIgnoreCase;

			if (null != unityPlayerConnection) {
				if(Monitor.TryEnter (unityPlayerConnection)) {
					try {
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
					finally {
						Monitor.Exit (unityPlayerConnection);
					}
				}
			}

			if (unityProcessesFinished) 
			{
				unityProcessesFinished = false;

				ThreadPool.QueueUserWorkItem (delegate {

					Process[] systemProcesses = Process.GetProcesses();
					var unityThreadProcesses = new List<UnityProcessInfo>();

					if(systemProcesses != null)
					{
						foreach (Process p in systemProcesses) {
							try {
								if ((p.ProcessName.StartsWith ("unity", comparison) ||
									p.ProcessName.Contains ("Unity.app")) &&
									!p.ProcessName.Contains ("UnityShader") &&
									!p.ProcessName.Contains ("UnityHelper") &&
									!p.ProcessName.Contains ("Unity Helper")) {
									unityThreadProcesses.Add (new UnityProcessInfo (p.Id, string.Format ("{0} ({1})", "Unity Editor", p.ProcessName)));
								}
							} catch {
								// Don't care; continue
							}
						}

						unityProcesses = unityThreadProcesses;
						unityProcessesFinished = true;
					}
				});
			}

			processes.AddRange (unityProcesses);

			#if UNITY_IOS_USB_ATTACH
			if (usbProcessesFinished)
			{
				usbProcessesFinished = false;

				ThreadPool.QueueUserWorkItem (delegate {
					// Direct USB devices
					lock(usbLock)
					{
						var usbThreadProcesses = new List<UnityProcessInfo>();

						try
						{
							iOSDevices.GetUSBDevices (ConnectorRegistry, usbThreadProcesses);
						}
						catch(NotSupportedException)
						{
							Log("iOS over USB not supported on this platform");
						}
						catch(Exception e)
						{
							Log("iOS USB Error: " + e);
						}
						usbProcesses = usbThreadProcesses;
						usbProcessesFinished = true;
					}
				});
			}

			processes.AddRange (usbProcesses);
			#endif

			return processes.ToArray ();
		}
	}

	// Allows to define how to setup and tear down connection for debugger to connect to the
	// debugee. For example to setup TCP tunneling over USB.
	public interface IUnityDbgConnector
	{
		SoftDebuggerStartInfo SetupConnection();
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


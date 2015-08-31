using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using Mono.Debugging.Client;
using Mono.Debugging.Soft;
using MonoDevelop.Core;

namespace MonoDevelop.Debugger.Soft.Unity
{
	class iOSUsbConnector: IUnityDbgConnector
	{
		readonly string udid;
		readonly ushort port = 12000;

		public SoftDebuggerStartInfo SetupConnection()
		{
			Usbmuxd.StartIosProxy(port, 56000, udid);
			var args = new SoftDebuggerConnectArgs(udid, IPAddress.Loopback, port);
			return new SoftDebuggerStartInfo(args);
		}

		public void OnDisconnect()
		{
			Usbmuxd.StopIosProxy(port);
		}

		public iOSUsbConnector(string udid)
		{
			this.udid = udid;
		}
	}


	static class Usbmuxd
	{
		// Note: This struct is used in .Net for interop. so do not change it, or know what
		//       you are doing!
		[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Ansi)]
		public struct iOSDevice
		{
			public int    productId;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst=41)]
			public string udid;
		};

		const string nativeDllOsx = "UnityEditor.iOS.Extensions.Native.dylib";
		const string nativeDllWin32 = "x86\\UnityEditor.iOS.Extensions.Native.dll";
		const string nativeDllWin64 = "x86_64\\UnityEditor.iOS.Extensions.Native.dll";
		static IDllLoader loader;
		static IntPtr dllHandle;


		public delegate bool StartIosProxyDelegate(ushort localPort, ushort devicePort, [MarshalAs(UnmanagedType.LPStr)] string deviceId);
		public delegate void StopIosProxyDelegate(ushort localPort);
		public delegate void StartUsbmuxdListenThreadDelegate();
		public delegate void StopUsbmuxdListenThreadDelegate();
		public delegate uint UsbmuxdGetDeviceCountDelegate();
		public delegate bool UsbmuxdGetDeviceDelegate(uint index, out iOSDevice device);


		public static StartIosProxyDelegate StartIosProxy;
		public static StopIosProxyDelegate StopIosProxy;
		public static StartUsbmuxdListenThreadDelegate StartUsbmuxdListenThread;
		public static StopUsbmuxdListenThreadDelegate StopUsbmuxdListenThread;
		public static UsbmuxdGetDeviceCountDelegate UsbmuxdGetDeviceCount;
		public static UsbmuxdGetDeviceDelegate UsbmuxdGetDevice;


		public static bool Supported { get { return loader != null; } }
		public static bool IsDllLoaded { get { return dllHandle != IntPtr.Zero; } }


		static void LoadDll(string nativeDll)
		{
			if (dllHandle == IntPtr.Zero)
			{
				dllHandle = loader.LoadLibrary(nativeDll);
				if (dllHandle != IntPtr.Zero)
					Console.WriteLine("Loaded: " + nativeDll);
				else
					Console.WriteLine("Couldn't load: " + nativeDll);
			}
		}


		static TType LoadFunction<TType>(string name) where TType: class
		{
			if (dllHandle == IntPtr.Zero)
				throw new Exception("iOS native extension dll was not loaded");

			IntPtr addr = loader.GetProcAddress(dllHandle, name);
			return Marshal.GetDelegateForFunctionPointer(addr, typeof(TType)) as TType;
		}


		static void InitFunctions()
		{
			StartUsbmuxdListenThread = LoadFunction<StartUsbmuxdListenThreadDelegate>("StartUsbmuxdListenThread");
			StopUsbmuxdListenThread = LoadFunction<StopUsbmuxdListenThreadDelegate>("StopUsbmuxdListenThread");
			UsbmuxdGetDeviceCount = LoadFunction<UsbmuxdGetDeviceCountDelegate>("UsbmuxdGetDeviceCount");
			UsbmuxdGetDevice = LoadFunction<UsbmuxdGetDeviceDelegate>("UsbmuxdGetDevice");
			StartIosProxy = LoadFunction<StartIosProxyDelegate>("StartIosProxy");
			StopIosProxy = LoadFunction<StopIosProxyDelegate>("StopIosProxy");
		}


		public static void Setup(string dllPath)
		{
			if (Platform.IsMac)
				dllPath = Path.Combine(dllPath, nativeDllOsx);
			else if (Platform.IsWindows && Environment.Is64BitProcess)
				dllPath = Path.Combine(dllPath, nativeDllWin64);
			else if (Platform.IsWindows)
				dllPath = Path.Combine(dllPath, nativeDllWin32);

			LoadDll(dllPath);
			InitFunctions();
		}

		static Usbmuxd() {
			if (Platform.IsMac)
				loader = new PosixDllLoader();
			else if (Platform.IsWindows)
				loader = new WindowsDllLoader();
			else
				throw new NotSupportedException("Platform not supported");
		}
	}


	interface IDllLoader {
		IntPtr LoadLibrary(string fileName);
		void FreeLibrary(IntPtr handle);
		IntPtr GetProcAddress(IntPtr dllHandle, string name);
	}


	class PosixDllLoader: IDllLoader
	{
		const int RTLD_LAZY = 1;
		const int RTLD_NOW = 2;


		[DllImport("libdl")]
		private static extern IntPtr dlopen(String fileName, int flags);
		
		[DllImport("libdl")]
		private static extern IntPtr dlsym(IntPtr handle, String symbol);
		
		[DllImport("libdl")]
		private static extern int dlclose(IntPtr handle);
		
		[DllImport("libdl")]
		private static extern IntPtr dlerror();


		public IntPtr LoadLibrary(string fileName)
		{
			if (!File.Exists (fileName))
				throw new FileNotFoundException (fileName);

			// clear previous errors if any
			dlerror();
			var res = dlopen(fileName, RTLD_NOW);
			var err = dlerror();
			if (res == IntPtr.Zero) {
				throw new Exception("dlopen: " + Marshal.PtrToStringAnsi(err));
			}
			return res;
		}


		public void FreeLibrary(IntPtr handle)
		{
			dlclose(handle);
		}


		public IntPtr GetProcAddress(IntPtr dllHandle, string name)
		{
			// clear previous errors if any
			dlerror();
			var res = dlsym(dllHandle, name);
			var errPtr = dlerror();
			if (errPtr != IntPtr.Zero) {
				throw new Exception("dlsym: " + Marshal.PtrToStringAnsi(errPtr));
			}
			return res;
		}
	}


	public class WindowsDllLoader: IDllLoader {
		void IDllLoader.FreeLibrary(IntPtr handle) {
			FreeLibrary(handle);
		}


		IntPtr IDllLoader.GetProcAddress(IntPtr dllHandle, string name) {
			return GetProcAddress(dllHandle, name);
		}


		IntPtr IDllLoader.LoadLibrary(string fileName) {
			if (!File.Exists (fileName))
				throw new FileNotFoundException (fileName);

			return LoadLibrary(fileName);
		}


		[DllImport("kernel32.dll")]
		private static extern IntPtr LoadLibrary(string fileName);

		[DllImport("kernel32.dll")]
		private static extern int FreeLibrary(IntPtr handle);

		[DllImport("kernel32.dll")]
		private static extern IntPtr GetProcAddress (IntPtr handle, string procedureName);
	}


	public struct iOSDeviceDescription
	{
		readonly public int vendorId;
		readonly public int productId;
		readonly public string type; 
		readonly public string model; 

		public iOSDeviceDescription(int vendorId, int productId, string type, string model)
		{
			this.vendorId = vendorId;
			this.productId = productId;
			this.type = type;
			this.model = model;
		}
	}


	static class iOSDevices
	{
		static iOSDeviceDescription[] descriptions;
		static string oldUnityPath;


		public static iOSDeviceDescription[] LoadDescriptions(string path)
		{
			var devices = new List<iOSDeviceDescription>();

			string[] lines = File.ReadAllLines(path);
			foreach (var line in lines)
			{
				string[] cols = line.Split(';');

				// Skip comment
				if ((cols.Length > 0) && (cols[0].Length > 0) && (cols[0][0] == '#'))
					continue;

				if (cols.Length < 4)
					continue;

				int vendorId;
				int productId;
				string type = cols[2];
				string model = cols[3];

				if (!int.TryParse(cols[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vendorId))
					continue;

				if (!int.TryParse(cols[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out productId))
					continue;

				devices.Add(new iOSDeviceDescription(vendorId, productId, type, model));
			}

			return devices.ToArray();
		}


		public static bool FindDescription(int vendorId, int productId, out iOSDeviceDescription device)
		{
			foreach (var d in descriptions)
			{
				if ((d.productId == productId) && (d.vendorId == vendorId))
				{
					device = d;
					return true;
				}
			}

			device = default(iOSDeviceDescription);
			return false;
		}


		static void SetupDescriptions(string path)
		{
			path = Path.Combine(path, "Data", "iosdevices.csv");
			try {
				descriptions = LoadDescriptions(path);
			} catch (Exception e) {
				LoggingService.LogWarning("Failed to load: " + path, e);
				descriptions = new iOSDeviceDescription[0];
			}
		}


		static bool SetupDll(string path)
		{
			try {
				Usbmuxd.Setup(path);
				if (Usbmuxd.IsDllLoaded)
					Usbmuxd.StartUsbmuxdListenThread();
				return true;
			} catch (Exception e) {
				LoggingService.LogWarning("Error while initializing usbmuxd", e);
				return false;
			}
		}


		static bool Setup()
		{
			string path = Path.Combine(Util.UnityEditorDataFolder, "PlaybackEngines", "iOSSupport");

			// Don't try to load again if it already failed once
			if (oldUnityPath == path)
				return false;
			else
				oldUnityPath = path;

			SetupDescriptions(path);
			return SetupDll(path);
		}

		public static bool Supported
		{
			get {
				return Usbmuxd.Supported;
			}
		}

		public static bool Initialized
		{
			get
			{
				return Usbmuxd.IsDllLoaded || Setup();
			}
		}

		public static void GetUSBDevices(ConnectorRegistry connectors, List<ProcessInfo> processes)
		{
			if (!Initialized)
				return;

			try {
				uint count = Usbmuxd.UsbmuxdGetDeviceCount();
				for (uint i = 0; i < count; i++) {
					var device = new Usbmuxd.iOSDevice();
					if (Usbmuxd.UsbmuxdGetDevice(i, out device) && !string.IsNullOrEmpty(device.udid)) {
						var name = GetNameForDevice(device);
						var processId = connectors.GetProcessIdForUniqueId(device.udid);

						processes.Add(new ProcessInfo(processId, "Unity iOS USB: " + name));
						connectors.Connectors[processId] = new iOSUsbConnector(device.udid);
					}
				}
			} catch (Exception e) {
				LoggingService.LogError("Error while getting USB devices", e);
			}
		}


		static string GetNameForDevice(Usbmuxd.iOSDevice device)
		{
			iOSDeviceDescription description;
			if (FindDescription(0x05AC, device.productId, out description))
				return description.type + " (" + description.model + ")";
			else
				return "Unknown iOS device";
		}
	}
}

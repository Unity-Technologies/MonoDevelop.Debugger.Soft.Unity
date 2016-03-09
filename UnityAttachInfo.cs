using System.Net;

namespace MonoDevelop.Debugger.Soft.Unity
{
	public class UnityAttachInfo
	{
		public string AppName { get; private set; }
		public IPAddress Address { get; private set; }
		public int Port { get; private set; }

		public UnityAttachInfo (string appName, IPAddress address, int port)
		{
			AppName = appName;
			Address = address;
			Port = port;
		}
	}
}


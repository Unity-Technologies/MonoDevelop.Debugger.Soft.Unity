namespace MonoDevelop.Debugger.Soft.Unity
{
	public static class Platform
	{
		public static bool IsLinux
		{
			get { return MonoDevelop.Core.Platform.IsLinux; }
		}

		public static bool IsMac
		{
			get { return MonoDevelop.Core.Platform.IsMac; }
		}

		public static bool IsWindows
		{
			get { return MonoDevelop.Core.Platform.IsWindows; }
		}
	}
}


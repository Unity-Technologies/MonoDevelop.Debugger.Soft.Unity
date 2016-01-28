using MonoDevelop.Ide.WelcomePage;
using Gtk;

namespace MonoDevelop.Ide.Unity
{
	[Mono.Addins.Extension]	
	public class WelcomePageProvider : IWelcomePageProvider
	{
		public Widget CreateWidget ()
		{
			return new UnityWelcomePage ();
		}
	}
}


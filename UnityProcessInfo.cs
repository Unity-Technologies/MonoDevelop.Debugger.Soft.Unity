namespace MonoDevelop.Debugger.Soft.Unity
{
	public class UnityProcessInfo
	{	
		public long Id { get; set; }
		public string Name { get; set; }

		public UnityProcessInfo(long id, string name)
		{
			Id = id;
			Name = name;
		}
	}
}


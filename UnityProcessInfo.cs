namespace MonoDevelop.Debugger.Soft.Unity
{
	public class UnityProcessInfo
	{
		public long Id { get; set; }
		public string Name { get; set; }
		public string ProjectName { get; set; }

		public UnityProcessInfo(long id, string name, string projectName)
		{
			Id = id;
			Name = name;
			ProjectName = projectName;
		}
	}
}

